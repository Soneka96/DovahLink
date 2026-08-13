#pragma once

#include <boost/asio/ip/tcp.hpp>
#include <boost/beast/websocket/stream.hpp>

#include <chrono>
#include <expected>
#include <string>

namespace dovahlink::transport {

/// Identifies WebSocket session operation failures.
enum class SessionError {
    /// The WebSocket upgrade handshake failed.
    kHandshakeFailed,
    /// A message could not be read.
    kReadFailed,
    /// A message could not be written.
    kWriteFailed,
    /// A binary frame was received where text is required.
    kBinaryFrameRejected,
    /// The inbound frame exceeded the configured size limit and must be closed
    /// immediately rather than routed through post-decode violation handling.
    kFrameTooLarge,
};

/// Owns one accepted TCP connection and applies the Phase 1 WebSocket rules:
/// compression and WebSocket keep-alive pings are disabled, only text frames
/// are accepted, and handshake/idle receive timeouts are enforced.
class WebSocketSession {
public:
    /// Takes ownership of the accepted TCP socket.
    explicit WebSocketSession(boost::asio::ip::tcp::socket socket);

    /// Performs a compression-disabled, size-bounded WebSocket handshake.
    /// Starts the handshake timeout; callers must switch to the idle timeout
    /// after authentication succeeds.
    [[nodiscard]] std::expected<void, SessionError> Accept();

    /// Replaces the handshake timeout with the authenticated idle timeout.
    void SwitchToIdleTimeout();

    /// Reads one text message, rejecting binary frames and oversized frames.
    [[nodiscard]] std::expected<std::string, SessionError> ReadMessage();

    /// Writes one UTF-8 text message.
    [[nodiscard]] std::expected<void, SessionError> WriteMessage(const std::string& text);

    /// Closes the WebSocket with a normal close code.
    void Close();

private:
    /// Applies Beast options and the OS receive timeout for a blocking operation.
    void SetReadTimeout(std::chrono::steady_clock::duration timeout);

    /// Owned WebSocket stream and its underlying TCP socket.
    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> ws_;
};

}  // namespace dovahlink::transport
