#include "application/bridge_worker_pool.hpp"

#include "transport/websocket_session.hpp"

#include <boost/asio/ip/tcp.hpp>
#include <boost/system/error_code.hpp>

namespace dovahlink::application {

BridgeWorkerPool::BridgeWorkerPool(transport::LoopbackListener& listenerV4, transport::LoopbackListener& listenerV6,
                                    transport::ConnectionSlot& slot, security::TokenStore& tokenStore,
                                    security::FailedTokenThrottle& tokenThrottle, SessionManager& sessionManager,
                                    const CharacterStateProvider& stateProvider)
    : listenerV4_(listenerV4),
      listenerV6_(listenerV6),
      slot_(slot),
      tokenStore_(tokenStore),
      tokenThrottle_(tokenThrottle),
      sessionManager_(sessionManager),
      stateProvider_(stateProvider) {}

BridgeWorkerPool::~BridgeWorkerPool() {
    Stop();
    Join();
}

void BridgeWorkerPool::AcceptLoop(transport::LoopbackListener& listener) {
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
        transport::WebSocketSession session(std::move(socket));
        RunConnectionSession(session, tokenStore_, tokenThrottle_, sessionManager_, connection, stateProvider_);
    }
}

void BridgeWorkerPool::Start() {
    threadV4_ = std::thread([this] { AcceptLoop(listenerV4_); });
    threadV6_ = std::thread([this] { AcceptLoop(listenerV6_); });
}

void BridgeWorkerPool::Stop() {
    stopping_.store(true, std::memory_order_release);
    boost::system::error_code ec;
    listenerV4_.Acceptor().close(ec);
    listenerV6_.Acceptor().close(ec);
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
