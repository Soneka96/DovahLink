#include "application/handshake_handler.hpp"

#include "protocol/messages.hpp"
#include "security/csprng.hpp"
#include "security/hex.hpp"

#include <cstdint>
#include <optional>
#include <string>
#include <utility>
#include <vector>

// Design notes on decisions not spelled out verbatim in
// protocol/schema/README.md or ai/context/protocol/security.md:
//
// - Every failure path closes the connection rather than leaving it open
//   for a retry on the same socket. security::FailedTokenThrottle is
//   documented as "shared across every connection attempt, not one per
//   connection", which only makes sense if a client's next attempt after a
//   failure is a fresh connection, not a second hello on the same socket.
//   This also keeps the pre-session state space small: there is no partial-
//   session bookkeeping to build for "a socket that failed hello once but
//   might still succeed."
// - Checks run cheapest/most-structural first: malformed shape, then the
//   token (which is scarce and one-time, so it should not be spent
//   validating a connection that was going to be rejected anyway for a
//   reason unrelated to authentication).
// - All three of protocol/fixtures/errors/error-unauthenticated-*.json use
//   the same wire `code: "unauthenticated"`; only their diagnostic
//   `message` text differs, and message text is explicitly "not for
//   branching" (protocol/schema/README.md). security::TokenStore::TryReserve
//   itself intentionally does not distinguish invalid/expired/reused, so a
//   single generic message covers all three without losing any
//   client-observable information.
// - A session slot already occupied (the one-connected-client limit) maps
//   to `unauthorized`: the presented token was valid, but this connection
//   is not currently permitted to hold a session. No canonical error code
//   names this exact case; `unauthorized` is the closest semantic fit among
//   protocol/schema/README.md's registered codes. TokenStore::Reservation
//   keeps the matching token unchanged until session admission commits, so
//   this retryable failure does not spend the one-time token.
// - A rejected trusted_device_credential gets `revoked` instead of the generic
//   `unauthenticated` when TrustStore::IsRevoked reports true for the presented clientId:
//   revocation is a bridge-side decision the client did not cause, and the distinct code lets the
//   app return the user directly to pairing rather than retrying a dead credential forever
//   (ai/context/protocol/security.md's "Persistent local trust").
// - A kBlocked clientId is rejected with `blocked` before any auth-method-specific work runs
//   (throttle bookkeeping, credential comparison), not folded into the trusted_device_credential
//   branch's existing `revoked` check: ROADMAP.md's "3.2 Known Device & Trust Administration"
//   requires blocking to reject "as early as hello" for *both* trusted_device_credential and
//   unpaired attempts (an unpaired session is how a blocked device would otherwise reach
//   pairing_request again), and `blocked` must never be conflated with `revoked`. Developer-token
//   (one_time_local_token) authentication is exempt -- it stays a separate provider never
//   redefined as a paired device by Known Device blocking (security.md's "Developer
//   authentication"). This check touches no throttle or trust-store mutation state, so a blocked
//   attempt updates no persisted metadata, matching the acceptance criterion.
// - Handshake-timeout closure (checking ConnectionTimeoutTracker::IsTimedOut
//   independent of a message actually arriving) is not this function's job;
//   this function only runs once a hello message has already been read.
//   That belongs to whatever owns the read loop, added in a later step.

namespace dovahlink::application {

namespace {

/// Builds a failed handshake response that closes the connection.
/// @param helloEnvelope Envelope from the failed hello request.
/// @param bridgeInstanceId This bridge's own identity, stamped onto the response when known.
/// @param code Canonical protocol error code.
/// @param message Safe diagnostic message.
/// @param retryable Whether a fresh connection may retry.
HandshakeResult Fail(const protocol::Envelope& helloEnvelope, const std::optional<std::string>& bridgeInstanceId,
                      std::string code, std::string message, bool retryable) {
    protocol::Envelope response = protocol::BuildErrorEnvelope(helloEnvelope.messageId, /*sessionId=*/std::nullopt,
                                                                std::move(code), std::move(message), retryable);
    response.bridgeInstanceId = bridgeInstanceId;
    return HandshakeResult{
        .response = std::move(response),
        .sessionLease = std::nullopt,
        .closeConnection = true,
    };
}

}

HandshakeResult HandleHello(const protocol::Envelope& helloEnvelope, security::TokenStore& tokenStore,
                             security::FailedTokenThrottle& tokenThrottle, security::TrustStore& trustStore,
                             security::FailedTokenThrottle& credentialThrottle, SessionManager& sessionManager,
                             ConnectionId connection, ConnectionTimeoutTracker& timeoutTracker,
                             std::chrono::steady_clock::time_point now,
                             const std::optional<std::string>& bridgeInstanceId,
                             const ActivePlayContext& activePlayContext, const std::string& bridgeVersion) {
    auto hello = protocol::DecodeHelloPayload(helloEnvelope.payload);
    if (!hello.has_value()) {
        return Fail(helloEnvelope, bridgeInstanceId, "malformed_message", "Malformed hello payload", false);
    }

    if (hello->authMethod != "one_time_local_token" && trustStore.IsBlocked(hello->clientId)) {
        return Fail(helloEnvelope, bridgeInstanceId, "blocked", "This device is blocked", false);
    }

    // A structurally invalid presented credential (not valid hex) can never match a stored one;
    // treat it as an immediate failed attempt without a store lookup, matching this codebase's
    // existing accepted precedent for Phase 1's timing-side-channel posture (see
    // token_store.cpp's "ponytail:" comment on TryReserve). Shared by both credentialed methods
    // below; "unpaired" has no credential to decode at all.
    auto presentedBytes = [&hello]() -> std::optional<std::vector<std::uint8_t>> {
        return hello->authToken.has_value() ? security::DecodeHex(*hello->authToken)
                                             : std::optional<std::vector<std::uint8_t>>{};
    };

    SessionTrustTier trustTier;
    SessionAuthMethod authMethod;
    std::optional<security::TokenStore::Reservation> tokenReservation;

    if (hello->authMethod == "one_time_local_token") {
        if (tokenThrottle.IsBlocked(now)) {
            return Fail(helloEnvelope, bridgeInstanceId, "rate_limited", "Too many failed token attempts", true);
        }
        auto bytes = presentedBytes();
        tokenReservation =
            bytes.has_value() ? tokenStore.TryReserve(*bytes) : std::optional<security::TokenStore::Reservation>{};
        if (!tokenReservation.has_value()) {
            tokenThrottle.RecordFailure(now);
            return Fail(helloEnvelope, bridgeInstanceId, "unauthenticated", "Invalid or expired one-time token",
                        false);
        }
        trustTier = SessionTrustTier::kFull;
        authMethod = SessionAuthMethod::kDeveloperToken;
    } else if (hello->authMethod == "unpaired") {
        // No credential to present or check yet; the session is admitted restricted, per
        // security.md's "Hello authentication and session trust tiers".
        trustTier = SessionTrustTier::kRestricted;
        authMethod = SessionAuthMethod::kUnpaired;
    } else {
        // Only "trusted_device_credential" remains, per DecodeHelloPayload's validated set.
        if (credentialThrottle.IsBlocked(now)) {
            return Fail(helloEnvelope, bridgeInstanceId, "rate_limited", "Too many failed credential attempts",
                        true);
        }
        auto bytes = presentedBytes();
        bool authenticated = bytes.has_value() && trustStore.Authenticate(hello->clientId, *bytes);
        if (!authenticated) {
            credentialThrottle.RecordFailure(now);
            if (trustStore.IsRevoked(hello->clientId)) {
                return Fail(helloEnvelope, bridgeInstanceId, "revoked", "This device's trust was revoked", false);
            }
            return Fail(helloEnvelope, bridgeInstanceId, "unauthenticated",
                        "Invalid or unrecognized device credential", false);
        }
        trustTier = SessionTrustTier::kFull;
        authMethod = SessionAuthMethod::kTrustedDeviceCredential;
    }

    auto sessionId = security::GenerateOpaqueId();
    auto responseMessageId = security::GenerateOpaqueId();
    if (!sessionId.has_value() || !responseMessageId.has_value()) {
        return Fail(helloEnvelope, bridgeInstanceId, "internal_error", "Unable to establish a secure connection",
                    false);
    }

    // "paired" only for a session admitted with an already-persisted trust credential; a
    // developer-authenticated or still-unpaired session reports "unpaired" regardless of trust
    // tier (security.md: developer authentication is never wire-visible as "paired").
    std::string clientIdentityKind = hello->authMethod == "trusted_device_credential" ? "paired" : "unpaired";

    auto context = activePlayContext.AcquireCurrent();
    protocol::Envelope response{
        .messageType = std::string(protocol::message_type::kHelloAck),
        .messageId = *responseMessageId,
        .sessionId = sessionId,
        .correlationId = helloEnvelope.messageId,
        .payload = protocol::EncodeHelloAckPayload(protocol::HelloAckPayload{
            .bridgeVersion = bridgeVersion,
            .clientIdentityKind = std::move(clientIdentityKind),
        }),
        // bridgeInstanceId is this bridge's own identity; playContextId
        // reflects whatever play context is already active at connect time
        // (e.g. a reconnect mid-game), which may be none. clientId echoes
        // the identity the client just established in this same hello.
        .bridgeInstanceId = bridgeInstanceId,
        .playContextId = context ? std::optional<std::string>(context->id) : std::nullopt,
        .clientId = hello->clientId,
    };

    auto sessionLease = sessionManager.TryCreateSession(connection, *sessionId, hello->clientId, trustTier,
                                                         authMethod);
    if (!sessionLease.has_value()) {
        return Fail(helloEnvelope, bridgeInstanceId, "unauthorized", "Another client is already connected", true);
    }

    if (tokenReservation.has_value()) {
        tokenReservation->Commit();
    }
    timeoutTracker.MarkAuthenticated(now);

    return HandshakeResult{
        .response = std::move(response),
        .sessionLease = std::move(*sessionLease),
        .closeConnection = false,
    };
}

}  // namespace dovahlink::application
