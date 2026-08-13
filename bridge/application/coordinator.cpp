#include "application/coordinator.hpp"

namespace dovahlink::application {

/**
     * @brief Initializes a coordinator with its callback, worker, and transport lifecycle components.
     *
     * @param callbacks Callback registry used to manage callback registration.
     * @param workers Worker pool used to manage worker execution.
     * @param transport Transport lifecycle manager used to control transport resources.
     */
    Coordinator::Coordinator(CallbackRegistry& callbacks, WorkerPool& workers, TransportLifecycle& transport)
    : callbacks_(callbacks), workers_(workers), transport_(transport) {}

/**
 * @brief Starts callback handling, worker processing, and transport handling.
 */
void Coordinator::Start() {
    callbacks_.RegisterAll();
    workers_.Start();
    transport_.Start();
}

/**
 * @brief Fully shuts down the coordinator and its associated resources.
 *
 * Concurrent callers wait until the shutdown is complete before returning.
 */
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

/**
 * @brief Determines whether the coordinator is stopping.
 *
 * @return `true` if shutdown has started, `false` otherwise.
 */
bool Coordinator::IsStopping() const {
    return stopping_.load(std::memory_order_acquire);
}

/**
 * @brief Gets the shared lifetime token for transport operations.
 *
 * @return Shared transport lifetime token.
 */
std::shared_ptr<LifetimeToken> Coordinator::TransportLifetimeTokenHandle() const {
    return transportToken_;
}

/**
 * @brief Determines whether the coordinator is available for operation.
 *
 * @return `true` if the coordinator is available, `false` otherwise.
 */
bool Coordinator::IsAvailable() const {
    return available_.load(std::memory_order_acquire);
}

/**
 * @brief Marks the coordinator as unavailable.
 */
void Coordinator::RegisterFailure() {
    available_.store(false, std::memory_order_release);
}

/**
 * @brief Marks the coordinator as available.
 */
void Coordinator::ResetAvailability() {
    available_.store(true, std::memory_order_release);
}

/**
 * @brief Executes work while containing exceptions and records failures.
 *
 * @param work Operation to execute.
 * @return `true` if the operation completes successfully, `false` if it throws.
 */
bool Coordinator::RunContained(const std::function<void()>& work) {
    try {
        work();
        return true;
    } catch (...) {
        RegisterFailure();
        return false;
    }
}

/**
 * @brief Admits a callback for execution unless coordinator shutdown is in progress.
 *
 * @param coordinator Coordinator whose callback lifecycle state is tracked.
 */
Coordinator::CallbackGuard::CallbackGuard(Coordinator& coordinator) : coordinator_(coordinator) {
    std::lock_guard<std::mutex> lock(coordinator_.mutex_);
    if (coordinator_.stopping_.load(std::memory_order_acquire)) {
        proceed_ = false;
        return;
    }
    ++coordinator_.inFlightCallbacks_;
    proceed_ = true;
}

/**
 * @brief Releases the callback admission held by this guard.
 */
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

/**
 * @brief Determines whether callback execution was admitted.
 *
 * @return `true` if callback execution may proceed, `false` otherwise.
 */
bool Coordinator::CallbackGuard::ShouldProceed() const {
    return proceed_;
}

}  // namespace dovahlink::application
