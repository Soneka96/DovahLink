#include "application/handshake_handler.hpp"

#include "protocol/messages.hpp"
#include "security/csprng.hpp"
#include "security/hex.hpp"

#include <algorithm>
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
// - Checks run cheapest/most-structural first: malformed shape, then
//   protocol version, then the token (which is scarce and one-time, so it
//   should not be spent validating a connection that was going to be
//   rejected anyway for a reason unrelated to authentication).
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
/// @param code Canonical protocol error code.
/// @param message Safe diagnostic message.
/// @param retryable Whether a fresh connection may retry.
HandshakeResult Fail(const protocol::Envelope& helloEnvelope, std::string code, std::string message,
                      bool retryable) {
    return HandshakeResult{
        .response = protocol::BuildErrorEnvelope(helloEnvelope.messageId, /*protocolVersion=*/0,
                                                  /*sessionId=*/std::nullopt, std::move(code),
                                                  std::move(message), retryable),
        .sessionLease = std::nullopt,
        .closeConnection = true,
    };
}

}

HandshakeResult HandleHello(const protocol::Envelope& helloEnvelope, security::TokenStore& tokenStore,
                             security::FailedTokenThrottle& tokenThrottle, SessionManager& sessionManager,
                             ConnectionId connection, ConnectionTimeoutTracker& timeoutTracker,
                             std::chrono::steady_clock::time_point now) {
    if (helloEnvelope.protocolVersion != 0) {
        return Fail(helloEnvelope, "malformed_message", "hello must use protocolVersion 0", false);
    }

    auto hello = protocol::DecodeHelloPayload(helloEnvelope.payload);
    if (!hello.has_value()) {
        return Fail(helloEnvelope, "malformed_message", "Malformed hello payload", false);
    }

    // Highest version both sides support: kSupportedProtocolVersions is
    // small and already ascending, so scanning it from the top and stopping
    // at the first mutual match is simpler than building a full
    // intersection.
    std::optional<std::int64_t> negotiatedVersion;
    for (auto it = kSupportedProtocolVersions.rbegin(); it != kSupportedProtocolVersions.rend(); ++it) {
        if (std::ranges::find(hello->supportedProtocolVersions, *it) != hello->supportedProtocolVersions.end()) {
            negotiatedVersion = *it;
            break;
        }
    }
    if (!negotiatedVersion.has_value()) {
        return Fail(helloEnvelope, "unsupported_version", "No mutually supported protocol version", false);
    }

    // clientId's payload-shape is already validated by DecodeHelloPayload;
    // whether it is required is an application-layer rule that depends on
    // the just-negotiated version, per messages.hpp's "codecs validate
    // shape, application validates rules" split.
    if (*negotiatedVersion >= 2 && !hello->clientId.has_value()) {
        return Fail(helloEnvelope, "malformed_message", "clientId is required once protocol version 2 is negotiated",
                     false);
    }

    if (tokenThrottle.IsBlocked(now)) {
        return Fail(helloEnvelope, "rate_limited", "Too many failed token attempts", true);
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
        return Fail(helloEnvelope, "unauthenticated", "Invalid or expired one-time token", false);
    }

    auto sessionId = security::GenerateOpaqueId();
    auto responseMessageId = security::GenerateOpaqueId();
    if (!sessionId.has_value() || !responseMessageId.has_value()) {
        return Fail(helloEnvelope, "internal_error", "Unable to establish a secure connection", false);
    }

    bool isV2 = *negotiatedVersion >= 2;
    protocol::Envelope response{
        // hello_ack keeps protocolVersion 0 for v1, matching v1's unchanged
        // "no version selected yet" convention; a v2 selection uses the
        // negotiated version itself so EncodeEnvelope's own >= 2 gate emits
        // the envelope identity fields below (protocol/schema/README.md's v2
        // section).
        .protocolVersion = isV2 ? *negotiatedVersion : 0,
        .messageType = std::string(protocol::message_type::kHelloAck),
        .messageId = *responseMessageId,
        .sessionId = sessionId,
        .correlationId = helloEnvelope.messageId,
        .payload = protocol::EncodeHelloAckPayload(protocol::HelloAckPayload{
            .selectedProtocolVersion = *negotiatedVersion,
            .clientIdentityKind = isV2 ? std::optional<std::string>("unpaired") : std::nullopt,
        }),
        // bridgeInstanceId and playContextId are populated once bridge-
        // instance and play-context identity are threaded into this layer
        // (later steps); clientId echoes the identity the client just
        // established in this same hello.
        .clientId = isV2 ? hello->clientId : std::nullopt,
    };

    auto sessionLease = sessionManager.TryCreateSession(connection, *sessionId);
    if (!sessionLease.has_value()) {
        return Fail(helloEnvelope, "unauthorized", "Another client is already connected", true);
    }

    tokenReservation->Commit();
    timeoutTracker.MarkAuthenticated(now);

    return HandshakeResult{
        .response = std::move(response),
        .sessionLease = std::move(*sessionLease),
        .closeConnection = false,
        .negotiatedProtocolVersion = *negotiatedVersion,
    };
}

}  // namespace dovahlink::application
