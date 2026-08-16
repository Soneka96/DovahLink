#pragma once

#include "application/pairing_notification_sink.hpp"
#include "application/play_context.hpp"
#include "application/session.hpp"
#include "application/subscription_handler.hpp"
#include "security/pairing_session.hpp"
#include "security/throttle.hpp"
#include "security/token_store.hpp"
#include "security/trust_store.hpp"
#include "transport/websocket_session.hpp"

#include <chrono>
#include <functional>

namespace dovahlink::application {

/// Supplies monotonic timestamps to one connection session.
using SteadyNowProvider = std::function<std::chrono::steady_clock::time_point()>;

/// Runs one accepted connection through authentication, message handling, and cleanup.
/// Session ownership is scope-bound so every exit after authentication invalidates it.
/// @param ws Accepted WebSocket session.
/// @param tokenStore Plugin-lifetime one-time token store.
/// @param tokenThrottle Plugin-lifetime failed-token throttle.
/// @param trustStore Plugin-lifetime persistent trust store.
/// @param credentialThrottle Plugin-lifetime failed device-credential attempt throttle.
/// @param sessionManager Session registry for the connection.
/// @param connection Transport connection identifier.
/// @param activePlayContext Source of the acquired play context this connection's state and
///     revisions belong to.
/// @param pairingSession Plugin-lifetime pairing challenge/pending-credential state machine.
/// @param pairingNotificationSink Displays a freshly generated pairing code to the user.
/// @param bridgeInstanceId This bridge process's identity, stamped onto every response envelope;
///     no value if generation failed at startup.
/// @param bridgeVersion The DovahLink Bridge/mod release version exposed to the client in
///     `hello_ack.bridgeVersion` (`ai/context/protocol/compatibility.md`).
/// @param steadyNow Supplies current monotonic time; injectable for deterministic timeout tests.
void RunConnectionSession(transport::WebSocketSession& ws, security::TokenStore& tokenStore,
                           security::FailedTokenThrottle& tokenThrottle, security::TrustStore& trustStore,
                           security::FailedTokenThrottle& credentialThrottle, SessionManager& sessionManager,
                           ConnectionId connection, const ActivePlayContext& activePlayContext,
                           security::PairingSession& pairingSession, PairingNotificationSink& pairingNotificationSink,
                           const std::optional<std::string>& bridgeInstanceId, const std::string& bridgeVersion,
                           SteadyNowProvider steadyNow = [] { return std::chrono::steady_clock::now(); });

}  // namespace dovahlink::application
