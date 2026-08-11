#include "transport/websocket_session.hpp"

#include "security/limits.hpp"

#include <boost/asio/buffer.hpp>
#include <boost/beast/core/buffers_to_string.hpp>
#include <boost/beast/core/error.hpp>
#include <boost/beast/core/flat_buffer.hpp>
#include <boost/beast/websocket/permessage_deflate.hpp>
#include <boost/beast/websocket/rfc6455.hpp>

#include <utility>

namespace dovahlink::transport {

WebSocketSession::WebSocketSession(boost::asio::ip::tcp::socket socket) : ws_(std::move(socket)) {}

std::expected<void, SessionError> WebSocketSession::Accept() {
    boost::beast::websocket::permessage_deflate compressionOptions;
    compressionOptions.client_enable = false;
    compressionOptions.server_enable = false;
    ws_.set_option(compressionOptions);

    boost::beast::websocket::stream_base::timeout timeoutOptions;
    timeoutOptions.handshake_timeout = security::kHandshakeTimeout;
    timeoutOptions.idle_timeout = security::kIdleTimeout;
    timeoutOptions.keep_alive_pings = false;
    ws_.set_option(timeoutOptions);

    boost::beast::error_code ec;
    ws_.accept(ec);
    if (ec) {
        return std::unexpected(SessionError::kHandshakeFailed);
    }
    return {};
}

std::expected<std::string, SessionError> WebSocketSession::ReadMessage() {
    boost::beast::flat_buffer buffer;
    boost::beast::error_code ec;
    ws_.read(buffer, ec);
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
