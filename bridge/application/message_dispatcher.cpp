#include "application/message_dispatcher.hpp"

#include "application/handshake_handler.hpp"
#include "application/pairing_handler.hpp"
#include "application/revision_tracker.hpp"
#include "protocol/bounded_json.hpp"
#include "protocol/messages.hpp"
#include "security/limits.hpp"

#include <boost/json/object.hpp>
#include <boost/json/serialize.hpp>

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

// A Restricted session is one that authenticated via hello's "unpaired" method (or has not yet
// finished pairing); it may only exchange ping/capabilities and the pairing handshake itself.
// Pairing messages are deliberately absent from the Full allowlist below: once a session is
// already trusted, re-running pairing is pointless, and excluding it keeps a stray retry from a
// confused already-paired client from reaching pairing_handler at all.
constexpr std::array<std::string_view, 7> kRestrictedAllowedMessageTypes = {
    protocol::message_type::kPing,
    protocol::message_type::kCapabilities,
    protocol::message_type::kPairingRequest,
    protocol::message_type::kPairingConfirm,
    protocol::message_type::kPairingAck,
    protocol::message_type::kPairingRenotify,
    protocol::message_type::kPairingCancel,
};

constexpr std::array<std::string_view, 4> kFullAllowedMessageTypes = {
    protocol::message_type::kPing,
    protocol::message_type::kCapabilities,
    protocol::message_type::kSubscribe,
    protocol::message_type::kSnapshotRequest,
};

/// Reports whether an inbound message type is allowed after authentication, given the
/// authenticated session's trust tier.
/// @param messageType Canonical message-type identifier.
/// @param trustTier The connection's current session trust tier.
/// @return `true` when a handler exists for the type at this trust tier.
bool IsAllowedMessageType(std::string_view messageType, SessionTrustTier trustTier) {
    if (trustTier == SessionTrustTier::kRestricted) {
        return std::ranges::find(kRestrictedAllowedMessageTypes, messageType) !=
               kRestrictedAllowedMessageTypes.end();
    }
    return std::ranges::find(kFullAllowedMessageTypes, messageType) != kFullAllowedMessageTypes.end();
}

/// Builds a protocol error and records the rejection as a violation.
/// @param correlationId Message ID being rejected, when available.
/// @param sessionId Authenticated session identifier.
/// @param code Canonical protocol error code.
/// @param message Safe diagnostic message.
/// @param retryable Whether the client may retry.
/// @param violations Violation tracker to update.
/// @param steadyNow Current monotonic time.
/// @param bridgeInstanceId This bridge's own identity, stamped onto the response when known.
/// @param currentContextId The currently active play context, stamped onto the response.
DispatchResult Reject(std::optional<std::string> correlationId, const std::string& sessionId, std::string code,
                       std::string message, bool retryable, security::ViolationTracker& violations,
                       std::chrono::steady_clock::time_point steadyNow,
                       const std::optional<std::string>& bridgeInstanceId,
                       const std::optional<std::string>& currentContextId) {
    bool limitReached = violations.RecordViolationAndCheckLimit(steadyNow);
    protocol::Envelope response = protocol::BuildErrorEnvelope(std::move(correlationId), sessionId, std::move(code),
                                                                std::move(message), retryable);
    response.bridgeInstanceId = bridgeInstanceId;
    response.playContextId = currentContextId;
    return DispatchResult{
        .responses = {std::move(response)},
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
/// absent) when no play context is currently active. Reuses
/// HandleSubscribe/HandleSnapshotRequest unchanged rather than inventing a
/// new wire signal, per protocol/schema/README.md.
class NullCharacterStateProvider : public CharacterStateProvider {
public:
    /// @copydoc CharacterStateProvider::CurrentCharacterSnapshot
    [[nodiscard]] CharacterSnapshot CurrentCharacterSnapshot() const override { return CharacterSnapshot{}; }
};

/// Builds a pong response correlated with an incoming ping.
/// @param pingEnvelope Envelope of the ping being acknowledged.
/// @param sessionId Authenticated session identifier.
protocol::Envelope BuildPong(const protocol::Envelope& pingEnvelope, const std::string& sessionId) {
    auto envelope = protocol::BuildEnvelope(std::string(protocol::message_type::kPong), sessionId,
                                             pingEnvelope.messageId, boost::json::object{});
    if (envelope.has_value()) {
        return std::move(*envelope);
    }
    // Same unreachable-in-practice CSPRNG fallback every envelope-building
    // path in this codebase shares (see messages.hpp's BuildErrorEnvelope);
    // ping/pong carries no application payload to preserve, so an
    // internal_error is the honest answer.
    return protocol::BuildErrorEnvelope(pingEnvelope.messageId, sessionId, "internal_error",
                                         "Unable to build response", false);
}

/// Builds an unsolicited fresh snapshot reflecting whichever character-state
/// source is currently effective, for the context-change resync mechanism.
/// `correlationId` is null: this message is unsolicited, matching
/// `capabilities`/`state_event`'s existing convention (protocol/schema/README.md).
/// @return The snapshot envelope, or no value if it could not be built (the
///     same unreachable-in-practice CSPRNG failure every envelope-building
///     path in this codebase shares).
std::optional<protocol::Envelope> BuildResyncSnapshot(const std::string& sessionId,
                                                       const CharacterStateProvider& provider,
                                                       RevisionTracker& revisions,
                                                       std::chrono::system_clock::time_point now) {
    const std::string stateArea(protocol::state_area::kCharacter);
    CharacterSnapshot snapshot = provider.CurrentCharacterSnapshot();
    std::optional<std::string> fingerprint =
        std::make_optional(boost::json::serialize(BuildCharacterStateData(snapshot)));
    // CommitSnapshotIfBuilt assigns and commits the revision atomically with building the
    // envelope, closing the race a separate preview (NextSnapshotRevision) followed by a later
    // commit (StartSnapshot) leaves open against another concurrent snapshot for this state area.
    return revisions.CommitSnapshotIfBuilt(stateArea, fingerprint, [&](std::int64_t revision) {
        return BuildCharacterSnapshotEnvelope(sessionId, /*correlationId=*/std::nullopt, snapshot, revision, now);
    });
}

}

DispatchResult ProcessInboundMessage(const std::string& rawMessage, std::size_t& receivedMessageCount,
                                      const std::string& sessionId, ConnectionId connection,
                                      SessionManager& sessionManager, ReplayGuard& replayGuard,
                                      security::ViolationTracker& violations,
                                      security::InboundMessageRateLimiter& rateLimiter,
                                      ConnectionTimeoutTracker& timeoutTracker,
                                      const ActivePlayContext& activePlayContext,
                                      SubscriptionState& subscriptionState,
                                      security::PairingSession& pairingSession, security::TrustStore& trustStore,
                                      PairingNotificationSink& pairingNotificationSink,
                                      const std::optional<std::string>& bridgeInstanceId,
                                      std::chrono::steady_clock::time_point steadyNow,
                                      std::chrono::system_clock::time_point wallNow) {
    ++receivedMessageCount;
    if (receivedMessageCount > security::kMaxMessagesPerSession) {
        return DispatchResult{.closeConnection = true};
    }

    if (timeoutTracker.IsTimedOut(steadyNow)) {
        return DispatchResult{.closeConnection = true};
    }

    // Resolved once, ahead of every rejection and message type, so every
    // response this call can produce -- including an early connection-
    // hygiene rejection below -- carries the same bridge/play-context
    // identity a client compares its cached state against (protocol/schema/
    // README.md: present on every Bridge-originated message once
    // authenticated). No active context: throwaway state producing the
    // existing unavailable shape, freshly constructed on every call, so a
    // NoContext response always reports revision 1 -- there is no
    // authoritative state outside a play context for a revision to persist
    // against, and the client already keys staleness off `playContextId:
    // null` rather than this number (ARCHITECTURE.md's "Authoritative state
    // and revisions").
    std::shared_ptr<PlayContext> context = activePlayContext.AcquireCurrent();
    std::optional<std::string> currentContextId = context ? std::optional<std::string>(context->id) : std::nullopt;

    auto parsed = protocol::ParseBoundedJson(rawMessage);
    if (!parsed.has_value()) {
        if (parsed.error() == protocol::BoundedJsonError::kFrameTooLarge) {
            return DispatchResult{.closeConnection = true};
        }
        return Reject(/*correlationId=*/std::nullopt, sessionId, "malformed_message", "Malformed message", false,
                       violations, steadyNow, bridgeInstanceId, currentContextId);
    }

    auto envelope = protocol::DecodeEnvelope(*parsed);
    if (!envelope.has_value()) {
        return Reject(/*correlationId=*/std::nullopt, sessionId, "malformed_message", "Malformed message", false,
                       violations, steadyNow, bridgeInstanceId, currentContextId);
    }

    // Depends only on `connection`, not on anything in the not-yet-validated envelope -- safe to
    // resolve this early. A connection with no active session at all (already invalidated, or
    // never authenticated) reads as Restricted here, same as a genuinely unpaired one; the
    // IsValidForConnection check below rejects it as stale_session regardless of which allowlist
    // this resolves to.
    SessionTrustTier trustTier =
        sessionManager.IsFullyTrusted(connection) ? SessionTrustTier::kFull : SessionTrustTier::kRestricted;

    if (!IsAllowedMessageType(envelope->messageType, trustTier)) {
        return Reject(envelope->messageId, sessionId, "malformed_message",
                       "Unexpected message type: " + envelope->messageType, false, violations, steadyNow,
                       bridgeInstanceId, currentContextId);
    }

    // Every allowed messageType requires a non-empty string sessionId at
    // decode time (protocol/envelope.cpp); only hello/error (neither
    // allowed here, see kRestrictedAllowedMessageTypes/kFullAllowedMessageTypes above) permit
    // anything else.
    // Asserted rather than left as a comment-only invariant: a future
    // change to kAllowedMessageTypes or to the check ordering above would
    // otherwise reintroduce a silent nullopt dereference here.
    assert(envelope->sessionId.has_value());
    if (!sessionManager.IsValidForConnection(*envelope->sessionId, connection)) {
        return Reject(envelope->messageId, sessionId, "stale_session", "Session is not valid on this connection",
                       false, violations, steadyNow, bridgeInstanceId, currentContextId);
    }

    if (replayGuard.RecordMessage(envelope->messageId) == MessageIdCheckResult::kReplayed) {
        return Reject(envelope->messageId, sessionId, "replayed_message", "Duplicate messageId", false, violations,
                       steadyNow, bridgeInstanceId, currentContextId);
    }

    if (rateLimiter.RecordMessageAndCheckLimit(steadyNow)) {
        return Reject(envelope->messageId, sessionId, "rate_limited",
                       "Inbound message rate exceeded 100 messages per second", true, violations, steadyNow,
                       bridgeInstanceId, currentContextId);
    }

    timeoutTracker.RecordActivity(steadyNow);

    NullCharacterStateProvider noContextProvider;
    RevisionTracker noContextRevisions;
    const CharacterStateProvider& effectiveProvider =
        context ? static_cast<const CharacterStateProvider&>(context->characterState)
                : static_cast<const CharacterStateProvider&>(noContextProvider);
    RevisionTracker& effectiveRevisions = context ? context->revisions : noContextRevisions;

    // The "existing connected clients learn context transitions" mechanism
    // (protocol/schema/README.md): on every message, a subscribed connection
    // whose remembered context differs from the current one gets an
    // unsolicited fresh snapshot prepended to whatever this message would
    // otherwise produce, bounded by the connection's own keepalive cadence
    // rather than pushed independently (Phase 4 scope).
    std::vector<protocol::Envelope> prepended;
    bool contextChanged = currentContextId != subscriptionState.lastKnownPlayContextId;
    if (contextChanged && !subscriptionState.subscribedStateAreas.empty()) {
        auto resyncSnapshot = BuildResyncSnapshot(sessionId, effectiveProvider, effectiveRevisions, wallNow);
        if (resyncSnapshot.has_value()) {
            prepended.push_back(std::move(*resyncSnapshot));
            subscriptionState.lastKnownPlayContextId = currentContextId;
        }
        // Building failed (unreachable-in-practice CSPRNG failure):
        // lastKnownPlayContextId stays stale so the next message retries.
    } else {
        subscriptionState.lastKnownPlayContextId = currentContextId;
    }

    DispatchResult result;
    if (envelope->messageType == protocol::message_type::kPing) {
        result = DispatchResult{.responses = {BuildPong(*envelope, sessionId)}};
    } else if (envelope->messageType == protocol::message_type::kCapabilities) {
        auto error = HandleClientCapabilities(*envelope, sessionId);
        result = error.has_value() ? FromHandlerResponse(std::move(*error), violations, steadyNow)
                                    : DispatchResult{};
    } else if (envelope->messageType == protocol::message_type::kSubscribe) {
        auto subscribeResult = HandleSubscribe(*envelope, sessionId, effectiveProvider, effectiveRevisions, wallNow);
        // Only a genuinely processed request (never a failure -- malformed
        // payload, or an internal error building the response) replaces the
        // remembered subscription: a rejected request carries no client
        // intent to unsubscribe, so a prior subscription must survive it.
        if (subscribeResult.subscriptionAck.messageType == protocol::message_type::kSubscriptionAck) {
            subscriptionState.subscribedStateAreas = subscribeResult.acceptedStateAreas;
        }
        result = FromHandlerResponse(std::move(subscribeResult.subscriptionAck), violations, steadyNow);
        for (auto& snapshot : subscribeResult.snapshots) {
            result.responses.push_back(std::move(snapshot));
        }
    } else if (envelope->messageType == protocol::message_type::kPairingRequest) {
        // Restricted-only (kRestrictedAllowedMessageTypes above): the session passed
        // IsValidForConnection above, so it is this connection's active session and always has a
        // clientId (SessionManager::TryCreateSession never admits one without it).
        auto clientId = sessionManager.ClientIdForConnection(connection);
        assert(clientId.has_value());
        result = FromHandlerResponse(
            HandlePairingRequest(*envelope, sessionId, *clientId, pairingSession, pairingNotificationSink, steadyNow),
            violations, steadyNow);
    } else if (envelope->messageType == protocol::message_type::kPairingConfirm) {
        auto clientId = sessionManager.ClientIdForConnection(connection);
        assert(clientId.has_value());
        result = FromHandlerResponse(
            HandlePairingConfirm(*envelope, sessionId, *clientId, pairingSession, pairingNotificationSink, steadyNow),
            violations, steadyNow);
    } else if (envelope->messageType == protocol::message_type::kPairingAck) {
        auto clientId = sessionManager.ClientIdForConnection(connection);
        assert(clientId.has_value());
        result = FromHandlerResponse(HandlePairingAck(*envelope, sessionId, *clientId, connection, pairingSession,
                                                       trustStore, sessionManager, steadyNow),
                                     violations, steadyNow);
    } else if (envelope->messageType == protocol::message_type::kPairingRenotify) {
        auto clientId = sessionManager.ClientIdForConnection(connection);
        assert(clientId.has_value());
        result = FromHandlerResponse(HandlePairingRenotify(*envelope, sessionId, *clientId, pairingSession,
                                                            pairingNotificationSink, steadyNow),
                                     violations, steadyNow);
    } else if (envelope->messageType == protocol::message_type::kPairingCancel) {
        auto clientId = sessionManager.ClientIdForConnection(connection);
        assert(clientId.has_value());
        result = FromHandlerResponse(HandlePairingCancel(*envelope, sessionId, *clientId, pairingSession, steadyNow),
                                     violations, steadyNow);
    } else {
        // Only kSnapshotRequest remains, per kFullAllowedMessageTypes -- unreachable for a
        // Restricted session, since every Restricted-allowed type is handled explicitly above.
        auto response = HandleSnapshotRequest(*envelope, sessionId, effectiveProvider, effectiveRevisions, wallNow);
        result = FromHandlerResponse(std::move(response), violations, steadyNow);
    }

    result.responses.insert(result.responses.begin(), std::make_move_iterator(prepended.begin()),
                            std::make_move_iterator(prepended.end()));

    // Stamps the connection's bridge and play-context identity onto every
    // response built through normal dispatch. The earlier Reject() calls
    // above stamp the same two fields themselves, at the point each is
    // built, since they return directly rather than flowing through
    // `result`. clientId is never stamped here or in Reject(): once
    // authenticated, the client identity is session-owned state
    // (SessionManager::ClientIdForConnection), not a value repeated on
    // every response.
    for (protocol::Envelope& response : result.responses) {
        response.bridgeInstanceId = bridgeInstanceId;
        response.playContextId = currentContextId;
    }

    return result;
}

}  // namespace dovahlink::application
