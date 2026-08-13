#pragma once

#include "application/connection_timeout_tracker.hpp"
#include "application/replay_guard.hpp"
#include "application/revision_tracker.hpp"
#include "application/session.hpp"
#include "application/subscription_handler.hpp"
#include "protocol/envelope.hpp"
#include "security/throttle.hpp"

#include <chrono>
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

/// Validates the existing session, rate-limits, and dispatches one inbound message.
/// Oversized frames and exhausted session limits request immediate closure without a response.
/// @param rawMessage Encoded inbound WebSocket text.
/// @param sessionId Authenticated session identifier.
/// @param connection Transport connection identifier.
/// @param sessionManager Session registry.
/// @param replayGuard Per-session message-ID guard.
/// @param violations Per-connection protocol-violation tracker.
/// @param rateLimiter Per-connection inbound rate limiter.
/// @param timeoutTracker Connection timeout tracker.
/// @param stateProvider Source of current character state.
/// @param revisions Per-session state revision tracker.
/// @param steadyNow Current monotonic time.
/// @param wallNow Current wall-clock time.
/// @return Responses and the connection-close decision.
[[nodiscard]] DispatchResult ProcessInboundMessage(
    const std::string& rawMessage, const std::string& sessionId, ConnectionId connection,
    SessionManager& sessionManager, ReplayGuard& replayGuard, security::ViolationTracker& violations,
    security::InboundMessageRateLimiter& rateLimiter, ConnectionTimeoutTracker& timeoutTracker,
    const CharacterStateProvider& stateProvider, RevisionTracker& revisions,
    std::chrono::steady_clock::time_point steadyNow, std::chrono::system_clock::time_point wallNow);

}  // namespace dovahlink::application
