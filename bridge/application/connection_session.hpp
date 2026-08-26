#pragma once

#include "application/active_play_context_reader.hpp"
#include "application/pairing_notification_sink.hpp"
#include "application/session.hpp"
#include "application/subscription_handler.hpp"
#include "application/trust_mutation_coordinator.hpp"
#include "security/pairing_session.hpp"
#include "security/throttle.hpp"
#include "security/token_store.hpp"
#include "security/trust_store.hpp"
#include "transport/websocket_session.hpp"

#include <chrono>
#include <functional>

namespace dovahlink::application {

///  Supplies monotonic timestamps to one connection session.
using SteadyNowProvider =
    std::function<std::chrono::steady_clock::time_point()>;

///  Runs one accepted connection through authentication, message handling, and
///  cleanup. Session ownership is scope-bound so every exit after authentication
///  invalidates it. Notifies `pairingSession` of this connection's own
///  `clientId` reconnecting (right after a successful hello) and disconnecting
///  (at the same scope-bound teardown point), so a challenge or pending
///  credential it owns can apply the reconnect-grace lazy-expiry rules in
///  `ai/context/protocol/security.md`'s Phase 3.1 ownership model. A harmless
///  no-op when `clientId` owns nothing.
///  @param ws Accepted WebSocket session.
///  @param tokenStore Plugin-lifetime one-time token store.
///  @param tokenThrottle Plugin-lifetime failed-token throttle.
///  @param trustStore Plugin-lifetime persistent trust store.
///  @param credentialThrottle Plugin-lifetime failed device-credential attempt
///  throttle.
///  @param sessionManager Session registry for the connection.
///  @param connection Transport connection identifier.
///  @param activePlayContext Source of the current play-context identity this
///  connection reports.
///  @param pairingSession Plugin-lifetime pairing challenge/pending-credential
///  state machine.
///  @param mutationCoordinator Serializes pairing finalization and administrative
///  trust mutations.
///  @param pairingNotificationSink Displays a freshly generated pairing code to
///  the user.
///  @param bridgeInstanceId This bridge process's identity, stamped onto every
///  response envelope;
///      no value if generation failed at startup.
///  @param bridgeVersion The DovahLink Bridge/mod release version exposed to the
///  client in
///      `hello_ack.bridgeVersion` (`ai/context/protocol/compatibility.md`).
///  @param steadyNow Supplies current monotonic time; injectable for
///  deterministic timeout tests.
void RunConnectionSession(
    transport::WebSocketSession& ws, security::TokenStore& tokenStore,
    security::FailedTokenThrottle& tokenThrottle,
    security::TrustStore& trustStore,
    security::FailedTokenThrottle& credentialThrottle,
    SessionManager& sessionManager, ConnectionId connection,
    const IActivePlayContextReader& activePlayContext,
    security::PairingSession& pairingSession,
    ITrustMutationCoordinator& mutationCoordinator,
    PairingNotificationSink& pairingNotificationSink,
    const std::optional<std::string>& bridgeInstanceId,
    const std::string& bridgeVersion, SteadyNowProvider steadyNow = [] {
        return std::chrono::steady_clock::now();
    });

} //  namespace dovahlink::application
