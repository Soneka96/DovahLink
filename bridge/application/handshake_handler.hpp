#pragma once

#include "application/connection_timeout_tracker.hpp"
#include "application/session.hpp"
#include "protocol/envelope.hpp"
#include "security/throttle.hpp"
#include "security/token_store.hpp"

#include <chrono>

namespace dovahlink::application {

/// Only protocol version currently supported by the bridge after negotiation.
inline constexpr std::int64_t kSupportedProtocolVersion = 1;

/// Result of processing one client hello message.
struct HandshakeResult {
    /// Response envelope to send to the client.
    protocol::Envelope response;

    /// Whether the connection must close after sending `response`.
    bool closeConnection = false;
};

/// Validates one decoded hello, throttles failures, and atomically consumes the token on success.
/// Successful handshakes bind a new session to `connection`; failures close the connection.
/// @param helloEnvelope Decoded client hello envelope.
/// @param tokenStore One-time token store.
/// @param tokenThrottle Global failed-token throttle.
/// @param sessionManager Session registry.
/// @param connection Transport connection identifier.
/// @param timeoutTracker Connection timeout tracker.
/// @param now Current monotonic time.
/// @return Response envelope and close decision for the connection.
[[nodiscard]] HandshakeResult HandleHello(const protocol::Envelope& helloEnvelope,
                                           security::TokenStore& tokenStore,
                                           security::FailedTokenThrottle& tokenThrottle,
                                           SessionManager& sessionManager, ConnectionId connection,
                                           ConnectionTimeoutTracker& timeoutTracker,
                                           std::chrono::steady_clock::time_point now);

}  // namespace dovahlink::application
