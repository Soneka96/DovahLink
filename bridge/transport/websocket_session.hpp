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
    // the maximum inbound message size bounded to bridge/security/
    // limits.hpp's kMaxInboundFrameBytes, and Beast's idle-read timeout
    // initialized to kHandshakeTimeout (5s). Beast's own handshake_timeout
    // option only covers the WebSocket protocol upgrade itself (the HTTP
    // Upgrade exchange completed inside this call); it has no separate
    // concept of "the first application-level read waits less time than
    // later ones", so the 5-second wait for the caller's first message
    // (hello) is implemented by starting idle_timeout at kHandshakeTimeout
    // rather than kIdleTimeout. The caller must call SwitchToIdleTimeout
    // once authentication succeeds, or every read after the first will
    // still only be allowed 5 seconds.
    [[nodiscard]] std::expected<void, SessionError> Accept();

    // Re-applies Beast's idle-read timeout to kIdleTimeout (60s), replacing
    // the kHandshakeTimeout (5s) window Accept() started with. Call this
    // once authentication succeeds (ai/context/protocol/security.md's
    // "60-second idle connection timeout without a valid heartbeat or
    // message" applies from that point on, not before).
    void SwitchToIdleTimeout();

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
