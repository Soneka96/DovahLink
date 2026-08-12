#pragma once

#include <boost/asio/ip/tcp.hpp>
#include <boost/beast/websocket/stream.hpp>

#include <chrono>
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
// sent), and the documented handshake/idle timeouts (bridge/security/
// limits.hpp) enforced at the OS socket level -- see SetReadTimeout for why
// this class does not rely on Boost.Beast's own stream_base::timeout option
// to do this, even though it still sets that option too. Beast's built-in
// keep-alive pings are deliberately left off: DovahLink's liveness proof is
// the registered `ping`/`pong` application message pair (protocol/schema/
// README.md), not the WebSocket protocol's own control-frame ping/pong, so
// enabling both would be a redundant, overlapping liveness mechanism.
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
    // limits.hpp's kMaxInboundFrameBytes, and the read timeout bounded to
    // kHandshakeTimeout (5s) -- see SetReadTimeout. The caller must call
    // SwitchToIdleTimeout once authentication succeeds, or every
    // subsequent read stays bounded to 5 seconds instead of the 60-second
    // idle window.
    [[nodiscard]] std::expected<void, SessionError> Accept();

    // Switches the read timeout to kIdleTimeout (60s), replacing the
    // kHandshakeTimeout (5s) window Accept() started with. Call this once
    // authentication succeeds (ai/context/protocol/security.md's
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
    // The single place that owns "how long may a blocking operation on
    // this connection wait before giving up." Sets Beast's own
    // stream_base::timeout option (handshake_timeout fixed at
    // kHandshakeTimeout, idle_timeout set to `timeout`, keep_alive_pings
    // left off) purely for documentation/consistency -- it does not by
    // itself bound this class's synchronous accept/read/write calls, or
    // the graceful-close wait inside Beast's own internal error handling,
    // because Beast only arms that option's timer for its asynchronous
    // API. The actual enforcement is an OS-level receive timeout
    // (SO_RCVTIMEO) applied directly to the underlying socket, which
    // bounds every blocking recv() this stream performs no matter which
    // Beast code path makes it, including ones inside Beast's own
    // internals this class has no direct call site for. Called from
    // Accept() (kHandshakeTimeout, before the accept handshake itself so
    // that wait is bounded too) and SwitchToIdleTimeout() (kIdleTimeout).
    // If the enforcement mechanism or window values ever need to change,
    // this is the only place that needs to.
    void SetReadTimeout(std::chrono::steady_clock::duration timeout);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> ws_;
};

}  // namespace dovahlink::transport
