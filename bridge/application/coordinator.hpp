#pragma once

#include "application/lifetime_token.hpp"

#include <atomic>
#include <condition_variable>
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

private:
    CallbackRegistry& callbacks_;
    WorkerPool& workers_;
    TransportLifecycle& transport_;

    std::shared_ptr<LifetimeToken> transportToken_ = std::make_shared<LifetimeToken>();

    mutable std::mutex mutex_;
    std::condition_variable inFlightCv_;
    std::condition_variable shutdownCompleteCv_;
    std::atomic<bool> stopping_{false};
    bool shutdownStarted_ = false;
    bool shutdownComplete_ = false;
    int inFlightCallbacks_ = 0;
};

}  // namespace dovahlink::application
