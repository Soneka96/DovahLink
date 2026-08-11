#include "application/handshake_handler.hpp"

#include "protocol/messages.hpp"
#include "security/csprng.hpp"
#include "security/hex.hpp"

#include <algorithm>
#include <optional>
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
//   capacity first.
// - Handshake-timeout closure (checking ConnectionTimeoutTracker::IsTimedOut
//   independent of a message actually arriving) is not this function's job;
//   this function only runs once a hello message has already been read.
//   That belongs to whatever owns the read loop, added in a later step.

namespace dovahlink::application {

namespace {

constexpr std::size_t kOpaqueIdBytes = 16;  // 128 bits: ample collision resistance for an opaque ID.

// Generates a fresh cryptographically random hex-encoded ID, used for both
// messageId and sessionId (protocol/schema/README.md requires both to be
// cryptographically random). nullopt only if the underlying CNG call fails
// (security::GenerateRandomBytes), a condition that codebase already treats
// as unreachable in practice and does not unit-test (security/csprng.hpp).
std::optional<std::string> GenerateOpaqueId() {
    auto bytes = security::GenerateRandomBytes(kOpaqueIdBytes);
    if (!bytes.has_value()) {
        return std::nullopt;
    }
    return security::EncodeHex(*bytes);
}

protocol::Envelope BuildErrorEnvelope(const protocol::Envelope& helloEnvelope, std::string messageId,
                                       std::string code, std::string message, bool retryable) {
    return protocol::Envelope{
        .protocolVersion = 0,
        .messageType = std::string(protocol::message_type::kError),
        .messageId = std::move(messageId),
        .sessionId = std::nullopt,  // no session exists yet on any hello-handling path.
        .correlationId = helloEnvelope.messageId,
        .payload = protocol::EncodeErrorPayload(protocol::ErrorPayload{
            .code = std::move(code),
            .message = std::move(message),
            .retryable = retryable,
            .details = std::nullopt,
        }),
    };
}

}  // namespace

HandshakeResult HandleHello(const protocol::Envelope& helloEnvelope, security::TokenStore& tokenStore,
                             security::FailedTokenThrottle& tokenThrottle, SessionManager& sessionManager,
                             ConnectionId connection, ConnectionTimeoutTracker& timeoutTracker,
                             std::chrono::steady_clock::time_point now) {
    auto responseMessageId = GenerateOpaqueId();
    if (!responseMessageId.has_value()) {
        // See GenerateOpaqueId's doc: unreachable in practice. A fixed,
        // non-random ID here is the one place this function cannot honor
        // the "cryptographically random messageId" requirement, because the
        // same broken primitive that failed here would fail identically if
        // retried for a "proper" error response.
        return HandshakeResult{
            .response = BuildErrorEnvelope(helloEnvelope, "csprng-unavailable", "internal_error",
                                            "Unable to establish a secure connection", false),
            .closeConnection = true,
        };
    }

    if (helloEnvelope.protocolVersion != 0) {
        return HandshakeResult{
            .response = BuildErrorEnvelope(helloEnvelope, *responseMessageId, "malformed_message",
                                            "hello must use protocolVersion 0", false),
            .closeConnection = true,
        };
    }

    auto hello = protocol::DecodeHelloPayload(helloEnvelope.payload);
    if (!hello.has_value()) {
        return HandshakeResult{
            .response = BuildErrorEnvelope(helloEnvelope, *responseMessageId, "malformed_message",
                                            "Malformed hello payload", false),
            .closeConnection = true,
        };
    }

    bool versionSupported = std::ranges::find(hello->supportedProtocolVersions, kSupportedProtocolVersion) !=
                             hello->supportedProtocolVersions.end();
    if (!versionSupported) {
        return HandshakeResult{
            .response = BuildErrorEnvelope(helloEnvelope, *responseMessageId, "unsupported_version",
                                            "No mutually supported protocol version", false),
            .closeConnection = true,
        };
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
            return HandshakeResult{
                .response = BuildErrorEnvelope(helloEnvelope, *responseMessageId, "rate_limited",
                                                "Too many failed token attempts", true),
                .closeConnection = true,
            };
        }
        return HandshakeResult{
            .response = BuildErrorEnvelope(helloEnvelope, *responseMessageId, "unauthenticated",
                                            "Invalid or expired one-time token", false),
            .closeConnection = true,
        };
    }

    auto sessionId = GenerateOpaqueId();
    if (!sessionId.has_value()) {
        return HandshakeResult{
            .response = BuildErrorEnvelope(helloEnvelope, *responseMessageId, "internal_error",
                                            "Unable to establish a secure connection", false),
            .closeConnection = true,
        };
    }

    if (!sessionManager.TryCreateSession(connection, *sessionId)) {
        return HandshakeResult{
            .response = BuildErrorEnvelope(helloEnvelope, *responseMessageId, "unauthorized",
                                            "Another client is already connected", true),
            .closeConnection = true,
        };
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
