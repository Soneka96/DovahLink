#pragma once

#include "application/connection_timeout_tracker.hpp"
#include "application/pairing_notification_sink.hpp"
#include "application/play_context.hpp"
#include "application/replay_guard.hpp"
#include "application/session.hpp"
#include "application/subscription_handler.hpp"
#include "protocol/envelope.hpp"
#include "security/pairing_session.hpp"
#include "security/throttle.hpp"
#include "security/trust_store.hpp"

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

/// Per-connection subscription bookkeeping for the "existing connected
/// clients learn context transitions" mechanism (protocol/schema/README.md).
struct SubscriptionState {
    /// State areas this connection is currently subscribed to.
    std::vector<std::string> subscribedStateAreas;

    /// `playContextId` last reported to this connection, or no value before
    /// the first message that could carry one.
    std::optional<std::string> lastKnownPlayContextId;
};

/// Counts, validates, rate-limits, and dispatches one inbound message.
/// Oversized frames and exhausted session limits request immediate closure without a response.
/// @param rawMessage Encoded inbound WebSocket text.
/// @param receivedMessageCount Number of completed messages read in this session; incremented before decoding.
/// @param sessionId Authenticated session identifier.
/// @param connection Transport connection identifier.
/// @param sessionManager Session registry. Also determines this call's allowed message types: a
///     `Restricted` session may only exchange `ping`/`capabilities`/the pairing message types; a
///     `Full` session may exchange `ping`/`capabilities`/`subscribe`/`snapshot_request`/
///     `rename_request` (pairing messages are pointless once already trusted).
/// @param replayGuard Per-session message-ID guard.
/// @param violations Per-connection protocol-violation tracker.
/// @param rateLimiter Per-connection inbound rate limiter.
/// @param timeoutTracker Connection timeout tracker.
/// @param activePlayContext Source of the acquired play context this connection's state and
///     revisions belong to; a connection with no active context reads the existing unavailable shape.
/// @param subscriptionState Per-connection subscription bookkeeping driving the context-change
///     resync mechanism.
/// @param pairingSession Bridge-lifetime pairing challenge/pending-credential state machine, for
///     the three pairing message types.
/// @param trustStore Persistent trust store, for `pairing_ack`'s idempotent-retry check and commit.
/// @param pairingNotificationSink Displays a freshly generated pairing code to the user.
/// @param bridgeInstanceId This bridge process's identity, stamped onto every response this call
///     produces, including an early connection-hygiene rejection. The client identity bound to a
///     session is never stamped on any response here: once a session exists, it is owned state
///     (@ref SessionManager::ClientIdForConnection), not a value repeated on the wire.
/// @param steadyNow Current monotonic time.
/// @param wallNow Current wall-clock time.
/// @return Responses and the connection-close decision.
[[nodiscard]] DispatchResult ProcessInboundMessage(
    const std::string& rawMessage, std::size_t& receivedMessageCount, const std::string& sessionId,
    ConnectionId connection, SessionManager& sessionManager, ReplayGuard& replayGuard,
    security::ViolationTracker& violations, security::InboundMessageRateLimiter& rateLimiter,
    ConnectionTimeoutTracker& timeoutTracker, const ActivePlayContext& activePlayContext,
    SubscriptionState& subscriptionState, security::PairingSession& pairingSession,
    security::TrustStore& trustStore, PairingNotificationSink& pairingNotificationSink,
    const std::optional<std::string>& bridgeInstanceId, std::chrono::steady_clock::time_point steadyNow,
    std::chrono::system_clock::time_point wallNow);

}  // namespace dovahlink::application
