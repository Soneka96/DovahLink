#pragma once

#include <boost/asio/ip/tcp.hpp>
#include <boost/beast/websocket/stream.hpp>

#include <expected>
#include <string>

namespace dovahlink::transport {

enum class SessionError {
    kHandshakeFailed,
    kReadFailed,
    kWriteFailed,
    kBinaryFrameRejected,
    // The inbound frame exceeded bridge/security/limits.hpp's
    // kMaxInboundFrameBytes (1 MiB), enforced by Boost.Beast before the
    // message could be fully buffered. Per
    // ai/context/protocol/security.md ("close immediately when framing or
    // size validation fails before a safe message can be decoded; do not
    // attempt to send an error over an invalid or oversized frame"), the
    // caller must close the connection immediately on this error rather
    // than routing it through the 3-violations/30s throttle
    // (bridge/security/throttle.hpp's ViolationTracker) that other,
    // post-decode protocol violations use.
    kFrameTooLarge,
};

// Wraps one accepted TCP connection as a WebSocket session with the Phase 1
// framing rules from ai/context/protocol/security.md: compression disabled,
// binary frames rejected (only UTF-8 JSON text messages are accepted and
// sent), and the documented handshake/idle timeouts
// (bridge/security/limits.hpp) enforced by Boost.Beast's own timeout
// mechanism. Beast's built-in keep-alive pings are deliberately left off:
// DovahLink's liveness proof is the registered `ping`/`pong` application
// message pair (protocol/schema/README.md), not the WebSocket protocol's
// own control-frame ping/pong, so enabling both would be a redundant,
// overlapping liveness mechanism.
//
// Compression being off is verified by code inspection (the explicit
// `client_enable = false, server_enable = false` in Accept), not by a
// runtime test: Beast does not expose a simple, documented public accessor
// for "was permessage-deflate negotiated" to introspect from outside the
// stream after the handshake.
class WebSocketSession {
public:
    explicit WebSocketSession(boost::asio::ip::tcp::socket socket);

    // Performs the WebSocket accept handshake with compression disabled,
    // the documented timeouts configured, and the maximum inbound message
    // size bounded to bridge/security/limits.hpp's kMaxInboundFrameBytes.
    [[nodiscard]] std::expected<void, SessionError> Accept();

    // Reads one message. Rejects (and does not return the content of) a
    // binary frame; the caller treats that as a protocol violation.
    [[nodiscard]] std::expected<std::string, SessionError> ReadMessage();

    // Writes one UTF-8 text frame.
    [[nodiscard]] std::expected<void, SessionError> WriteMessage(const std::string& text);

    // Closes the WebSocket connection with a normal closure code.
    void Close();

private:
    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> ws_;
};

}  // namespace dovahlink::transport
