#include "application/cadence_tick_driver.hpp"

#include "application/capture_work_item.hpp"
#include "shared/enums.hpp"

#include <boost/json/object.hpp>

#include <utility>

namespace dovahlink::application {

CadenceTickDriver::CadenceTickDriver(ICadenceScheduler& scheduler,
                                     ICaptureDispatchWorker& worker,
                                     ITaskMarshaller& taskMarshaller,
                                     IActivePlayContextProvider& activeContext,
                                     std::chrono::milliseconds tickInterval)
    : scheduler_(scheduler), worker_(worker), taskMarshaller_(taskMarshaller),
      activeContext_(activeContext), tickInterval_(tickInterval),
      callbackState_(std::make_shared<CallbackState>()) {
    callbackState_->owner = this;
}

CadenceTickDriver::~CadenceTickDriver() {
    Stop();
    Join();
    std::lock_guard<std::mutex> lock(callbackState_->mutex);
    callbackState_->owner = nullptr;
}

void CadenceTickDriver::Start() {
    {
        std::lock_guard<std::mutex> lock(mutex_);
        started_ = true;
    }
    thread_ = std::thread([this] { TickerLoop(); });
}

void CadenceTickDriver::Stop() {
    {
        std::lock_guard<std::mutex> lock(mutex_);
        if (!started_) {
            return;
        }
        stopping_ = true;
    }
    {
        std::lock_guard<std::mutex> lock(callbackState_->mutex);
        callbackState_->cancelled = true;
    }
    stopSignal_.notify_all();
}

void CadenceTickDriver::Join() {
    if (thread_.joinable()) {
        thread_.join();
    }
    std::unique_lock<std::mutex> lock(callbackState_->mutex);
    callbackState_->changed.wait(
        lock, [this] { return callbackState_->callbacksInFlight == 0; });
}

void CadenceTickDriver::TickerLoop() {
    std::unique_lock<std::mutex> lock(mutex_);
    while (!stopSignal_.wait_for(lock, tickInterval_,
                                 [this] { return stopping_; })) {
        lock.unlock();
        auto callbackState = callbackState_;
        bool shouldQueue = false;
        {
            std::lock_guard<std::mutex> callbackLock(callbackState->mutex);
            if (!callbackState->cancelled && !callbackState->tickPending) {
                callbackState->tickPending = true;
                shouldQueue = true;
            }
        }
        if (!shouldQueue) {
            lock.lock();
            continue;
        }
        try {
            taskMarshaller_.RunOnGameThread(
                [callbackState] { RunQueuedTick(std::move(callbackState)); });
        } catch (...) {
            //  Per ai/context/skse/cpp-style.md's "Ownership and lifetime":
            //  never allow an exception to escape a worker-thread boundary.
            //  One failed marshal attempt does not stop future ticks.
            {
                std::lock_guard<std::mutex> callbackLock(callbackState->mutex);
                callbackState->tickPending = false;
            }
            callbackState->changed.notify_all();
        }
        lock.lock();
    }
}

void CadenceTickDriver::RunQueuedTick(
    std::shared_ptr<CallbackState> state) noexcept {
    CadenceTickDriver* owner = nullptr;
    {
        std::lock_guard<std::mutex> lock(state->mutex);
        if (state->cancelled || state->owner == nullptr) {
            state->tickPending = false;
            state->changed.notify_all();
            return;
        }
        ++state->callbacksInFlight;
        owner = state->owner;
    }

    try {
        owner->OnGameThreadTick();
    } catch (...) {
        //  Keep the queued callback a noexcept boundary even if the driver's
        //  implementation changes its own containment later.
    }

    {
        std::lock_guard<std::mutex> lock(state->mutex);
        --state->callbacksInFlight;
        state->tickPending = false;
    }
    state->changed.notify_all();
}

void CadenceTickDriver::OnGameThreadTick() {
    //  Runs on the game thread; an exception here must never escape onto
    //  SKSE's own call stack.
    try {
        //  Pinned once, before the due-key check, and reused for every due
        //  key this tick -- the same atomic guard-then-pin shape native-event
        //  capture uses, so no due-key check or capture happens while
        //  loading or before an authoritative play context exists.
        auto context = activeContext_.CurrentPlayContext();
        if (!context) {
            return;
        }
        auto now = std::chrono::steady_clock::now();
        for (const auto& key : scheduler_.DueKeys(now)) {
            //  The closure always reports no change: no caller ever
            //  registers a key with `scheduler_`, so `DueKeys` never returns
            //  one and this loop body never runs in production today. It is
            //  not a template for Phase 4.3's real per-domain apply/compare
            //  logic -- there is no domain-independent abstraction here to
            //  build one from, since a sampled value's shape is inherently
            //  domain-specific. Whichever phase registers the first sampled
            //  domain supplies its own closure for that key.
            CaptureWorkItem item{
                .playContext = context,
                .stateArea = key,
                .mode = CaptureMode::kSnapshot,
                .applyAndBuildIfChanged =
                    [](PlayContext&) -> std::optional<boost::json::object> {
                    return std::nullopt;
                },
                .occurredAt = std::chrono::system_clock::now(),
            };
            (void)worker_.TryEnqueue(std::move(item));
        }
    } catch (...) {
    }
}

} //  namespace dovahlink::application
