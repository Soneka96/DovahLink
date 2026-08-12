#include "application/message_dispatcher.hpp"

#include "application/handshake_handler.hpp"
#include "protocol/bounded_json.hpp"
#include "protocol/messages.hpp"

#include <boost/json/object.hpp>

#include <algorithm>
#include <array>
#include <cassert>
#include <string_view>
#include <utility>

// Design notes on decisions not spelled out verbatim in
// protocol/schema/README.md or ai/context/protocol/security.md:
//
// - A rate-limited message counts as a protocol violation, in addition to
//   its own dedicated rate_limited response. A client that keeps sending
//   too fast after being told to slow down is not meaningfully different
//   from any other client that keeps sending invalid input -- "do not
//   retry invalid input indefinitely" (security.md) is applied here too,
//   rather than letting an over-fast client hold a connection open forever
//   just because it never sends anything structurally wrong.
// - The session message cap (10,000) closes immediately with no response
//   at all, per security.md's literal wording ("the bridge closes the
//   session before this bound is exceeded"): there is nothing left to
//   correlate a response against once the session is already gone, and no
//   canonical error code names this case. Like an oversized frame, this is
//   its own independent hard limit, not routed through the shared Reject()
//   helper and not counted as a violation.
// - Everything that is not in the messageType allowlist is rejected as
//   malformed_message: there is no canonical code for "message type not
//   valid in this context", and receiving hello, or any bridge-to-client-
//   only type, on an already-authenticated connection is exactly that.
// - The timeout check runs before anything else, including parsing: a
//   message that only arrived because the client trickled bytes slowly
//   enough to keep each individual OS-level socket read
//   (WebSocketSession::SetReadTimeout) from firing on its own must still be
//   rejected once the cumulative idle/handshake window has elapsed. Closed
//   with no response, the same category as frame_too_large and the session
//   message cap: there is nothing left to correlate a response against once
//   the connection is already past its allowed window.

namespace dovahlink::application {

namespace {

constexpr std::array<std::string_view, 4> kAllowedMessageTypes = {
    protocol::message_type::kPing,
    protocol::message_type::kCapabilities,
    protocol::message_type::kSubscribe,
    protocol::message_type::kSnapshotRequest,
};

bool IsAllowedMessageType(std::string_view messageType) {
    return std::ranges::find(kAllowedMessageTypes, messageType) != kAllowedMessageTypes.end();
}

DispatchResult Reject(std::optional<std::string> correlationId, const std::string& sessionId, std::string code,
                       std::string message, bool retryable, security::ViolationTracker& violations,
                       std::chrono::steady_clock::time_point steadyNow) {
    bool limitReached = violations.RecordViolationAndCheckLimit(steadyNow);
    return DispatchResult{
        .responses = {protocol::BuildErrorEnvelope(std::move(correlationId), kSupportedProtocolVersion, sessionId,
                                                     std::move(code), std::move(message), retryable)},
        .closeConnection = limitReached,
    };
}

// Wraps a handler's response(s): if the first (or only) response turned out
// to be an error envelope, it counts as a protocol violation, matching how
// every other rejection path in this function is tallied.
DispatchResult FromHandlerResponse(protocol::Envelope firstResponse, security::ViolationTracker& violations,
                                    std::chrono::steady_clock::time_point steadyNow) {
    bool isError = firstResponse.messageType == protocol::message_type::kError;
    bool closeConnection = isError && violations.RecordViolationAndCheckLimit(steadyNow);
    DispatchResult result{.closeConnection = closeConnection};
    result.responses.push_back(std::move(firstResponse));
    return result;
}

protocol::Envelope BuildPong(const protocol::Envelope& pingEnvelope, const std::string& sessionId) {
    auto envelope = protocol::BuildEnvelope(kSupportedProtocolVersion, std::string(protocol::message_type::kPong),
                                             sessionId, pingEnvelope.messageId, boost::json::object{});
    if (envelope.has_value()) {
        return std::move(*envelope);
    }
    // Same unreachable-in-practice CSPRNG fallback every envelope-building
    // path in this codebase shares (see messages.hpp's BuildErrorEnvelope);
    // ping/pong carries no application payload to preserve, so an
    // internal_error is the honest answer.
    return protocol::BuildErrorEnvelope(pingEnvelope.messageId, kSupportedProtocolVersion, sessionId,
                                         "internal_error", "Unable to build response", false);
}

}  // namespace

DispatchResult ProcessInboundMessage(const std::string& rawMessage, const std::string& sessionId,
                                      ConnectionId connection, SessionManager& sessionManager,
                                      ReplayGuard& replayGuard, security::ViolationTracker& violations,
                                      security::InboundMessageRateLimiter& rateLimiter,
                                      ConnectionTimeoutTracker& timeoutTracker,
                                      const CharacterStateProvider& stateProvider, RevisionTracker& revisions,
                                      std::chrono::steady_clock::time_point steadyNow,
                                      std::chrono::system_clock::time_point wallNow) {
    if (timeoutTracker.IsTimedOut(steadyNow)) {
        return DispatchResult{.closeConnection = true};
    }

    auto parsed = protocol::ParseBoundedJson(rawMessage);
    if (!parsed.has_value()) {
        if (parsed.error() == protocol::BoundedJsonError::kFrameTooLarge) {
            return DispatchResult{.closeConnection = true};
        }
        return Reject(/*correlationId=*/std::nullopt, sessionId, "malformed_message", "Malformed message", false,
                       violations, steadyNow);
    }

    auto envelope = protocol::DecodeEnvelope(*parsed);
    if (!envelope.has_value()) {
        return Reject(/*correlationId=*/std::nullopt, sessionId, "malformed_message", "Malformed message", false,
                       violations, steadyNow);
    }

    if (!IsAllowedMessageType(envelope->messageType)) {
        return Reject(envelope->messageId, sessionId, "malformed_message",
                       "Unexpected message type: " + envelope->messageType, false, violations, steadyNow);
    }

    // Every allowed messageType requires a non-empty string sessionId at
    // decode time (protocol/envelope.cpp); only hello/error (neither
    // allowed here, see kAllowedMessageTypes above) permit anything else.
    // Asserted rather than left as a comment-only invariant: a future
    // change to kAllowedMessageTypes or to the check ordering above would
    // otherwise reintroduce a silent nullopt dereference here.
    assert(envelope->sessionId.has_value());
    if (!sessionManager.IsValidForConnection(*envelope->sessionId, connection)) {
        return Reject(envelope->messageId, sessionId, "stale_session", "Session is not valid on this connection",
                       false, violations, steadyNow);
    }

    switch (replayGuard.RecordMessage(envelope->messageId)) {
        case MessageIdCheckResult::kSessionCapReached:
            return DispatchResult{.closeConnection = true};
        case MessageIdCheckResult::kReplayed:
            return Reject(envelope->messageId, sessionId, "replayed_message", "Duplicate messageId", false,
                           violations, steadyNow);
        case MessageIdCheckResult::kAccepted:
            break;
    }

    if (rateLimiter.RecordMessageAndCheckLimit(steadyNow)) {
        return Reject(envelope->messageId, sessionId, "rate_limited",
                       "Inbound message rate exceeded 100 messages per second", true, violations, steadyNow);
    }

    timeoutTracker.RecordActivity(steadyNow);

    if (envelope->messageType == protocol::message_type::kPing) {
        return DispatchResult{.responses = {BuildPong(*envelope, sessionId)}};
    }

    if (envelope->messageType == protocol::message_type::kCapabilities) {
        auto error = HandleClientCapabilities(*envelope, sessionId);
        if (!error.has_value()) {
            return DispatchResult{};
        }
        return FromHandlerResponse(std::move(*error), violations, steadyNow);
    }

    if (envelope->messageType == protocol::message_type::kSubscribe) {
        auto result = HandleSubscribe(*envelope, sessionId, stateProvider, revisions, wallNow);
        DispatchResult dispatch = FromHandlerResponse(std::move(result.subscriptionAck), violations, steadyNow);
        for (auto& snapshot : result.snapshots) {
            dispatch.responses.push_back(std::move(snapshot));
        }
        return dispatch;
    }

    // Only kSnapshotRequest remains, per kAllowedMessageTypes.
    auto response = HandleSnapshotRequest(*envelope, sessionId, stateProvider, revisions, wallNow);
    return FromHandlerResponse(std::move(response), violations, steadyNow);
}

}  // namespace dovahlink::application
