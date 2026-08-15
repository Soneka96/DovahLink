#pragma once

#include "application/connection_timeout_tracker.hpp"
#include "application/play_context.hpp"
#include "application/replay_guard.hpp"
#include "application/revision_tracker.hpp"
#include "application/session.hpp"
#include "application/subscription_handler.hpp"
#include "protocol/envelope.hpp"
#include "security/throttle.hpp"

#include <chrono>
#include <cstddef>
#include <optional>
#include <string>
#include <vector>

namespace dovahlink::application {

/// Contains responses produced while processing one inbound message.
struct DispatchResult {
    /// Response envelopes to send in order.
    std::vector<protocol::Envelope> responses;

    /// Whether the connection must close after responses are sent.
    bool closeConnection = false;
};

/// Per-connection subscription bookkeeping for protocol v2's "existing
/// connected clients learn context transitions" mechanism
/// (protocol/schema/README.md's v2 section). Unused by the v1 path.
struct SubscriptionState {
    /// State areas this connection is currently subscribed to.
    std::vector<std::string> subscribedStateAreas;

    /// `playContextId` last reported to this connection, or no value before
    /// the first v2 message that could carry one.
    std::optional<std::string> lastKnownPlayContextId;
};

/// Counts, validates, rate-limits, and dispatches one inbound message.
/// Oversized frames and exhausted session limits request immediate closure without a response.
/// @param rawMessage Encoded inbound WebSocket text.
/// @param receivedMessageCount Number of completed messages read in this session; incremented before decoding.
/// @param sessionId Authenticated session identifier.
/// @param protocolVersion Connection's negotiated protocol version, stamped on every outbound envelope.
/// @param connection Transport connection identifier.
/// @param sessionManager Session registry.
/// @param replayGuard Per-session message-ID guard.
/// @param violations Per-connection protocol-violation tracker.
/// @param rateLimiter Per-connection inbound rate limiter.
/// @param timeoutTracker Connection timeout tracker.
/// @param stateProvider Source of current character state on the v1 (session-scoped) path.
/// @param revisions Per-session state revision tracker on the v1 (session-scoped) path.
/// @param activePlayContext Source of the acquired play context on the v2 (play-context-scoped) path;
///     unused when `protocolVersion < 2`.
/// @param subscriptionState Per-connection subscription bookkeeping driving the v2 context-change
///     resync mechanism; unused when `protocolVersion < 2`.
/// @param bridgeInstanceId This bridge process's identity, stamped onto every v2 response envelope;
///     unused when `protocolVersion < 2`.
/// @param steadyNow Current monotonic time.
/// @param wallNow Current wall-clock time.
/// @return Responses and the connection-close decision.
[[nodiscard]] DispatchResult ProcessInboundMessage(
    const std::string& rawMessage, std::size_t& receivedMessageCount, const std::string& sessionId,
    std::int64_t protocolVersion, ConnectionId connection, SessionManager& sessionManager, ReplayGuard& replayGuard,
    security::ViolationTracker& violations, security::InboundMessageRateLimiter& rateLimiter,
    ConnectionTimeoutTracker& timeoutTracker, const CharacterStateProvider& stateProvider,
    RevisionTracker& revisions, const ActivePlayContext& activePlayContext, SubscriptionState& subscriptionState,
    const std::optional<std::string>& bridgeInstanceId, std::chrono::steady_clock::time_point steadyNow,
    std::chrono::system_clock::time_point wallNow);

}  // namespace dovahlink::application
