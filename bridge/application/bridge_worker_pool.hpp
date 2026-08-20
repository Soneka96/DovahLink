#pragma once

#include "application/active_session_disconnector.hpp"
#include "application/connection_session.hpp"
#include "application/coordinator.hpp"
#include "application/pairing_notification_sink.hpp"
#include "application/session.hpp"
#include "application/subscription_handler.hpp"
#include "security/pairing_session.hpp"
#include "security/throttle.hpp"
#include "security/token_store.hpp"
#include "security/trust_store.hpp"
#include "transport/connection_slot.hpp"
#include "transport/listener.hpp"

#include <boost/asio/ip/tcp.hpp>

#include <atomic>
#include <memory>
#include <mutex>
#include <string_view>
#include <thread>

namespace dovahlink::application {

/// Owns one accept worker per loopback listener and enforces one active client.
/// `Stop()` closes listeners and shuts down the active session socket before
/// `Join()` so blocked accepts, handshakes, and reads can finish. Also the reusable
/// `ActiveSessionDisconnector` implementation for trust administration, since it is the only
/// component that ever holds a live session's socket handle.
class BridgeWorkerPool : public WorkerPool, public ActiveSessionDisconnector {
public:
    /// Creates workers for the two loopback listeners.
    /// @param listenerV4 IPv4 loopback listener owned by the caller.
    /// @param listenerV6 IPv6 loopback listener owned by the caller.
    /// @param slot Admission gate enforcing the connected-client limit.
    /// @param tokenStore One-time token store shared by connections.
    /// @param tokenThrottle Failed-token throttle shared by connections.
    /// @param trustStore Persistent trust store shared by connections.
    /// @param credentialThrottle Failed device-credential attempt throttle shared by connections.
    /// @param sessionManager Session ownership manager.
    /// @param activePlayContext Source of the acquired play context each connection's state and
    ///     revisions belong to.
    /// @param pairingSession Bridge-lifetime pairing challenge/pending-credential state machine
    ///     shared by connections.
    /// @param pairingNotificationSink Displays a freshly generated pairing code to the user.
    /// @param bridgeInstanceId This bridge process's identity, stamped onto every response
    ///     envelope; no value if generation failed at startup.
    /// @param bridgeVersion The DovahLink Bridge/mod release version exposed to clients in
    ///     `hello_ack.bridgeVersion` (`ai/context/protocol/compatibility.md`).
    BridgeWorkerPool(transport::LoopbackListener& listenerV4, transport::LoopbackListener& listenerV6,
                     transport::ConnectionSlot& slot, security::TokenStore& tokenStore,
                     security::FailedTokenThrottle& tokenThrottle, security::TrustStore& trustStore,
                     security::FailedTokenThrottle& credentialThrottle, SessionManager& sessionManager,
                     const ActivePlayContext& activePlayContext, security::PairingSession& pairingSession,
                     PairingNotificationSink& pairingNotificationSink, std::optional<std::string> bridgeInstanceId,
                     std::string bridgeVersion);

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

    /// @copydoc ActiveSessionDisconnector::DisconnectIfClientActive
    void DisconnectIfClientActive(std::string_view clientId) override;

    /// @copydoc ActiveSessionDisconnector::DisconnectActive
    void DisconnectActive() override;

private:
    /// Accepts connections from one loopback listener until stopping.
    /// @param listener Listener whose accept loop is executed.
    /// @param workerRunner Per-connection exception containment boundary.
    void AcceptLoop(transport::LoopbackListener& listener, const ContainedWorkRunner& workerRunner);

    /// Joins the previous connection's session thread, if any is still joinable. `ConnectionSlot`
    /// admits at most one session at a time, so by the time a *new* connection successfully
    /// acquires the slot the previous session thread has already released it (finished or is
    /// finishing) -- this join is therefore expected to return immediately, not to reintroduce the
    /// accept-loop stall `RunSessionOnOwnThread` exists to avoid. Call only while holding
    /// `connectionThreadMutex_`.
    void JoinConnectionThreadLocked();

    /// Runs one accepted connection's full session (`RunConnectionSession`, wrapped in
    /// `workerRunner`'s exception containment) on its own thread, moving `slotLease` into that
    /// thread so the slot is held for the session's real lifetime. Letting `AcceptLoop` return to
    /// `AcceptLoopbackOnly()` immediately after spawning this -- rather than blocking on the
    /// session itself -- is what lets a second connection attempt arriving while this one is still
    /// live actually reach `ConnectionSlot::TryAcquire()` and be rejected promptly, matching
    /// `ai/context/protocol/security.md`'s "reject without a WebSocket handshake" contract instead
    /// of sitting unrejected in the OS accept backlog until this session's own idle timeout frees
    /// the accept thread.
    /// @param workerRunner Per-connection exception containment boundary.
    /// @param connection Identifier assigned to the accepted connection.
    /// @param socket The accepted, not-yet-upgraded TCP socket.
    /// @param slotLease The connection slot lease admitting this connection; released when the
    ///     session ends.
    void RunSessionOnOwnThread(const ContainedWorkRunner& workerRunner, ConnectionId connection,
                               boost::asio::ip::tcp::socket socket, transport::ConnectionSlot::Lease slotLease);

    /// Shuts down the active session socket, if one is currently published, independent of the
    /// caller's thread -- the same cross-thread-safe path `Socket::Shutdown()` itself provides.
    /// Shared by `Stop()`, `DisconnectIfClientActive()`, and `DisconnectActive()` so all three tear
    /// down the active socket exactly the same way.
    void ShutdownActiveSocket();

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

    /// Shared persistent trust store.
    security::TrustStore& trustStore_;

    /// Shared failed device-credential attempt throttle.
    security::FailedTokenThrottle& credentialThrottle_;

    /// Session manager shared by accepted connections.
    SessionManager& sessionManager_;

    /// Source of the acquired play context each connection's state and revisions belong to.
    const ActivePlayContext& activePlayContext_;

    /// Shared pairing challenge/pending-credential state machine.
    security::PairingSession& pairingSession_;

    /// Displays a freshly generated pairing code to the user.
    PairingNotificationSink& pairingNotificationSink_;

    /// This bridge process's identity, stamped onto every response envelope.
    std::optional<std::string> bridgeInstanceId_;

    /// The DovahLink Bridge/mod release version exposed to clients in `hello_ack.bridgeVersion`.
    std::string bridgeVersion_;

    /// Signals both accept workers to stop.
    std::atomic<bool> stopping_{false};

    /// Next transport connection identifier.
    std::atomic<ConnectionId> nextConnectionId_{1};

    /// Serializes active-socket publication with shutdown lookup.
    std::mutex activeSocketMutex_;

    /// Non-owning handle to the active session socket, when one exists.
    std::weak_ptr<transport::WebSocketSession::Socket> activeSocket_;

    /// Connection identifier `activeSocket_` was published for, read together with it under
    /// `activeSocketMutex_`. `DisconnectIfClientActive` asks `sessionManager_` "who owns *this*
    /// connection" instead of "who is active right now" (`SessionManager::ActiveClientId()`,
    /// unscoped): the latter can change between an unsynchronized read and the shutdown call,
    /// letting a stale revoke for a just-ended session hit a new connection that raced into its
    /// place. Meaningless while `activeSocket_.lock()` is null; the single-connected-client limit
    /// means this can stay one field instead of a registry -- see roadmap/09-multi-client-runtime-foundation.md Phase 9 for
    /// generalizing it once that limit is lifted.
    ConnectionId activeConnectionId_{};

    /// IPv4 accept worker.
    std::thread threadV4_;

    /// IPv6 accept worker.
    std::thread threadV6_;

    /// Serializes replacing `connectionThread_` (both accept-loop threads may spawn one).
    std::mutex connectionThreadMutex_;

    /// Runs the most recently accepted connection's full session. `ConnectionSlot`'s exclusivity
    /// means at most one of these is ever doing meaningful work at a time -- see
    /// `JoinConnectionThreadLocked`'s own doc comment for why a single field suffices here, the
    /// same reasoning `activeConnectionId_` above already documents for the single-connected-client
    /// model.
    std::thread connectionThread_;
};

}  // namespace dovahlink::application
