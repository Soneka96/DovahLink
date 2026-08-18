#pragma once

#include "application/connection_timeout_tracker.hpp"
#include "application/play_context.hpp"
#include "application/session.hpp"
#include "protocol/envelope.hpp"
#include "security/throttle.hpp"
#include "security/token_store.hpp"
#include "security/trust_store.hpp"

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

/// Validates one decoded hello and consumes the presented credential only after session admission
/// succeeds. Successful handshakes bind a new session to `connection`; failures close the
/// connection. Branches on `hello.auth.method`: `one_time_local_token` (developer authentication,
/// admits `kFull`) and `trusted_device_credential` (a persisted pairing credential checked via
/// `trustStore.Authenticate`, admits `kFull`) both require a matching secret; `unpaired` requires
/// none and admits `kRestricted`. `hello_ack.clientIdentityKind` is `"paired"` only for
/// `trusted_device_credential`; both other methods report `"unpaired"`, per `security.md`'s "Hello
/// authentication and session trust tiers".
/// @param helloEnvelope Decoded client hello envelope.
/// @param tokenStore One-time token store, consulted for `auth.method: "one_time_local_token"`.
/// @param tokenThrottle Global failed one-time-token attempt throttle.
/// @param trustStore Persistent trust store, consulted for `auth.method:
///     "trusted_device_credential"`.
/// @param credentialThrottle Global failed device-credential attempt throttle, separate from
///     `tokenThrottle` so guessing one cannot block or be blocked by the other.
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
                                           security::TrustStore& trustStore,
                                           security::FailedTokenThrottle& credentialThrottle,
                                           SessionManager& sessionManager, ConnectionId connection,
                                           ConnectionTimeoutTracker& timeoutTracker,
                                           std::chrono::steady_clock::time_point now,
                                           const std::optional<std::string>& bridgeInstanceId = std::nullopt,
                                           const ActivePlayContext& activePlayContext = ActivePlayContext(),
                                           const std::string& bridgeVersion = "0.0.0");

}  // namespace dovahlink::application
