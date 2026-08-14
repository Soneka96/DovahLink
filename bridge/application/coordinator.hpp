#pragma once

#include "application/lifetime_token.hpp"

#include <atomic>
#include <condition_variable>
#include <functional>
#include <memory>
#include <mutex>

namespace dovahlink::application {

/// Owns one potentially move-only operation submitted at a runtime boundary.
using ContainedWork = std::move_only_function<void()>;

/// Executes one synchronous runtime-boundary operation without allowing an
/// exception to escape.
/// @return `true` when the operation was admitted and completed successfully.
using ContainedWorkRunner = std::function<bool(ContainedWork)>;

/// Registers and unregisters runtime callbacks owned by the coordinator.
class CallbackRegistry {
public:
    /// Releases the interface without performing work.
    virtual ~CallbackRegistry() = default;

    /// Registers all callbacks required by the bridge.
    /// @param callbackRunner Guarded containment boundary retained by callbacks.
    virtual void RegisterAll(ContainedWorkRunner callbackRunner) = 0;

    /// Unregisters all callbacks before owned state is destroyed.
    virtual void UnregisterAll() = 0;
};

/// Controls worker startup and shutdown.
class WorkerPool {
public:
    /// Releases the interface without performing work.
    virtual ~WorkerPool() = default;

    /// Starts worker processing.
    /// @param workerRunner Containment boundary retained by worker threads.
    virtual void Start(ContainedWorkRunner workerRunner) = 0;

    /// Stops workers and drains or cancels queued work without joining them.
    virtual void Stop() = 0;

    /// Waits until every worker has exited.
    virtual void Join() = 0;
};

/// Controls transport startup, completion cancellation, and closure.
class TransportLifecycle {
public:
    /// Releases the interface without performing work.
    virtual ~TransportLifecycle() = default;

    /// Starts transport handling.
    virtual void Start() = 0;

    /// Cancels transport completions that can still run.
    virtual void CancelCompletions() = 0;

    /// Closes transport resources.
    virtual void Close() = 0;
};

/// Owns callback, worker, and transport lifecycle and provides idempotent shutdown.
class Coordinator {
public:
    /// Creates a coordinator from its lifecycle components.
    /// @param callbacks Callback registration boundary.
    /// @param workers Worker lifecycle boundary.
    /// @param transport Transport lifecycle boundary.
    Coordinator(CallbackRegistry& callbacks, WorkerPool& workers, TransportLifecycle& transport);

    /// Shuts down every lifecycle dependency before the coordinator is destroyed.
    ~Coordinator() noexcept;

    /// Registers callbacks and starts workers and transport in order.
    void Start();

    /// Completes every shutdown stage on a best-effort basis; repeated calls wait for completion.
    /// Lifecycle implementations must not call this method re-entrantly from a shutdown stage.
    void Shutdown() noexcept;

    /// Reports whether shutdown has started.
    [[nodiscard]] bool IsStopping() const;

    /// Tracks one callback admitted before shutdown.
    class CallbackGuard {
    public:
        /// Attempts to admit a callback to the coordinator.
        /// @param coordinator Coordinator whose shutdown state is tracked.
        explicit CallbackGuard(Coordinator& coordinator);

        /// Releases the callback admission, if one was granted.
        ~CallbackGuard();

        /// Copying an admission guard is not supported.
        CallbackGuard(const CallbackGuard&) = delete;

        /// Copy assignment is not supported.
        CallbackGuard& operator=(const CallbackGuard&) = delete;

        /// Reports whether the callback may proceed.
        [[nodiscard]] bool ShouldProceed() const;

    private:
        /// Coordinator whose in-flight count is tracked.
        Coordinator& coordinator_;

        /// Whether this guard incremented the in-flight count.
        bool proceed_;
    };

    /// Returns the lifetime token used by transport completions.
    [[nodiscard]] std::shared_ptr<LifetimeToken> TransportLifetimeTokenHandle() const;

    /// Reports whether the coordinator is available for operation.
    [[nodiscard]] bool IsAvailable() const;

    /// Marks the coordinator unavailable after a contained failure.
    void RegisterFailure();

    /// Restores availability after a fresh session snapshot.
    void ResetAvailability();

    /// Runs work and converts an exception into an unavailable state.
    /// @param work Operation to execute.
    /// @return `true` when work completes without throwing.
    bool RunContained(ContainedWork work) noexcept;

    /// Admits one runtime callback and contains every exception it throws.
    /// @param work Callback operation to execute.
    /// @return `true` when the callback was admitted and completed successfully.
    bool RunCallbackContained(ContainedWork work) noexcept;

private:
    /// Publishes barrier completion when the owning shutdown call exits.
    class ShutdownCompletionGuard {
    public:
        /// Binds completion publication to one coordinator.
        /// @param coordinator Coordinator whose shutdown barrier is owned.
        explicit ShutdownCompletionGuard(Coordinator& coordinator) noexcept;

        /// Publishes completion and releases every waiting shutdown caller.
        ~ShutdownCompletionGuard() noexcept;

        /// Copying the unique completion publisher is not supported.
        ShutdownCompletionGuard(const ShutdownCompletionGuard&) = delete;

        /// Copy assignment is not supported.
        ShutdownCompletionGuard& operator=(const ShutdownCompletionGuard&) = delete;

    private:
        /// Coordinator whose completion state is published.
        Coordinator& coordinator_;
    };

    /// Callback registration boundary.
    CallbackRegistry& callbacks_;

    /// Worker lifecycle boundary.
    WorkerPool& workers_;

    /// Transport lifecycle boundary.
    TransportLifecycle& transport_;

    /// Lifetime token shared with transport completions.
    std::shared_ptr<LifetimeToken> transportToken_ = std::make_shared<LifetimeToken>();

    /// Synchronizes shutdown state and callback admission.
    mutable std::mutex mutex_;

    /// Notifies waiters when in-flight callbacks leave.
    std::condition_variable inFlightCv_;

    /// Prevents new callback admission after shutdown begins.
    std::atomic<bool> stopping_{false};

    /// Whether the coordinator can publish state.
    std::atomic<bool> available_{true};

    /// Whether one caller has begun the shutdown barrier.
    bool shutdownStarted_ = false;

    /// Publishes shutdown completion to callers that did not own the barrier.
    std::atomic<bool> shutdownComplete_{false};

    /// Number of callbacks currently admitted.
    int inFlightCallbacks_ = 0;
};

}  // namespace dovahlink::application
