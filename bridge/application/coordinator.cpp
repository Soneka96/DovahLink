#include "application/coordinator.hpp"

namespace dovahlink::application {

Coordinator::Coordinator(CallbackRegistry& callbacks, WorkerPool& workers, TransportLifecycle& transport)
    : callbacks_(callbacks), workers_(workers), transport_(transport) {}

void Coordinator::Start() {
    callbacks_.RegisterAll();
    workers_.Start();
    transport_.Start();
}

void Coordinator::Shutdown() {
    {
        std::unique_lock<std::mutex> lock(mutex_);
        if (shutdownStarted_) {
            // A different caller already started the barrier: wait for it to
            // finish rather than returning early, so every caller's Shutdown()
            // only returns once the system is actually fully shut down.
            shutdownCompleteCv_.wait(lock, [this] { return shutdownComplete_; });
            return;
        }
        shutdownStarted_ = true;
    }

    // 1. Mark stopping so a new CallbackGuard returns without touching state.
    stopping_.store(true, std::memory_order_release);

    // 2. Unregister callbacks.
    callbacks_.UnregisterAll();

    // 3. Wait for callbacks already in flight to leave.
    {
        std::unique_lock<std::mutex> lock(mutex_);
        inFlightCv_.wait(lock, [this] { return inFlightCallbacks_ == 0; });
    }

    // 4. Stop and join workers. Draining/cancelling queued work is the
    //    worker pool implementation's responsibility (see WorkerPool::Stop).
    workers_.Stop();
    workers_.Join();

    // 5. Cancel transport completions.
    transport_.CancelCompletions();

    // 6. Invalidate the transport lifetime token: an in-flight completion
    //    that captured it now sees IsValid() == false.
    transportToken_->Invalidate();

    // 7. Close transport resources.
    transport_.Close();

    {
        std::lock_guard<std::mutex> lock(mutex_);
        shutdownComplete_ = true;
    }
    shutdownCompleteCv_.notify_all();
}

bool Coordinator::IsStopping() const {
    return stopping_.load(std::memory_order_acquire);
}

std::shared_ptr<LifetimeToken> Coordinator::TransportLifetimeTokenHandle() const {
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

bool Coordinator::RunContained(const std::function<void()>& work) {
    try {
        work();
        return true;
    } catch (...) {
        RegisterFailure();
        return false;
    }
}

Coordinator::CallbackGuard::CallbackGuard(Coordinator& coordinator) : coordinator_(coordinator) {
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

bool Coordinator::CallbackGuard::ShouldProceed() const {
    return proceed_;
}

}  // namespace dovahlink::application
