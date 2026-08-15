#include "application/handshake_handler.hpp"

#include "protocol/messages.hpp"
#include "security/csprng.hpp"
#include "security/hex.hpp"

#include <string>
#include <utility>

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
                             security::FailedTokenThrottle& tokenThrottle, SessionManager& sessionManager,
                             ConnectionId connection, ConnectionTimeoutTracker& timeoutTracker,
                             std::chrono::steady_clock::time_point now,
                             const std::optional<std::string>& bridgeInstanceId,
                             const ActivePlayContext& activePlayContext, const std::string& bridgeVersion) {
    auto hello = protocol::DecodeHelloPayload(helloEnvelope.payload);
    if (!hello.has_value()) {
        return Fail(helloEnvelope, bridgeInstanceId, "malformed_message", "Malformed hello payload", false);
    }

    if (tokenThrottle.IsBlocked(now)) {
        return Fail(helloEnvelope, bridgeInstanceId, "rate_limited", "Too many failed token attempts", true);
    }

    // A structurally invalid presented token (not valid hex) can never
    // match the stored token; treat it as an immediate failed attempt
    // without a TryReserve call, matching this codebase's existing
    // accepted precedent for Phase 1's timing-side-channel posture (see
    // token_store.cpp's "ponytail:" comment on TryReserve).
    auto presentedBytes = security::DecodeHex(hello->authToken);
    auto tokenReservation = presentedBytes.has_value()
                                ? tokenStore.TryReserve(*presentedBytes)
                                : std::optional<security::TokenStore::Reservation>{};
    if (!tokenReservation.has_value()) {
        tokenThrottle.RecordFailure(now);
        return Fail(helloEnvelope, bridgeInstanceId, "unauthenticated", "Invalid or expired one-time token", false);
    }

    auto sessionId = security::GenerateOpaqueId();
    auto responseMessageId = security::GenerateOpaqueId();
    if (!sessionId.has_value() || !responseMessageId.has_value()) {
        return Fail(helloEnvelope, bridgeInstanceId, "internal_error", "Unable to establish a secure connection",
                    false);
    }

    auto context = activePlayContext.AcquireCurrent();
    protocol::Envelope response{
        .messageType = std::string(protocol::message_type::kHelloAck),
        .messageId = *responseMessageId,
        .sessionId = sessionId,
        .correlationId = helloEnvelope.messageId,
        .payload = protocol::EncodeHelloAckPayload(protocol::HelloAckPayload{
            .bridgeVersion = bridgeVersion,
            .clientIdentityKind = "unpaired",
        }),
        // bridgeInstanceId is this bridge's own identity; playContextId
        // reflects whatever play context is already active at connect time
        // (e.g. a reconnect mid-game), which may be none. clientId echoes
        // the identity the client just established in this same hello.
        .bridgeInstanceId = bridgeInstanceId,
        .playContextId = context ? std::optional<std::string>(context->id) : std::nullopt,
        .clientId = hello->clientId,
    };

    auto sessionLease = sessionManager.TryCreateSession(connection, *sessionId);
    if (!sessionLease.has_value()) {
        return Fail(helloEnvelope, bridgeInstanceId, "unauthorized", "Another client is already connected", true);
    }

    tokenReservation->Commit();
    timeoutTracker.MarkAuthenticated(now);

    return HandshakeResult{
        .response = std::move(response),
        .sessionLease = std::move(*sessionLease),
        .closeConnection = false,
    };
}

}  // namespace dovahlink::application
