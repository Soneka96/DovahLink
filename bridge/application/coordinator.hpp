#pragma once

#include "application/lifetime_token.hpp"

#include <atomic>
#include <condition_variable>
#include <functional>
#include <memory>
#include <mutex>

namespace dovahlink::application {

// Seams the coordinator depends on. Production implementations are added in
// later steps: callback registration wires to the SKSE plugin boundary,
// workers to the queue/worker infrastructure, transport to Boost.Asio/Beast.
// Tests use fakes that record call order (see coordinator_test.cpp).
//
// "Drain or cancel queued work" (the shutdown barrier step between
// unregistering callbacks and stopping workers) is this coordinator's fourth
// barrier step conceptually, but it is not a separate seam here: it belongs
// to whatever owns the actual queue, which is the worker pool implementation
// -- WorkerPool::Stop() is documented as responsible for it.
class CallbackRegistry {
public:
    virtual ~CallbackRegistry() = default;
    virtual void RegisterAll() = 0;
    virtual void UnregisterAll() = 0;
};

class WorkerPool {
public:
    virtual ~WorkerPool() = default;
    virtual void Start() = 0;
    // Signals workers to stop and drains or cancels their queued work; does
    // not block for workers to actually exit (see Join).
    virtual void Stop() = 0;
    // Blocks until every worker has exited.
    virtual void Join() = 0;
};

class TransportLifecycle {
public:
    virtual ~TransportLifecycle() = default;
    virtual void Start() = 0;
    virtual void CancelCompletions() = 0;
    virtual void Close() = 0;
};

// Owns callback registration, worker lifecycle, and transport lifecycle for
// the bridge, implementing the exact shutdown barrier documented in
// ai/context/skse/architecture.md:
//
// 1. mark stopping, so a new CallbackGuard returns ShouldProceed() == false
//    without touching coordinator state
// 2. unregister callbacks
// 3. wait for callbacks already in flight to leave (see CallbackGuard)
// 4. stop and join workers (draining/cancelling their queued work is the
//    worker pool implementation's responsibility, see WorkerPool::Stop)
// 5. cancel transport completions
// 6. invalidate the transport lifetime token, so an in-flight completion
//    that captured it stops touching coordinator/transport state instead of
//    the coordinator blocking to wait for it
// 7. close transport resources
//
// Shutdown is idempotent: calling it more than once, from any thread, is
// safe, only the first call performs the sequence, and every caller's
// Shutdown() -- whether it performed the sequence or arrived after another
// caller already started it -- only returns once that sequence has fully
// completed. A caller can rely on the system being shut down when its own
// call to Shutdown() returns, regardless of which caller "won".
class Coordinator {
public:
    Coordinator(CallbackRegistry& callbacks, WorkerPool& workers, TransportLifecycle& transport);

    // Registers callbacks, starts workers, starts transport.
    void Start();

    // The full shutdown barrier described above. Idempotent.
    void Shutdown();

    [[nodiscard]] bool IsStopping() const;

    // RAII in-flight guard a game-thread callback constructs before doing
    // any coordinator-owned work. See
    // ai/context/skse/architecture.md's "callback registration and
    // in-flight tracking must remain alive until the unregister-and-wait
    // barrier completes."
    class CallbackGuard {
    public:
        explicit CallbackGuard(Coordinator& coordinator);
        ~CallbackGuard();

        CallbackGuard(const CallbackGuard&) = delete;
        CallbackGuard& operator=(const CallbackGuard&) = delete;

        // False if the coordinator was already stopping when this guard was
        // constructed: the caller must return immediately without touching
        // coordinator state, and this guard did not join the in-flight count.
        [[nodiscard]] bool ShouldProceed() const;

    private:
        Coordinator& coordinator_;
        bool proceed_;
    };

    // The independently-owned lifetime token for transport completions (see
    // LifetimeToken's own docs). A transport completion handler captures a
    // copy of this shared_ptr when it is scheduled and checks IsValid()
    // before touching coordinator- or transport-owned state.
    [[nodiscard]] std::shared_ptr<LifetimeToken> TransportLifetimeTokenHandle() const;

    // Failure containment (ai/context/skse/cpp-style.md: "Catch all
    // exceptions at callback, worker-thread, and transport-completion
    // boundaries... never allow an exception to escape a callback or thread
    // entry point"; ai/context/skse/architecture.md: "If a worker exits
    // unexpectedly, the coordinator enters unavailable, stops publishing
    // state as current, reports a controlled internal_error, and either
    // restarts the worker through an approved policy or requires a clean
    // reconnect.")
    //
    // Phase 1's approved policy is "requires a clean reconnect": no
    // automatic worker restart is implemented, since there is no worker
    // infrastructure yet for a restart to act on. A failure marks the
    // coordinator unavailable; the caller (the transport/session layer, in a
    // later step) is expected to close the connection and let a fresh
    // reconnect establish a new session. ResetAvailability is called once
    // that new session has published a fresh snapshot, matching "a
    // restarted worker must receive a fresh snapshot before publication
    // resumes."
    //
    // Known limitation: availability is coordinator-scoped, spanning every
    // session over the plugin's lifetime, not session-scoped. A failure
    // reported late by a straggling worker/callback from a session that has
    // already been abandoned for a reconnect can flip a freshly
    // ResetAvailability()'d coordinator back to unavailable, with no way
    // from here to tell that the failure belonged to the old session.
    // Resolving this needs the future integration that wires SessionManager
    // (bridge/application/session.hpp) together with this coordinator,
    // which can attach a session identity to the failure; this generic
    // coordinator does not know about sessions and should not guess at that
    // wiring prematurely.

    // True until a failure has been registered and no session has
    // re-established availability since.
    [[nodiscard]] bool IsAvailable() const;

    // Records that a worker, callback, or transport-completion boundary
    // caught an exception, marking the coordinator unavailable. Safe to call
    // more than once; only the first call after becoming available again has
    // an observable effect.
    void RegisterFailure();

    // Called once a fresh snapshot has been published on a newly
    // reconnected session, re-establishing availability.
    void ResetAvailability();

    // Runs `work` and contains any exception it throws by calling
    // RegisterFailure instead of letting it escape, per the "never allow an
    // exception to escape" rule above. Wrap the body of a worker loop, a
    // game-thread callback, or a transport completion handler with this.
    // Returns true if `work` completed without throwing.
    bool RunContained(const std::function<void()>& work);

private:
    CallbackRegistry& callbacks_;
    WorkerPool& workers_;
    TransportLifecycle& transport_;

    std::shared_ptr<LifetimeToken> transportToken_ = std::make_shared<LifetimeToken>();

    mutable std::mutex mutex_;
    std::condition_variable inFlightCv_;
    std::condition_variable shutdownCompleteCv_;
    std::atomic<bool> stopping_{false};
    std::atomic<bool> available_{true};
    bool shutdownStarted_ = false;
    bool shutdownComplete_ = false;
    int inFlightCallbacks_ = 0;
};

}  // namespace dovahlink::application
