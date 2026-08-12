#include "transport/websocket_session.hpp"

#include "security/limits.hpp"

#include <boost/asio/buffer.hpp>
#include <boost/beast/core/buffers_to_string.hpp>
#include <boost/beast/core/error.hpp>
#include <boost/beast/core/flat_buffer.hpp>
#include <boost/beast/websocket/error.hpp>
#include <boost/beast/websocket/option.hpp>
#include <boost/beast/websocket/rfc6455.hpp>

#include <winsock2.h>

#include <chrono>
#include <cstdint>
#include <utility>

namespace dovahlink::transport {

WebSocketSession::WebSocketSession(boost::asio::ip::tcp::socket socket) : ws_(std::move(socket)) {}

void WebSocketSession::SetReadTimeout(std::chrono::steady_clock::duration timeout) {
    boost::beast::websocket::stream_base::timeout timeoutOptions;
    timeoutOptions.handshake_timeout = security::kHandshakeTimeout;
    timeoutOptions.idle_timeout = timeout;
    timeoutOptions.keep_alive_pings = false;
    ws_.set_option(timeoutOptions);

    auto timeoutMs = static_cast<std::uint32_t>(
        std::chrono::duration_cast<std::chrono::milliseconds>(timeout).count());
    ::setsockopt(ws_.next_layer().native_handle(), SOL_SOCKET, SO_RCVTIMEO,
                 reinterpret_cast<const char*>(&timeoutMs), sizeof(timeoutMs));
}

std::expected<void, SessionError> WebSocketSession::Accept() {
    boost::beast::websocket::permessage_deflate compressionOptions;
    compressionOptions.client_enable = false;
    compressionOptions.server_enable = false;
    ws_.set_option(compressionOptions);

    ws_.read_message_max(security::kMaxInboundFrameBytes);

    // Applied before accept() itself so the WebSocket upgrade handshake's
    // own wait is bounded too, not just reads that come after it.
    SetReadTimeout(security::kHandshakeTimeout);

    boost::beast::error_code ec;
    ws_.accept(ec);
    if (ec) {
        return std::unexpected(SessionError::kHandshakeFailed);
    }
    return {};
}

void WebSocketSession::SwitchToIdleTimeout() { SetReadTimeout(security::kIdleTimeout); }

std::expected<std::string, SessionError> WebSocketSession::ReadMessage() {
    boost::beast::flat_buffer buffer;
    boost::beast::error_code ec;
    ws_.read(buffer, ec);
    if (ec == boost::beast::websocket::error::message_too_big) {
        return std::unexpected(SessionError::kFrameTooLarge);
    }
    if (ec) {
        return std::unexpected(SessionError::kReadFailed);
    }
    if (ws_.got_binary()) {
        return std::unexpected(SessionError::kBinaryFrameRejected);
    }
    return boost::beast::buffers_to_string(buffer.data());
}

std::expected<void, SessionError> WebSocketSession::WriteMessage(const std::string& text) {
    ws_.text(true);
    boost::beast::error_code ec;
    ws_.write(boost::asio::buffer(text), ec);
    if (ec) {
        return std::unexpected(SessionError::kWriteFailed);
    }
    return {};
}

void WebSocketSession::Close() {
    boost::beast::error_code ec;
    ws_.close(boost::beast::websocket::close_code::normal, ec);
}

}  // namespace dovahlink::transport
