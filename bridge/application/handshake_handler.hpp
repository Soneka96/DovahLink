#pragma once

#include "application/connection_timeout_tracker.hpp"
#include "application/play_context.hpp"
#include "application/session.hpp"
#include "protocol/envelope.hpp"
#include "security/throttle.hpp"
#include "security/token_store.hpp"

#include <chrono>
#include <optional>
#include <string>

namespace dovahlink::application {

/// Result of processing one client hello message.
struct HandshakeResult {
    /// Response envelope to send to the client.
    protocol::Envelope response;

    /// Ownership of the authenticated session on success.
    std::optional<SessionManager::Lease> sessionLease;

    /// Whether the connection must close after sending `response`.
    bool closeConnection = false;
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
/// @param bridgeInstanceId This bridge process's identity, stamped onto the response; no value if
///     generation failed at startup, or if this call site does not participate in identity
///     stamping (the default, for callers that do not care, e.g. most handshake-mechanics tests).
/// @param activePlayContext Source of the play context active at connect time, stamped onto the
///     response's `playContextId`; an empty (kNoContext) default when unspecified.
/// @param bridgeVersion The DovahLink Bridge/mod release version exposed to the client in
///     `hello_ack.bridgeVersion` for its own compatibility check (`ai/context/protocol/compatibility.md`);
///     a placeholder default for call sites that do not exercise bridge-version behavior.
/// @return Response envelope and close decision for the connection.
[[nodiscard]] HandshakeResult HandleHello(const protocol::Envelope& helloEnvelope,
                                           security::TokenStore& tokenStore,
                                           security::FailedTokenThrottle& tokenThrottle,
                                           SessionManager& sessionManager, ConnectionId connection,
                                           ConnectionTimeoutTracker& timeoutTracker,
                                           std::chrono::steady_clock::time_point now,
                                           const std::optional<std::string>& bridgeInstanceId = std::nullopt,
                                           const ActivePlayContext& activePlayContext = ActivePlayContext(),
                                           const std::string& bridgeVersion = "0.0.0");

}  // namespace dovahlink::application
