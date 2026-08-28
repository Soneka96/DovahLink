#pragma once

#include "application/contained_work.hpp"
#include "application/i_bridge_callback_registry.hpp"
#include "application/lifetime_token.hpp"

#include <atomic>
#include <condition_variable>
#include <memory>
#include <mutex>

namespace dovahlink::application {

class IBridgeWorkerPool;
class IBridgeTransport;
class ICaptureDispatchWorker;
class ICadenceTickDriver;

///  Owns callback, capture, worker, and transport lifecycle and provides
///  idempotent shutdown.
class ICoordinator {
  public:
    ///  Releases the interface without performing work.
    virtual ~ICoordinator() = default;

    ///  Registers callbacks and starts workers, transport, and the optional
    ///  capture lifecycle in order.
    ///  Repeated calls and calls after shutdown has begun return without action.
    ///  Lifecycle dependencies must not call coordinator lifecycle methods
    ///  re-entrantly.
    virtual void Start() = 0;

    ///  Completes every shutdown stage on a best-effort basis; repeated calls wait
    ///  for completion. Lifecycle implementations must not call this method
    ///  re-entrantly from a shutdown stage.
    virtual void Shutdown() noexcept = 0;

    ///  Reports whether shutdown has started.
    [[nodiscard]] virtual bool IsStopping() const = 0;

    ///  Returns the lifetime token used by transport completions.
    [[nodiscard]] virtual std::shared_ptr<LifetimeToken>
    TransportLifetimeTokenHandle() const = 0;

    ///  Reports whether the coordinator is available for operation.
    [[nodiscard]] virtual bool IsAvailable() const = 0;

    ///  Marks the coordinator unavailable after a contained failure.
    virtual void RegisterFailure() = 0;

    ///  Restores availability after a fresh session snapshot.
    virtual void ResetAvailability() = 0;

    ///  Runs work and converts an exception into an unavailable state.
    ///  @param work Operation to execute.
    ///  @return `true` when work completes without throwing.
    virtual bool RunContained(ContainedWork work) noexcept = 0;

    ///  Admits one runtime callback and contains every exception it throws.
    ///  @param work Callback operation to execute.
    ///  @return `true` when the callback was admitted and completed successfully.
    virtual bool RunCallbackContained(ContainedWork work) noexcept = 0;
};

///  @copydoc ICoordinator
class Coordinator final : public ICoordinator {
  public:
    ///  Creates a coordinator from its lifecycle components.
    ///  @param callbacks Callback registration boundary.
    ///  @param workers Worker lifecycle boundary.
    ///  @param transport Transport lifecycle boundary.
    Coordinator(IBridgeCallbackRegistry& callbacks, IBridgeWorkerPool& workers,
                IBridgeTransport& transport);

    ///  Creates a coordinator with the production capture lifecycle
    ///  dependencies.
    ///  @param callbacks Callback registration boundary.
    ///  @param workers Worker lifecycle boundary.
    ///  @param transport Transport lifecycle boundary.
    ///  @param captureWorker Capture-to-publication worker lifecycle.
    ///  @param cadenceDriver Sampled-capture cadence lifecycle.
    Coordinator(IBridgeCallbackRegistry& callbacks, IBridgeWorkerPool& workers,
                IBridgeTransport& transport,
                ICaptureDispatchWorker& captureWorker,
                ICadenceTickDriver& cadenceDriver);

    ///  Shuts down every lifecycle dependency before the coordinator is destroyed.
    ~Coordinator() noexcept override;

    ///  @copydoc ICoordinator::Start
    void Start() override;

    ///  @copydoc ICoordinator::Shutdown
    void Shutdown() noexcept override;

    ///  @copydoc ICoordinator::IsStopping
    [[nodiscard]] bool IsStopping() const override;

    ///  Tracks one callback admitted before shutdown.
    class CallbackGuard {
      public:
        ///  Attempts to admit a callback to the coordinator.
        ///  @param coordinator Coordinator whose shutdown state is tracked.
        explicit CallbackGuard(Coordinator& coordinator);

        ///  Releases the callback admission, if one was granted.
        ~CallbackGuard();

        ///  Copying an admission guard is not supported.
        CallbackGuard(const CallbackGuard&) = delete;

        ///  Copy assignment is not supported.
        CallbackGuard& operator=(const CallbackGuard&) = delete;

        ///  Reports whether the callback may proceed.
        [[nodiscard]] bool ShouldProceed() const;

      private:
        ///  Coordinator whose in-flight count is tracked.
        Coordinator& coordinator_;

        ///  Whether this guard incremented the in-flight count.
        bool proceed_;
    };

    ///  @copydoc ICoordinator::TransportLifetimeTokenHandle
    [[nodiscard]] std::shared_ptr<LifetimeToken>
    TransportLifetimeTokenHandle() const override;

    ///  @copydoc ICoordinator::IsAvailable
    [[nodiscard]] bool IsAvailable() const override;

    ///  @copydoc ICoordinator::RegisterFailure
    void RegisterFailure() override;

    ///  @copydoc ICoordinator::ResetAvailability
    void ResetAvailability() override;

    ///  @copydoc ICoordinator::RunContained
    bool RunContained(ContainedWork work) noexcept override;

    ///  @copydoc ICoordinator::RunCallbackContained
    bool RunCallbackContained(ContainedWork work) noexcept override;

  private:
    ///  Publishes barrier completion when the owning shutdown call exits.
    class ShutdownCompletionGuard {
      public:
        ///  Binds completion publication to one coordinator.
        ///  @param coordinator Coordinator whose shutdown barrier is owned.
        explicit ShutdownCompletionGuard(Coordinator& coordinator) noexcept;

        ///  Publishes completion and releases every waiting shutdown caller.
        ~ShutdownCompletionGuard() noexcept;

        ///  Copying the unique completion publisher is not supported.
        ShutdownCompletionGuard(const ShutdownCompletionGuard&) = delete;

        ///  Copy assignment is not supported.
        ShutdownCompletionGuard&
        operator=(const ShutdownCompletionGuard&) = delete;

      private:
        ///  Coordinator whose completion state is published.
        Coordinator& coordinator_;
    };

    ///  Callback registration boundary.
    IBridgeCallbackRegistry& callbacks_;

    ///  Worker lifecycle boundary.
    IBridgeWorkerPool& workers_;

    ///  Transport lifecycle boundary.
    IBridgeTransport& transport_;

    ///  Capture worker lifecycle boundary, present in the production
    ///  composition and absent from the lightweight harness constructor.
    ICaptureDispatchWorker* captureWorker_ = nullptr;

    ///  Cadence driver lifecycle boundary, present in the production
    ///  composition and absent from the lightweight harness constructor.
    ICadenceTickDriver* cadenceDriver_ = nullptr;

    ///  Lifetime token shared with transport completions.
    std::shared_ptr<LifetimeToken> transportToken_ =
        std::make_shared<LifetimeToken>();

    ///  Synchronizes shutdown state and callback admission.
    mutable std::mutex mutex_;

    ///  Serializes lifecycle startup and shutdown transitions.
    mutable std::mutex lifecycleMutex_;

    ///  Notifies waiters when in-flight callbacks leave.
    std::condition_variable inFlightCv_;

    ///  Prevents new callback admission after shutdown begins.
    std::atomic<bool> stopping_{false};

    ///  Whether the coordinator can publish state.
    std::atomic<bool> available_{true};

    ///  Whether startup has been claimed, including a startup that failed partway
    ///  through.
    bool startClaimed_ = false;

    ///  Whether one caller has begun the shutdown barrier.
    bool shutdownStarted_ = false;

    ///  Publishes shutdown completion to callers that did not own the barrier.
    std::atomic<bool> shutdownComplete_{false};

    ///  Number of callbacks currently admitted.
    int inFlightCallbacks_ = 0;
};

} //  namespace dovahlink::application
