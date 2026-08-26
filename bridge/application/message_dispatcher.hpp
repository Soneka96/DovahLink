#pragma once

#include "application/active_play_context_reader.hpp"
#include "application/connection_timeout_tracker.hpp"
#include "application/pairing_notification_sink.hpp"
#include "application/replay_guard.hpp"
#include "application/session.hpp"
#include "application/subscription_handler.hpp"
#include "application/trust_mutation_coordinator.hpp"
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

///  Contains responses produced while processing one inbound message.
struct DispatchResult {
    ///  Response envelopes to send in order.
    std::vector<protocol::Envelope> responses;

    ///  Whether the connection must close after responses are sent.
    bool closeConnection = false;
};

///  Counts, validates, rate-limits, and dispatches one inbound message.
///  Oversized frames and exhausted session limits request immediate closure
///  without a response.
///  @param rawMessage Encoded inbound WebSocket text.
///  @param receivedMessageCount Number of completed messages read in this
///  session; incremented before decoding.
///  @param sessionId Authenticated session identifier.
///  @param connection Transport connection identifier.
///  @param sessionManager Session registry. Also determines this call's allowed
///  message types: a
///      `Restricted` session may only exchange `ping`/`capabilities`/the pairing
///      message types; a `Full` session may exchange
///      `ping`/`capabilities`/`subscribe`/`snapshot_request`/ `rename_request`
///      (pairing messages are pointless once already trusted).
///  @param replayGuard Per-session message-ID guard.
///  @param violations Per-connection protocol-violation tracker.
///  @param rateLimiter Per-connection inbound rate limiter.
///  @param timeoutTracker Connection timeout tracker.
///  @param activePlayContext Source of the current play-context identity, stamped
///      onto every response as `playContextId`.
///  @param pairingSession Bridge-lifetime pairing challenge/pending-credential
///  state machine, for
///      the three pairing message types.
///  @param trustStore Persistent trust store, for full-session trust-store
///  operations such as `rename_request`.
///  @param mutationCoordinator Serializes pairing finalization and administrative
///  trust mutations.
///  @param pairingNotificationSink Displays a freshly generated pairing code to
///  the user.
///  @param bridgeInstanceId This bridge process's identity, stamped onto every
///  response this call
///      produces, including an early connection-hygiene rejection. The client
///      identity bound to a session is never stamped on any response here: once
///      a session exists, it is owned state
///      (@ref SessionManager::ClientIdForConnection), not a value repeated on
///      the wire.
///  @param steadyNow Current monotonic time.
///  @return Responses and the connection-close decision.
[[nodiscard]] DispatchResult ProcessInboundMessage(
    const std::string& rawMessage, std::size_t& receivedMessageCount,
    const std::string& sessionId, ConnectionId connection,
    SessionManager& sessionManager, ReplayGuard& replayGuard,
    security::ViolationTracker& violations,
    security::InboundMessageRateLimiter& rateLimiter,
    ConnectionTimeoutTracker& timeoutTracker,
    const IActivePlayContextReader& activePlayContext,
    security::PairingSession& pairingSession, security::TrustStore& trustStore,
    ITrustMutationCoordinator& mutationCoordinator,
    PairingNotificationSink& pairingNotificationSink,
    const std::optional<std::string>& bridgeInstanceId,
    std::chrono::steady_clock::time_point steadyNow);

} //  namespace dovahlink::application
