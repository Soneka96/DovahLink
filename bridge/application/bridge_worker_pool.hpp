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
#include <memory>
#include <mutex>
#include <thread>

namespace dovahlink::application {

/// Owns one accept worker per loopback listener and enforces one active client.
/// `Stop()` closes listeners and shuts down the active session socket before
/// `Join()` so blocked accepts, handshakes, and reads can finish.
class BridgeWorkerPool : public WorkerPool {
public:
    /// Creates workers for the two loopback listeners.
    /// @param listenerV4 IPv4 loopback listener owned by the caller.
    /// @param listenerV6 IPv6 loopback listener owned by the caller.
    /// @param slot Admission gate enforcing the connected-client limit.
    /// @param tokenStore One-time token store shared by connections.
    /// @param tokenThrottle Failed-token throttle shared by connections.
    /// @param sessionManager Session ownership manager.
    /// @param stateProvider Provider of current character state on the v1 (session-scoped) path.
    /// @param activePlayContext Source of the acquired play context on the v2 (play-context-scoped) path.
    /// @param bridgeInstanceId This bridge process's identity, stamped onto every v2 response
    ///     envelope; no value if generation failed at startup.
    BridgeWorkerPool(transport::LoopbackListener& listenerV4, transport::LoopbackListener& listenerV6,
                     transport::ConnectionSlot& slot, security::TokenStore& tokenStore,
                     security::FailedTokenThrottle& tokenThrottle, SessionManager& sessionManager,
                     const CharacterStateProvider& stateProvider, const ActivePlayContext& activePlayContext,
                     std::optional<std::string> bridgeInstanceId);

    /// Stops and joins any workers that remain active.
    ~BridgeWorkerPool() override;

    /// Copying a worker pool is not supported.
    BridgeWorkerPool(const BridgeWorkerPool&) = delete;

    /// Copy assignment is not supported.
    BridgeWorkerPool& operator=(const BridgeWorkerPool&) = delete;

    /// @copydoc WorkerPool::Start
    void Start(ContainedWorkRunner workerRunner) override;

    /// @copydoc WorkerPool::Stop
    void Stop() override;

    /// @copydoc WorkerPool::Join
    void Join() override;

private:
    /// Accepts connections from one loopback listener until stopping.
    /// @param listener Listener whose accept loop is executed.
    /// @param workerRunner Per-connection exception containment boundary.
    void AcceptLoop(transport::LoopbackListener& listener, const ContainedWorkRunner& workerRunner);

    /// IPv4 listener used by one accept worker.
    transport::LoopbackListener& listenerV4_;

    /// IPv6 listener used by one accept worker.
    transport::LoopbackListener& listenerV6_;

    /// Admission gate for the active connection.
    transport::ConnectionSlot& slot_;

    /// Shared one-time authentication token store.
    security::TokenStore& tokenStore_;

    /// Shared failed-token throttle.
    security::FailedTokenThrottle& tokenThrottle_;

    /// Session manager shared by accepted connections.
    SessionManager& sessionManager_;

    /// Source of captured character state.
    const CharacterStateProvider& stateProvider_;

    /// Source of the acquired play context on the v2 (play-context-scoped) path.
    const ActivePlayContext& activePlayContext_;

    /// This bridge process's identity, stamped onto every v2 response envelope.
    std::optional<std::string> bridgeInstanceId_;

    /// Signals both accept workers to stop.
    std::atomic<bool> stopping_{false};

    /// Next transport connection identifier.
    std::atomic<ConnectionId> nextConnectionId_{1};

    /// Serializes active-socket publication with shutdown lookup.
    std::mutex activeSocketMutex_;

    /// Non-owning handle to the active session socket, when one exists.
    std::weak_ptr<transport::WebSocketSession::Socket> activeSocket_;

    /// IPv4 accept worker.
    std::thread threadV4_;

    /// IPv6 accept worker.
    std::thread threadV6_;
};

}  // namespace dovahlink::application
