#include "application/bridge_worker_pool.hpp"

#include "transport/websocket_session.hpp"

#include <boost/asio/ip/tcp.hpp>
#include <boost/system/error_code.hpp>

#include <string>

namespace dovahlink::application {

BridgeWorkerPool::BridgeWorkerPool(
    transport::LoopbackListener& listenerV4,
    transport::LoopbackListener& listenerV6, transport::IConnectionSlot& slot,
    security::ITokenStore& tokenStore,
    security::IFailedTokenThrottle& tokenThrottle,
    security::ITrustStore& trustStore,
    security::IFailedTokenThrottle& credentialThrottle,
    SessionManager& sessionManager,
    const IActivePlayContextReader& activePlayContext,
    IActiveSessionSocket& activeSessionSocket,
    security::IPairingSession& pairingSession,
    ITrustMutationCoordinator& mutationCoordinator,
    PairingNotificationSink& pairingNotificationSink,
    std::optional<std::string> bridgeInstanceId, std::string bridgeVersion)
    : listenerV4_(listenerV4), listenerV6_(listenerV6), slot_(slot),
      tokenStore_(tokenStore), tokenThrottle_(tokenThrottle),
      trustStore_(trustStore), credentialThrottle_(credentialThrottle),
      sessionManager_(sessionManager), activePlayContext_(activePlayContext),
      activeSessionSocket_(activeSessionSocket),
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
    boost::asio::ip::tcp::socket socket, shared::ScopedRelease slotLease) {
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
                activeSessionSocket_.Publish(connection, socketHandle);
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
    activeSessionSocket_.Shutdown();
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
    //  active-session controller shutdown call.
    std::lock_guard<std::mutex> connectionThreadLock(connectionThreadMutex_);
    JoinConnectionThreadLocked();
}

} //  namespace dovahlink::application
