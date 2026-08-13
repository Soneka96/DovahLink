#pragma once

#include "application/session.hpp"
#include "application/subscription_handler.hpp"
#include "security/throttle.hpp"
#include "security/token_store.hpp"
#include "transport/websocket_session.hpp"

namespace dovahlink::application {

/// Runs one accepted connection through authentication, message handling, and cleanup.
/// Every exit after session creation invalidates that session before the socket closes.
/// @param ws Accepted WebSocket session.
/// @param tokenStore Plugin-lifetime one-time token store.
/// @param tokenThrottle Plugin-lifetime failed-token throttle.
/// @param sessionManager Session registry for the connection.
/// @param connection Transport connection identifier.
/// @param stateProvider Source of current character state.
void RunConnectionSession(transport::WebSocketSession& ws, security::TokenStore& tokenStore,
                           security::FailedTokenThrottle& tokenThrottle, SessionManager& sessionManager,
                           ConnectionId connection, const CharacterStateProvider& stateProvider);

}  // namespace dovahlink::application
