#include "application/cadence_tick_driver.hpp"

#include "application/capture_work_item.hpp"
#include "shared/enums.hpp"

#include <boost/json/object.hpp>

#include <utility>

namespace dovahlink::application {

CadenceTickDriver::CadenceTickDriver(ICadenceScheduler& scheduler,
                                     ICaptureDispatchWorker& worker,
                                     ITaskMarshaller& taskMarshaller,
                                     std::chrono::milliseconds tickInterval)
    : scheduler_(scheduler), worker_(worker), taskMarshaller_(taskMarshaller),
      tickInterval_(tickInterval) {}

CadenceTickDriver::~CadenceTickDriver() {
    Stop();
    Join();
}

void CadenceTickDriver::Start() {
    thread_ = std::thread([this] { TickerLoop(); });
}

void CadenceTickDriver::Stop() {
    {
        std::lock_guard<std::mutex> lock(mutex_);
        stopping_ = true;
    }
    stopSignal_.notify_all();
}

void CadenceTickDriver::Join() {
    if (thread_.joinable()) {
        thread_.join();
    }
}

void CadenceTickDriver::TickerLoop() {
    std::unique_lock<std::mutex> lock(mutex_);
    while (!stopSignal_.wait_for(lock, tickInterval_,
                                 [this] { return stopping_; })) {
        lock.unlock();
        try {
            taskMarshaller_.RunOnGameThread([this] { OnGameThreadTick(); });
        } catch (...) {
            //  Per ai/context/skse/cpp-style.md's "Ownership and lifetime":
            //  never allow an exception to escape a worker-thread boundary.
            //  One failed marshal attempt does not stop future ticks.
        }
        lock.lock();
    }
}

void CadenceTickDriver::OnGameThreadTick() {
    //  Runs on the game thread; per ai/context/skse/architecture.md's
    //  "Threading and callbacks", an exception here must never escape onto
    //  SKSE's own call stack.
    try {
        auto now = std::chrono::steady_clock::now();
        for (const auto& key : scheduler_.DueKeys(now)) {
            CaptureWorkItem item{
                .stateArea = key,
                .mode = CaptureMode::kSnapshot,
                .buildData = [] { return boost::json::object{}; },
                .occurredAt = std::chrono::system_clock::now(),
            };
            (void)worker_.TryEnqueue(std::move(item));
        }
    } catch (...) {
    }
}

} //  namespace dovahlink::application
