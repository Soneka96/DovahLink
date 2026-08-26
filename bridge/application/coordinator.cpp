#include "application/coordinator.hpp"

#include "application/bridge_transport.hpp"
#include "application/bridge_worker_pool.hpp"

#include <utility>

namespace dovahlink::application {

Coordinator::Coordinator(IBridgeCallbackRegistry& callbacks,
                         IBridgeWorkerPool& workers, IBridgeTransport& transport)
    : callbacks_(callbacks), workers_(workers), transport_(transport) {}

Coordinator::~Coordinator() noexcept { Shutdown(); }

void Coordinator::Start() {
    std::unique_lock<std::mutex> lifecycleLock(lifecycleMutex_);
    {
        std::lock_guard<std::mutex> lock(mutex_);
        if (startClaimed_ || shutdownStarted_ ||
            stopping_.load(std::memory_order_acquire)) {
            return;
        }
        startClaimed_ = true;
    }

    callbacks_.RegisterAll([this](ContainedWork work) noexcept {
        return RunCallbackContained(std::move(work));
    });
    workers_.Start([this](ContainedWork work) noexcept {
        return RunContained(std::move(work));
    });
    transport_.Start();
}

void Coordinator::Shutdown() noexcept {
    std::unique_lock<std::mutex> lifecycleLock(lifecycleMutex_);
    {
        std::unique_lock<std::mutex> lock(mutex_);
        if (shutdownStarted_) {
            lock.unlock();
            shutdownComplete_.wait(false, std::memory_order_acquire);
            return;
        }
        shutdownStarted_ = true;
    }

    //  Publish completion even if synchronization or future non-lifecycle work
    //  throws after this point. Atomic wait/notify avoids requiring a second
    //  fallible mutex acquisition while unwinding.
    ShutdownCompletionGuard completionSignal(*this);

    //  1. Mark stopping so a new CallbackGuard returns without touching state.
    stopping_.store(true, std::memory_order_release);

    //  2. Unregister callbacks.
    (void)RunContained([this] { callbacks_.UnregisterAll(); });

    //  3. Wait for callbacks already in flight to leave.
    {
        std::unique_lock<std::mutex> lock(mutex_);
        inFlightCv_.wait(lock, [this] { return inFlightCallbacks_ == 0; });
    }

    //  4. Stop and join workers. Draining/cancelling queued work is the
    //     worker pool implementation's responsibility (see IBridgeWorkerPool::Stop).
    (void)RunContained([this] { workers_.Stop(); });
    (void)RunContained([this] { workers_.Join(); });

    //  5. Cancel transport completions.
    (void)RunContained([this] { transport_.CancelCompletions(); });

    //  6. Invalidate the transport lifetime token: an in-flight completion
    //     that captured it now sees IsValid() == false.
    transportToken_->Invalidate();

    //  7. Close transport resources.
    (void)RunContained([this] { transport_.Close(); });
}

bool Coordinator::IsStopping() const {
    return stopping_.load(std::memory_order_acquire);
}

std::shared_ptr<LifetimeToken>
Coordinator::TransportLifetimeTokenHandle() const {
    return transportToken_;
}

bool Coordinator::IsAvailable() const {
    return available_.load(std::memory_order_acquire);
}

void Coordinator::RegisterFailure() {
    available_.store(false, std::memory_order_release);
}

void Coordinator::ResetAvailability() {
    available_.store(true, std::memory_order_release);
}

bool Coordinator::RunContained(ContainedWork work) noexcept {
    try {
        work();
        return true;
    } catch (...) {
        RegisterFailure();
        return false;
    }
}

bool Coordinator::RunCallbackContained(ContainedWork work) noexcept {
    CallbackGuard guard(*this);
    if (!guard.ShouldProceed()) {
        return false;
    }
    return RunContained(std::move(work));
}

Coordinator::ShutdownCompletionGuard::ShutdownCompletionGuard(
    Coordinator& coordinator) noexcept
    : coordinator_(coordinator) {}

Coordinator::ShutdownCompletionGuard::~ShutdownCompletionGuard() noexcept {
    coordinator_.shutdownComplete_.store(true, std::memory_order_release);
    coordinator_.shutdownComplete_.notify_all();
}

Coordinator::CallbackGuard::CallbackGuard(Coordinator& coordinator)
    : coordinator_(coordinator) {
    std::lock_guard<std::mutex> lock(coordinator_.mutex_);
    if (coordinator_.stopping_.load(std::memory_order_acquire)) {
        proceed_ = false;
        return;
    }
    ++coordinator_.inFlightCallbacks_;
    proceed_ = true;
}

Coordinator::CallbackGuard::~CallbackGuard() {
    if (!proceed_) {
        return;
    }
    {
        std::lock_guard<std::mutex> lock(coordinator_.mutex_);
        --coordinator_.inFlightCallbacks_;
    }
    coordinator_.inFlightCv_.notify_all();
}

bool Coordinator::CallbackGuard::ShouldProceed() const { return proceed_; }

} //  namespace dovahlink::application
