#include "transport/websocket_session.hpp"

#include "security/limits.hpp"

#include <boost/asio/buffer.hpp>
#include <boost/asio/post.hpp>
#include <boost/beast/core/buffers_to_string.hpp>
#include <boost/beast/core/error.hpp>
#include <boost/beast/core/flat_buffer.hpp>
#include <boost/beast/websocket/error.hpp>
#include <boost/beast/websocket/option.hpp>
#include <boost/beast/websocket/rfc6455.hpp>

#include <chrono>
#include <future>
#include <stdexcept>
#include <utility>

namespace dovahlink::transport {
namespace {

/// Runs one asynchronous stream operation to completion on the worker thread.
template <typename StartOperation>
boost::beast::error_code RunOperation(boost::asio::io_context& ioContext, StartOperation&& startOperation) {
    std::promise<boost::beast::error_code> completion;
    auto result = completion.get_future();

    ioContext.restart();
    std::forward<StartOperation>(startOperation)(
        [&completion](boost::beast::error_code ec, auto...) { completion.set_value(ec); });

    while (result.wait_for(std::chrono::seconds::zero()) != std::future_status::ready) {
        if (ioContext.run_one() == 0) {
            // Preserve every stack-owned operation buffer even if the
            // worker-exclusive event-loop invariant is violated elsewhere.
            result.wait();
        }
    }
    return result.get();
}

}  // namespace

WebSocketSession::Socket::Socket(boost::asio::ip::tcp::socket socket)
    : ioContext_(static_cast<boost::asio::io_context&>(socket.get_executor().context())), stream_(std::move(socket)) {}

void WebSocketSession::Socket::Shutdown() noexcept {
    if (shutdownRequested_.exchange(true, std::memory_order_acq_rel)) {
        return;
    }

    try {
        auto socket = weak_from_this();
        boost::asio::post(ioContext_, [socket = std::move(socket)] {
            auto activeSocket = socket.lock();
            if (!activeSocket) {
                return;
            }
            boost::beast::error_code ec;
            auto& tcpSocket = activeSocket->stream_.next_layer();
            tcpSocket.cancel(ec);
            tcpSocket.shutdown(boost::asio::ip::tcp::socket::shutdown_both, ec);
            tcpSocket.close(ec);
        });
    } catch (...) {
        // Shutdown is a best-effort noexcept boundary. A pending operation still
        // retains its configured timeout if the cancellation handler cannot queue.
    }
}

WebSocketSession::WebSocketSession(boost::asio::ip::tcp::socket socket)
    : WebSocketSession(CreateSocket(std::move(socket))) {}

WebSocketSession::WebSocketSession(SocketHandle socket) : socket_(std::move(socket)) {
    static_cast<void>(RequireSocket(socket_));
}

WebSocketSession::SocketHandle WebSocketSession::CreateSocket(boost::asio::ip::tcp::socket socket) {
    return SocketHandle(new Socket(std::move(socket)));
}

WebSocketSession::Socket& WebSocketSession::RequireSocket(const SocketHandle& socket) {
    if (!socket) {
        throw std::invalid_argument("WebSocketSession requires a socket");
    }
    return *socket;
}

bool WebSocketSession::IsShutdownRequested() const noexcept {
    return socket_->shutdownRequested_.load(std::memory_order_acquire);
}

void WebSocketSession::SetReadTimeout(std::chrono::steady_clock::duration timeout) {
    boost::beast::websocket::stream_base::timeout timeoutOptions;
    timeoutOptions.handshake_timeout = security::kHandshakeTimeout;
    timeoutOptions.idle_timeout = timeout;
    timeoutOptions.keep_alive_pings = false;
    socket_->stream_.set_option(timeoutOptions);
}

std::expected<void, SessionError> WebSocketSession::Accept() {
    if (IsShutdownRequested()) {
        return std::unexpected(SessionError::kHandshakeFailed);
    }

    boost::beast::websocket::permessage_deflate compressionOptions;
    compressionOptions.client_enable = false;
    compressionOptions.server_enable = false;
    socket_->stream_.set_option(compressionOptions);

    socket_->stream_.read_message_max(security::kMaxInboundFrameBytes);

    // Applied before accept() itself so the WebSocket upgrade handshake's
    // own wait is bounded too, not just reads that come after it.
    SetReadTimeout(security::kHandshakeTimeout);

    auto ec = RunOperation(socket_->ioContext_,
                           [this](auto completion) { socket_->stream_.async_accept(std::move(completion)); });
    if (ec) {
        return std::unexpected(SessionError::kHandshakeFailed);
    }
    return {};
}

void WebSocketSession::SwitchToIdleTimeout() {
    if (!IsShutdownRequested()) {
        SetReadTimeout(security::kIdleTimeout);
    }
}

std::expected<std::string, SessionError> WebSocketSession::ReadMessage() {
    if (IsShutdownRequested()) {
        return std::unexpected(SessionError::kReadFailed);
    }
    boost::beast::flat_buffer buffer;
    auto ec = RunOperation(socket_->ioContext_, [this, &buffer](auto completion) {
        socket_->stream_.async_read(buffer, std::move(completion));
    });
    if (ec == boost::beast::websocket::error::message_too_big) {
        return std::unexpected(SessionError::kFrameTooLarge);
    }
    if (ec) {
        return std::unexpected(SessionError::kReadFailed);
    }
    if (socket_->stream_.got_binary()) {
        return std::unexpected(SessionError::kBinaryFrameRejected);
    }
    return boost::beast::buffers_to_string(buffer.data());
}

std::expected<void, SessionError> WebSocketSession::WriteMessage(const std::string& text) {
    if (IsShutdownRequested()) {
        return std::unexpected(SessionError::kWriteFailed);
    }
    socket_->stream_.text(true);
    auto ec = RunOperation(socket_->ioContext_, [this, &text](auto completion) {
        socket_->stream_.async_write(boost::asio::buffer(text), std::move(completion));
    });
    if (ec) {
        return std::unexpected(SessionError::kWriteFailed);
    }
    return {};
}

void WebSocketSession::Close() {
    if (IsShutdownRequested()) {
        return;
    }
    static_cast<void>(RunOperation(socket_->ioContext_, [this](auto completion) {
        socket_->stream_.async_close(boost::beast::websocket::close_code::normal, std::move(completion));
    }));
}

}  // namespace dovahlink::transport
