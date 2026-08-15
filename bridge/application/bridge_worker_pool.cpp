#include "application/bridge_worker_pool.hpp"

#include "transport/websocket_session.hpp"

#include <boost/asio/ip/tcp.hpp>
#include <boost/system/error_code.hpp>

namespace dovahlink::application {

BridgeWorkerPool::BridgeWorkerPool(transport::LoopbackListener& listenerV4, transport::LoopbackListener& listenerV6,
                                   transport::ConnectionSlot& slot, security::TokenStore& tokenStore,
                                   security::FailedTokenThrottle& tokenThrottle, SessionManager& sessionManager,
                                   const ActivePlayContext& activePlayContext,
                                   std::optional<std::string> bridgeInstanceId, std::string bridgeVersion)
    : listenerV4_(listenerV4), listenerV6_(listenerV6), slot_(slot), tokenStore_(tokenStore),
      tokenThrottle_(tokenThrottle), sessionManager_(sessionManager), activePlayContext_(activePlayContext),
      bridgeInstanceId_(std::move(bridgeInstanceId)), bridgeVersion_(std::move(bridgeVersion)) {}

BridgeWorkerPool::~BridgeWorkerPool() {
    Stop();
    Join();
}

void BridgeWorkerPool::AcceptLoop(transport::LoopbackListener& listener, const ContainedWorkRunner& workerRunner) {
    while (!stopping_.load(std::memory_order_acquire)) {
        auto accepted = listener.AcceptLoopbackOnly();
        if (!accepted.has_value()) {
            if (accepted.error() == transport::AcceptError::kNonLoopbackPeerRejected) {
                // The acceptor itself is still fine; only this one peer was
                // rejected. Try again.
                continue;
            }
            // kAcceptFailed: either Stop() closed the acceptor (expected
            // shutdown) or it failed for some other reason. Either way,
            // this acceptor cannot be used again.
            return;
        }

        boost::asio::ip::tcp::socket socket = std::move(*accepted);
        auto slotLease = slot_.TryAcquire();
        if (!slotLease.has_value()) {
            // Phase 1 allows exactly one connected client
            // (ai/context/protocol/security.md); reject without a WebSocket
            // handshake, matching ConnectionSlot's documented contract.
            boost::system::error_code ec;
            socket.close(ec);
            continue;
        }

        ConnectionId connection = nextConnectionId_.fetch_add(1, std::memory_order_relaxed);
        (void)workerRunner([this, connection, socket = std::move(socket)]() mutable {
            auto socketHandle = transport::WebSocketSession::CreateSocket(std::move(socket));
            {
                std::lock_guard<std::mutex> lock(activeSocketMutex_);
                activeSocket_ = socketHandle;
            }
            if (stopping_.load(std::memory_order_acquire)) {
                socketHandle->Shutdown();
                return;
            }

            transport::WebSocketSession session(std::move(socketHandle));
            RunConnectionSession(session, tokenStore_, tokenThrottle_, sessionManager_, connection,
                                 activePlayContext_, bridgeInstanceId_, bridgeVersion_);
        });
    }
}

void BridgeWorkerPool::Start(ContainedWorkRunner workerRunner) {
    threadV4_ = std::thread(
        [this, workerRunner] { (void)workerRunner([this, workerRunner] { AcceptLoop(listenerV4_, workerRunner); }); });
    threadV6_ = std::thread(
        [this, workerRunner] { (void)workerRunner([this, workerRunner] { AcceptLoop(listenerV6_, workerRunner); }); });
}

void BridgeWorkerPool::Stop() {
    stopping_.store(true, std::memory_order_release);
    boost::system::error_code ec;
    listenerV4_.Acceptor().close(ec);
    listenerV6_.Acceptor().close(ec);

    transport::WebSocketSession::SocketHandle activeSocket;
    {
        std::lock_guard<std::mutex> lock(activeSocketMutex_);
        activeSocket = activeSocket_.lock();
    }
    if (activeSocket) {
        activeSocket->Shutdown();
    }
}

void BridgeWorkerPool::Join() {
    if (threadV4_.joinable()) {
        threadV4_.join();
    }
    if (threadV6_.joinable()) {
        threadV6_.join();
    }
}

}  // namespace dovahlink::application
