#include "application/message_dispatcher.hpp"

#include "application/handshake_handler.hpp"
#include "protocol/bounded_json.hpp"
#include "protocol/messages.hpp"
#include "security/limits.hpp"

#include <boost/json/object.hpp>

#include <algorithm>
#include <array>
#include <cassert>
#include <iterator>
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
// - The session message cap (10,000) counts every completed message before
//   decoding and closes immediately with no response when exceeded, per
//   security.md. There is nothing left to correlate a response against once
//   the session is already gone, and no canonical error code names this case.
//   Like an oversized frame, this is an independent hard limit, not routed
//   through the shared Reject() helper or counted as a violation.
// - Everything that is not in the messageType allowlist is rejected as
//   malformed_message: there is no canonical code for "message type not
//   valid in this context", and receiving hello, or any bridge-to-client-
//   only type, on an already-authenticated connection is exactly that.
// - After total-message budget accounting, the timeout check runs before
//   parsing or other application validation: a
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

/// Reports whether an inbound message type is allowed after authentication.
/// @param messageType Canonical message-type identifier.
/// @return `true` when a handler exists for the type.
bool IsAllowedMessageType(std::string_view messageType) {
    return std::ranges::find(kAllowedMessageTypes, messageType) != kAllowedMessageTypes.end();
}

/// Builds a protocol error and records the rejection as a violation.
/// @param correlationId Message ID being rejected, when available.
/// @param sessionId Authenticated session identifier.
/// @param protocolVersion Connection's negotiated protocol version.
/// @param code Canonical protocol error code.
/// @param message Safe diagnostic message.
/// @param retryable Whether the client may retry.
/// @param violations Violation tracker to update.
/// @param steadyNow Current monotonic time.
DispatchResult Reject(std::optional<std::string> correlationId, const std::string& sessionId,
                       std::int64_t protocolVersion, std::string code, std::string message, bool retryable,
                       security::ViolationTracker& violations, std::chrono::steady_clock::time_point steadyNow) {
    bool limitReached = violations.RecordViolationAndCheckLimit(steadyNow);
    return DispatchResult{
        .responses = {protocol::BuildErrorEnvelope(std::move(correlationId), protocolVersion, sessionId,
                                                     std::move(code), std::move(message), retryable)},
        .closeConnection = limitReached,
    };
}

// Wraps a handler's response(s): if the first (or only) response turned out
// to be an error envelope, it counts as a protocol violation, matching how
/// Wraps a handler response and counts an error response as a violation.
DispatchResult FromHandlerResponse(protocol::Envelope firstResponse, security::ViolationTracker& violations,
                                    std::chrono::steady_clock::time_point steadyNow) {
    bool isError = firstResponse.messageType == protocol::message_type::kError;
    bool closeConnection = isError && violations.RecordViolationAndCheckLimit(steadyNow);
    DispatchResult result{.closeConnection = closeConnection};
    result.responses.push_back(std::move(firstResponse));
    return result;
}

/// Supplies the existing unavailable character-state shape (every field
/// absent) for the v2 path when no play context is currently active. Reuses
/// HandleSubscribe/HandleSnapshotRequest unchanged rather than inventing a
/// new wire signal, per protocol/schema/README.md's v2 section.
class NullCharacterStateProvider : public CharacterStateProvider {
public:
    /// @copydoc CharacterStateProvider::CurrentCharacterSnapshot
    [[nodiscard]] CharacterSnapshot CurrentCharacterSnapshot() const override { return CharacterSnapshot{}; }
};

/// Builds a pong response correlated with an incoming ping.
/// @param pingEnvelope Envelope of the ping being acknowledged.
/// @param sessionId Authenticated session identifier.
/// @param protocolVersion Connection's negotiated protocol version.
protocol::Envelope BuildPong(const protocol::Envelope& pingEnvelope, const std::string& sessionId,
                             std::int64_t protocolVersion) {
    auto envelope = protocol::BuildEnvelope(protocolVersion, std::string(protocol::message_type::kPong),
                                             sessionId, pingEnvelope.messageId, boost::json::object{});
    if (envelope.has_value()) {
        return std::move(*envelope);
    }
    // Same unreachable-in-practice CSPRNG fallback every envelope-building
    // path in this codebase shares (see messages.hpp's BuildErrorEnvelope);
    // ping/pong carries no application payload to preserve, so an
    // internal_error is the honest answer.
    return protocol::BuildErrorEnvelope(pingEnvelope.messageId, protocolVersion, sessionId,
                                         "internal_error", "Unable to build response", false);
}

/// Builds an unsolicited fresh snapshot reflecting whichever character-state
/// source is currently effective, for protocol v2's context-change resync
/// mechanism. `correlationId` is null: this message is unsolicited, matching
/// `capabilities`/`state_event`'s existing convention (protocol/schema/README.md).
/// @return The snapshot envelope, or no value if it could not be built (the
///     same unreachable-in-practice CSPRNG failure every envelope-building
///     path in this codebase shares).
std::optional<protocol::Envelope> BuildResyncSnapshot(const std::string& sessionId, std::int64_t protocolVersion,
                                                       const CharacterStateProvider& provider,
                                                       RevisionTracker& revisions,
                                                       std::chrono::system_clock::time_point now) {
    const std::string stateArea(protocol::state_area::kCharacter);
    CharacterSnapshot snapshot = provider.CurrentCharacterSnapshot();
    std::int64_t revision = revisions.NextSnapshotRevision(stateArea);
    auto envelope = BuildCharacterSnapshotEnvelope(sessionId, protocolVersion, /*correlationId=*/std::nullopt,
                                                    snapshot, revision, now);
    if (envelope.has_value()) {
        revisions.StartSnapshot(stateArea);
    }
    return envelope;
}

}

DispatchResult ProcessInboundMessage(const std::string& rawMessage, std::size_t& receivedMessageCount,
                                      const std::string& sessionId, std::int64_t protocolVersion,
                                      ConnectionId connection, SessionManager& sessionManager,
                                      ReplayGuard& replayGuard, security::ViolationTracker& violations,
                                      security::InboundMessageRateLimiter& rateLimiter,
                                      ConnectionTimeoutTracker& timeoutTracker,
                                      const CharacterStateProvider& stateProvider, RevisionTracker& revisions,
                                      const ActivePlayContext& activePlayContext,
                                      SubscriptionState& subscriptionState,
                                      std::chrono::steady_clock::time_point steadyNow,
                                      std::chrono::system_clock::time_point wallNow) {
    ++receivedMessageCount;
    if (receivedMessageCount > security::kMaxMessagesPerSession) {
        return DispatchResult{.closeConnection = true};
    }

    if (timeoutTracker.IsTimedOut(steadyNow)) {
        return DispatchResult{.closeConnection = true};
    }

    auto parsed = protocol::ParseBoundedJson(rawMessage);
    if (!parsed.has_value()) {
        if (parsed.error() == protocol::BoundedJsonError::kFrameTooLarge) {
            return DispatchResult{.closeConnection = true};
        }
        return Reject(/*correlationId=*/std::nullopt, sessionId, protocolVersion, "malformed_message",
                       "Malformed message", false, violations, steadyNow);
    }

    auto envelope = protocol::DecodeEnvelope(*parsed);
    if (!envelope.has_value()) {
        return Reject(/*correlationId=*/std::nullopt, sessionId, protocolVersion, "malformed_message",
                       "Malformed message", false, violations, steadyNow);
    }

    if (!IsAllowedMessageType(envelope->messageType)) {
        return Reject(envelope->messageId, sessionId, protocolVersion, "malformed_message",
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
        return Reject(envelope->messageId, sessionId, protocolVersion, "stale_session",
                       "Session is not valid on this connection", false, violations, steadyNow);
    }

    if (replayGuard.RecordMessage(envelope->messageId) == MessageIdCheckResult::kReplayed) {
        return Reject(envelope->messageId, sessionId, protocolVersion, "replayed_message", "Duplicate messageId",
                       false, violations, steadyNow);
    }

    if (rateLimiter.RecordMessageAndCheckLimit(steadyNow)) {
        return Reject(envelope->messageId, sessionId, protocolVersion, "rate_limited",
                       "Inbound message rate exceeded 100 messages per second", true, violations, steadyNow);
    }

    timeoutTracker.RecordActivity(steadyNow);

    // Routes to v1's session-scoped state, this connection's acquired v2
    // play context, or (v2 with no active context) throwaway state
    // producing the existing unavailable shape -- resolved once, ahead of
    // every message type, so the resync check below and kSubscribe/
    // kSnapshotRequest further down share identical routing, per
    // protocol/schema/README.md's v2 section ("NoContext ... reuses the
    // existing unavailable state-area shape"). The throwaway RevisionTracker
    // is freshly constructed on every call, so a NoContext response always
    // reports revision 1: there is no authoritative state outside a play
    // context for a revision to persist against, and the client already
    // keys staleness off `playContextId: null` rather than this number
    // (ARCHITECTURE.md's "Authoritative state and revisions").
    std::shared_ptr<PlayContext> context =
        protocolVersion >= 2 ? activePlayContext.AcquireCurrent() : nullptr;
    NullCharacterStateProvider noContextProvider;
    RevisionTracker noContextRevisions;
    const CharacterStateProvider& effectiveProvider =
        protocolVersion < 2 ? stateProvider
        : context           ? static_cast<const CharacterStateProvider&>(context->characterState)
                             : static_cast<const CharacterStateProvider&>(noContextProvider);
    RevisionTracker& effectiveRevisions =
        protocolVersion < 2 ? revisions : (context ? context->revisions : noContextRevisions);

    // Protocol v2's "existing connected clients learn context transitions"
    // mechanism (protocol/schema/README.md): on every message, a subscribed
    // v2 connection whose remembered context differs from the current one
    // gets an unsolicited fresh snapshot prepended to whatever this message
    // would otherwise produce, bounded by the connection's own keepalive
    // cadence rather than pushed independently (Phase 4 scope).
    std::vector<protocol::Envelope> prepended;
    if (protocolVersion >= 2) {
        std::optional<std::string> currentContextId = context ? std::optional<std::string>(context->id) : std::nullopt;
        bool contextChanged = currentContextId != subscriptionState.lastKnownPlayContextId;
        if (contextChanged && !subscriptionState.subscribedStateAreas.empty()) {
            auto resyncSnapshot =
                BuildResyncSnapshot(sessionId, protocolVersion, effectiveProvider, effectiveRevisions, wallNow);
            if (resyncSnapshot.has_value()) {
                prepended.push_back(std::move(*resyncSnapshot));
                subscriptionState.lastKnownPlayContextId = currentContextId;
            }
            // Building failed (unreachable-in-practice CSPRNG failure):
            // lastKnownPlayContextId stays stale so the next message retries.
        } else {
            subscriptionState.lastKnownPlayContextId = currentContextId;
        }
    }

    DispatchResult result;
    if (envelope->messageType == protocol::message_type::kPing) {
        result = DispatchResult{.responses = {BuildPong(*envelope, sessionId, protocolVersion)}};
    } else if (envelope->messageType == protocol::message_type::kCapabilities) {
        auto error = HandleClientCapabilities(*envelope, sessionId, protocolVersion);
        result = error.has_value() ? FromHandlerResponse(std::move(*error), violations, steadyNow)
                                    : DispatchResult{};
    } else if (envelope->messageType == protocol::message_type::kSubscribe) {
        auto subscribeResult =
            HandleSubscribe(*envelope, sessionId, protocolVersion, effectiveProvider, effectiveRevisions, wallNow);
        // Only a genuinely processed request (never a failure -- malformed
        // payload, or an internal error building the response) replaces the
        // remembered subscription: a rejected request carries no client
        // intent to unsubscribe, so a prior subscription must survive it.
        if (protocolVersion >= 2 && subscribeResult.subscriptionAck.messageType == protocol::message_type::kSubscriptionAck) {
            subscriptionState.subscribedStateAreas = subscribeResult.acceptedStateAreas;
        }
        result = FromHandlerResponse(std::move(subscribeResult.subscriptionAck), violations, steadyNow);
        for (auto& snapshot : subscribeResult.snapshots) {
            result.responses.push_back(std::move(snapshot));
        }
    } else {
        // Only kSnapshotRequest remains, per kAllowedMessageTypes.
        auto response = HandleSnapshotRequest(*envelope, sessionId, protocolVersion, effectiveProvider,
                                              effectiveRevisions, wallNow);
        result = FromHandlerResponse(std::move(response), violations, steadyNow);
    }

    result.responses.insert(result.responses.begin(), std::make_move_iterator(prepended.begin()),
                            std::make_move_iterator(prepended.end()));
    return result;
}

}  // namespace dovahlink::application
