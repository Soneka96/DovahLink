#pragma once

#include "application/play_context.hpp"
#include "application/session.hpp"
#include "application/subscription_handler.hpp"
#include "security/throttle.hpp"
#include "security/token_store.hpp"
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
/// @param sessionManager Session registry for the connection.
/// @param connection Transport connection identifier.
/// @param stateProvider Source of current character state on the v1 (session-scoped) path.
/// @param activePlayContext Source of the acquired play context on the v2 (play-context-scoped) path.
/// @param bridgeInstanceId This bridge process's identity, stamped onto every v2 response envelope;
///     no value if generation failed at startup.
/// @param steadyNow Supplies current monotonic time; injectable for deterministic timeout tests.
void RunConnectionSession(transport::WebSocketSession& ws, security::TokenStore& tokenStore,
                           security::FailedTokenThrottle& tokenThrottle, SessionManager& sessionManager,
                           ConnectionId connection, const CharacterStateProvider& stateProvider,
                           const ActivePlayContext& activePlayContext,
                           const std::optional<std::string>& bridgeInstanceId,
                           SteadyNowProvider steadyNow = [] { return std::chrono::steady_clock::now(); });

}  // namespace dovahlink::application
