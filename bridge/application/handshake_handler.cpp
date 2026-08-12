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
//   branching" (protocol/schema/README.md). security::TokenStore::TryConsume
//   itself cannot distinguish invalid/expired/reused (a single bool), so a
//   single generic message covers all three without losing any
//   client-observable information.
// - A session slot already occupied (the one-connected-client limit) maps
//   to `unauthorized`: the presented token was valid, but this connection
//   is not currently permitted to hold a session. No canonical error code
//   names this exact case; `unauthorized` is the closest semantic fit among
//   protocol/schema/README.md's registered codes. Note this does spend the
//   one-time token on a doomed attempt -- unavoidable given
//   TokenStore::TryConsume's required atomic compare-and-consume (TASK.md),
//   which rules out a non-consuming "peek" that could check session
//   capacity first. Both the session ID and this response's messageId are
//   generated together, before TryCreateSession runs, so a CSPRNG failure
//   at that point never leaves a session created that the client can never
//   learn the ID of.
// - Handshake-timeout closure (checking ConnectionTimeoutTracker::IsTimedOut
//   independent of a message actually arriving) is not this function's job;
//   this function only runs once a hello message has already been read.
//   That belongs to whatever owns the read loop, added in a later step.

namespace dovahlink::application {

namespace {

HandshakeResult Fail(const protocol::Envelope& helloEnvelope, std::string code, std::string message,
                      bool retryable) {
    return HandshakeResult{
        .response = protocol::BuildErrorEnvelope(helloEnvelope.messageId, /*protocolVersion=*/0,
                                                  /*sessionId=*/std::nullopt, std::move(code),
                                                  std::move(message), retryable),
        .closeConnection = true,
    };
}

}  // namespace

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

    bool versionSupported = std::ranges::find(hello->supportedProtocolVersions, kSupportedProtocolVersion) !=
                             hello->supportedProtocolVersions.end();
    if (!versionSupported) {
        return Fail(helloEnvelope, "unsupported_version", "No mutually supported protocol version", false);
    }

    // A structurally invalid presented token (not valid hex) can never
    // match the stored token; treat it as an immediate failed attempt
    // without a TryConsume call, matching this codebase's existing
    // accepted precedent for Phase 1's timing-side-channel posture (see
    // token_store.cpp's "ponytail:" comment on TryConsume).
    auto presentedBytes = security::DecodeHex(hello->authToken);
    bool consumed = presentedBytes.has_value() && tokenStore.TryConsume(*presentedBytes);
    if (!consumed) {
        bool limitExceeded = tokenThrottle.RecordFailureAndCheckLimit(now);
        if (limitExceeded) {
            return Fail(helloEnvelope, "rate_limited", "Too many failed token attempts", true);
        }
        return Fail(helloEnvelope, "unauthenticated", "Invalid or expired one-time token", false);
    }

    auto sessionId = security::GenerateOpaqueId();
    auto responseMessageId = security::GenerateOpaqueId();
    if (!sessionId.has_value() || !responseMessageId.has_value()) {
        return Fail(helloEnvelope, "internal_error", "Unable to establish a secure connection", false);
    }

    if (!sessionManager.TryCreateSession(connection, *sessionId)) {
        return Fail(helloEnvelope, "unauthorized", "Another client is already connected", true);
    }

    timeoutTracker.MarkAuthenticated(now);

    return HandshakeResult{
        .response =
            protocol::Envelope{
                .protocolVersion = 0,
                .messageType = std::string(protocol::message_type::kHelloAck),
                .messageId = *responseMessageId,
                .sessionId = std::move(sessionId),
                .correlationId = helloEnvelope.messageId,
                .payload = protocol::EncodeHelloAckPayload(
                    protocol::HelloAckPayload{.selectedProtocolVersion = kSupportedProtocolVersion}),
            },
        .closeConnection = false,
    };
}

}  // namespace dovahlink::application
