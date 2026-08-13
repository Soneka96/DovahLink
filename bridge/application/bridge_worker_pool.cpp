#include "application/bridge_worker_pool.hpp"

#include "transport/websocket_session.hpp"

#include <boost/asio/ip/tcp.hpp>
#include <boost/system/error_code.hpp>

namespace dovahlink::application {

/**
       * @brief Creates a worker pool for accepting IPv4 and IPv6 loopback connections.
       *
       * @param listenerV4 IPv4 loopback listener.
       * @param listenerV6 IPv6 loopback listener.
       * @param slot Connection slot that controls active connection admission.
       * @param tokenStore Token store used to authenticate connections.
       * @param tokenThrottle Failed-token throttling service.
       * @param sessionManager Session manager used by accepted connections.
       * @param stateProvider Provider of character state for active sessions.
       */
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

/**
 * @brief Stops the listeners and joins their worker threads.
 */
BridgeWorkerPool::~BridgeWorkerPool() {
    Stop();
    Join();
}

/**
 * @brief Accepts and runs loopback connections for a listener.
 *
 * Continues accepting connections until stopping is requested or the listener
 * can no longer accept connections. Only one active connection is admitted at
 * a time.
 *
 * @param listener Listener from which loopback connections are accepted.
 */
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
        if (!slot_.TryAcquire()) {
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
        slot_.Release();
    }
}

/**
 * @brief Starts the IPv4 and IPv6 connection-acceptance worker threads.
 */
void BridgeWorkerPool::Start() {
    threadV4_ = std::thread([this] { AcceptLoop(listenerV4_); });
    threadV6_ = std::thread([this] { AcceptLoop(listenerV6_); });
}

/**
 * @brief Requests the worker threads to stop and closes both listeners.
 */
void BridgeWorkerPool::Stop() {
    stopping_.store(true, std::memory_order_release);
    boost::system::error_code ec;
    listenerV4_.Acceptor().close(ec);
    listenerV6_.Acceptor().close(ec);
}

/**
 * @brief Waits for the IPv4 and IPv6 worker threads to finish.
 */
void BridgeWorkerPool::Join() {
    if (threadV4_.joinable()) {
        threadV4_.join();
    }
    if (threadV6_.joinable()) {
        threadV6_.join();
    }
}

}  // namespace dovahlink::application
