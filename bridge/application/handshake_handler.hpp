#pragma once

#include "application/connection_timeout_tracker.hpp"
#include "application/session.hpp"
#include "protocol/envelope.hpp"
#include "security/throttle.hpp"
#include "security/token_store.hpp"

#include <array>
#include <chrono>
#include <optional>

namespace dovahlink::application {

/// Only protocol version currently supported by the bridge after negotiation.
/// Superseded by `kSupportedProtocolVersions` for negotiation itself; still
/// used by call sites not yet threaded onto their connection's own
/// negotiated version.
inline constexpr std::int64_t kSupportedProtocolVersion = 1;

/// Protocol versions this bridge can negotiate with a client, ascending.
/// `HandleHello` selects the highest version also present in the client's
/// `supportedProtocolVersions`.
inline constexpr std::array<std::int64_t, 2> kSupportedProtocolVersions = {1, 2};

/// Result of processing one client hello message.
struct HandshakeResult {
    /// Response envelope to send to the client.
    protocol::Envelope response;

    /// Ownership of the authenticated session on success.
    std::optional<SessionManager::Lease> sessionLease;

    /// Whether the connection must close after sending `response`.
    bool closeConnection = false;

    /// Protocol version actually negotiated on success; `0` on any failure
    /// path. Distinct from `response.protocolVersion`, which stays `0` for
    /// `hello_ack` itself even when v1 is negotiated (protocol/schema/README.md).
    std::int64_t negotiatedProtocolVersion = 0;
};

/// Validates one decoded hello and consumes the token only after session admission succeeds.
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
