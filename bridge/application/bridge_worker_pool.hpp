#pragma once

#include "application/connection_session.hpp"
#include "application/coordinator.hpp"
#include "application/session.hpp"
#include "application/subscription_handler.hpp"
#include "security/throttle.hpp"
#include "security/token_store.hpp"
#include "transport/connection_slot.hpp"
#include "transport/listener.hpp"

#include <atomic>
#include <thread>

namespace dovahlink::application {

// The real WorkerPool: one accept-loop thread per loopback listener (IPv4,
// IPv6), each running RunConnectionSession inline, synchronously, for
// whichever connection it accepts and claims the one-connection slot for
// (transport::ConnectionSlot already enforces "one connected client" --
// ai/context/protocol/security.md -- so at most one of the two threads is
// ever actually running a session at a time; the other sits blocked in
// accept() until its own next connection, immediately closing anything it
// accepts while the slot is held). Running the session inline on the
// accept-loop thread, rather than handing it to a further worker thread,
// avoids unbounded thread creation for what is, by design, never more than
// one live connection.
//
// Start()/Stop()/Join() satisfy the ordering Coordinator::Shutdown()
// requires: Stop() closes both listeners' acceptors itself (not just sets
// a flag) so any thread blocked in a pending accept() unblocks immediately,
// before Join() is called -- Coordinator::Shutdown() calls Stop() then
// Join() before it ever calls TransportLifecycle::Close(), so waiting for
// that later close would deadlock.
class BridgeWorkerPool : public WorkerPool {
public:
    BridgeWorkerPool(transport::LoopbackListener& listenerV4, transport::LoopbackListener& listenerV6,
                      transport::ConnectionSlot& slot, security::TokenStore& tokenStore,
                      security::FailedTokenThrottle& tokenThrottle, SessionManager& sessionManager,
                      const CharacterStateProvider& stateProvider);

    // Guarantees Stop()+Join() happen even if a caller never does (e.g. an
    // exception unwinding past an explicit shutdown call): a joinable
    // std::thread destroyed without being joined calls std::terminate(),
    // which would otherwise crash the whole process on any such path, not
    // just leak. Safe to call after an explicit Stop()+Join() already ran
    // (Stop() closing an already-closed acceptor is a no-op; Join() checks
    // joinable() itself).
    ~BridgeWorkerPool() override;

    BridgeWorkerPool(const BridgeWorkerPool&) = delete;
    BridgeWorkerPool& operator=(const BridgeWorkerPool&) = delete;

    void Start() override;
    void Stop() override;
    void Join() override;

private:
    void AcceptLoop(transport::LoopbackListener& listener);

    transport::LoopbackListener& listenerV4_;
    transport::LoopbackListener& listenerV6_;
    transport::ConnectionSlot& slot_;
    security::TokenStore& tokenStore_;
    security::FailedTokenThrottle& tokenThrottle_;
    SessionManager& sessionManager_;
    const CharacterStateProvider& stateProvider_;

    std::atomic<bool> stopping_{false};
    std::atomic<ConnectionId> nextConnectionId_{1};
    std::thread threadV4_;
    std::thread threadV6_;
};

}  // namespace dovahlink::application
