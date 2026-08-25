#include "application/bridge_worker_pool.hpp"

#include "protocol/envelope.hpp"
#include "protocol/session_invalidated_payload.hpp"
#include "transport/websocket_session.hpp"

#include <boost/asio/ip/tcp.hpp>
#include <boost/system/error_code.hpp>

#include <string>

namespace dovahlink::application {

BridgeWorkerPool::BridgeWorkerPool(
    transport::LoopbackListener& listenerV4,
    transport::LoopbackListener& listenerV6, transport::ConnectionSlot& slot,
    security::TokenStore& tokenStore,
    security::FailedTokenThrottle& tokenThrottle,
    security::TrustStore& trustStore,
    security::FailedTokenThrottle& credentialThrottle,
    SessionManager& sessionManager, const ActivePlayContext& activePlayContext,
    security::PairingSession& pairingSession,
    ITrustMutationCoordinator& mutationCoordinator,
    PairingNotificationSink& pairingNotificationSink,
    std::optional<std::string> bridgeInstanceId, std::string bridgeVersion)
    : listenerV4_(listenerV4), listenerV6_(listenerV6), slot_(slot),
      tokenStore_(tokenStore), tokenThrottle_(tokenThrottle),
      trustStore_(trustStore), credentialThrottle_(credentialThrottle),
      sessionManager_(sessionManager), activePlayContext_(activePlayContext),
      pairingSession_(pairingSession),
      mutationCoordinator_(mutationCoordinator),
      pairingNotificationSink_(pairingNotificationSink),
      bridgeInstanceId_(std::move(bridgeInstanceId)),
      bridgeVersion_(std::move(bridgeVersion)) {}

BridgeWorkerPool::~BridgeWorkerPool() {
    Stop();
    Join();
}

void BridgeWorkerPool::AcceptLoop(transport::LoopbackListener& listener,
                                  const ContainedWorkRunner& workerRunner) {
    while (!stopping_.load(std::memory_order_acquire)) {
        auto accepted = listener.AcceptLoopbackOnly();
        if (!accepted.has_value()) {
            if (accepted.error() ==
                transport::AcceptError::kNonLoopbackPeerRejected) {
                //  The acceptor itself is still fine; only this one peer was
                //  rejected. Try again.
                continue;
            }
            //  kAcceptFailed: either Stop() closed the acceptor (expected
            //  shutdown) or it failed for some other reason. Either way,
            //  this acceptor cannot be used again.
            return;
        }

        boost::asio::ip::tcp::socket socket = std::move(*accepted);
        auto slotLease = slot_.TryAcquire();
        if (!slotLease.has_value()) {
            //  Phase 1 allows exactly one connected client
            //  (ai/context/protocol/security.md); reject without a WebSocket
            //  handshake, matching ConnectionSlot's documented contract.
            boost::system::error_code ec;
            socket.close(ec);
            continue;
        }

        ConnectionId connection =
            nextConnectionId_.fetch_add(1, std::memory_order_relaxed);
        RunSessionOnOwnThread(workerRunner, connection, std::move(socket),
                              std::move(*slotLease));
    }
}

void BridgeWorkerPool::JoinConnectionThreadLocked() {
    if (connectionThread_.joinable()) {
        connectionThread_.join();
    }
}

void BridgeWorkerPool::RunSessionOnOwnThread(
    const ContainedWorkRunner& workerRunner, ConnectionId connection,
    boost::asio::ip::tcp::socket socket,
    transport::ConnectionSlot::Lease slotLease) {
    std::lock_guard<std::mutex> connectionThreadLock(connectionThreadMutex_);
    JoinConnectionThreadLocked();
    connectionThread_ =
        std::thread([this, workerRunner, connection, socket = std::move(socket),
                     slotLease = std::move(slotLease)]() mutable {
            //  slotLease's destruction (releasing ConnectionSlot) is what determines
            //  when the *next* connection attempt can be admitted -- moved in here
            //  so it spans this thread's whole session, not just the brief
            //  AcceptLoop iteration that spawned it.
            (void)workerRunner([this, connection,
                                socket = std::move(socket)]() mutable {
                auto socketHandle =
                    transport::WebSocketSession::CreateSocket(std::move(socket));
                {
                    std::lock_guard<std::mutex> lock(activeSocketMutex_);
                    activeSocket_ = socketHandle;
                    activeConnectionId_ = connection;
                }
                if (stopping_.load(std::memory_order_acquire)) {
                    socketHandle->Shutdown();
                    return;
                }

                transport::WebSocketSession session(std::move(socketHandle));
                RunConnectionSession(session, tokenStore_, tokenThrottle_,
                                     trustStore_, credentialThrottle_,
                                     sessionManager_, connection, activePlayContext_,
                                     pairingSession_, mutationCoordinator_,
                                     pairingNotificationSink_,
                                     bridgeInstanceId_, bridgeVersion_);
            });
        });
}

void BridgeWorkerPool::Start(ContainedWorkRunner workerRunner) {
    threadV4_ = std::thread([this, workerRunner] {
        (void)workerRunner(
            [this, workerRunner] { AcceptLoop(listenerV4_, workerRunner); });
    });
    threadV6_ = std::thread([this, workerRunner] {
        (void)workerRunner(
            [this, workerRunner] { AcceptLoop(listenerV6_, workerRunner); });
    });
}

void BridgeWorkerPool::Stop() {
    stopping_.store(true, std::memory_order_release);
    boost::system::error_code ec;
    listenerV4_.Acceptor().close(ec);
    listenerV6_.Acceptor().close(ec);
    ShutdownActiveSocket();
}

void BridgeWorkerPool::DisconnectIfClientActive(std::string_view clientId,
                                                std::string_view reason) {
    transport::WebSocketSession::SocketHandle activeSocket;
    std::optional<std::string> activeSessionId;
    {
        //  Holding activeSocketMutex_ across the socket read and the session
        //  snapshot makes both atomic: no new connection can publish itself as
        //  activeSocket_/activeConnectionId_ while this decides whether the
        //  *current* one belongs to clientId. SessionForConnection itself resolves
        //  clientId, authMethod, and sessionId together under SessionManager's own
        //  single lock, so there is no second, separately-locked read that could
        //  observe the session changing between them.
        std::lock_guard<std::mutex> lock(activeSocketMutex_);
        auto socket = activeSocket_.lock();
        if (!socket) {
            return;
        }
        auto session = sessionManager_.SessionForConnection(activeConnectionId_);
        if (!session.has_value() || session->clientId != clientId) {
            return;
        }
        //  A developer-token (one_time_local_token) session is never treated as a
        //  Known Device (ai/context/protocol/security.md's "Developer
        //  authentication"), so it stays connected even when its self-declared
        //  clientId happens to match the device this call targets. DisconnectActive
        //  (Factory Reset's unconditional path) intentionally has no equivalent
        //  check: Factory Reset invalidates every session, developer-token sessions
        //  included.
        if (session->authMethod == SessionAuthMethod::kDeveloperToken) {
            return;
        }
        if (!sessionManager_.InvalidateSession(activeConnectionId_,
                                               session->sessionId)) {
            return;
        }
        activeSessionId = std::move(session->sessionId);
        activeSocket = std::move(socket);
    }
    NotifyAndShutdownActiveSocket(activeSocket, std::move(activeSessionId),
                                  reason);
}

void BridgeWorkerPool::DisconnectActive(std::string_view reason) {
    transport::WebSocketSession::SocketHandle activeSocket;
    std::optional<std::string> activeSessionId;
    {
        //  Same reasoning as DisconnectIfClientActive: the session snapshot must be
        //  read under the same lock that resolved the socket, not afterward, and
        //  scoped to the exact activeConnectionId_ that socket belongs to rather
        //  than an unscoped "whoever is active" query independent of it.
        std::lock_guard<std::mutex> lock(activeSocketMutex_);
        activeSocket = activeSocket_.lock();
        if (activeSocket) {
            auto session = sessionManager_.SessionForConnection(activeConnectionId_);
            if (session.has_value()) {
                (void)sessionManager_.InvalidateSession(activeConnectionId_,
                                                        session->sessionId);
                activeSessionId = std::move(session->sessionId);
            }
        }
    }
    if (activeSocket) {
        NotifyAndShutdownActiveSocket(activeSocket, std::move(activeSessionId),
                                      reason);
    }
}

void BridgeWorkerPool::ShutdownActiveSocket() {
    transport::WebSocketSession::SocketHandle activeSocket;
    {
        std::lock_guard<std::mutex> lock(activeSocketMutex_);
        activeSocket = activeSocket_.lock();
    }
    if (activeSocket) {
        activeSocket->Shutdown();
    }
}

void BridgeWorkerPool::NotifyAndShutdownActiveSocket(
    const transport::WebSocketSession::SocketHandle& socket,
    std::optional<std::string> sessionId, std::string_view reason) {
    auto envelope = protocol::BuildSessionInvalidatedEnvelope(
        std::move(sessionId), std::string(reason));
    envelope.bridgeInstanceId = bridgeInstanceId_;
    auto context = activePlayContext_.AcquireCurrent();
    envelope.playContextId =
        context ? std::optional<std::string>(context->id) : std::nullopt;
    socket->ShutdownWithNotification(protocol::EncodeEnvelope(envelope));
}

void BridgeWorkerPool::Join() {
    if (threadV4_.joinable()) {
        threadV4_.join();
    }
    if (threadV6_.joinable()) {
        threadV6_.join();
    }
    //  After both accept threads have joined, no further connection thread can be
    //  spawned; the current one (if any) is already unwinding from Stop()'s
    //  ShutdownActiveSocket() call.
    std::lock_guard<std::mutex> connectionThreadLock(connectionThreadMutex_);
    JoinConnectionThreadLocked();
}

} //  namespace dovahlink::application
