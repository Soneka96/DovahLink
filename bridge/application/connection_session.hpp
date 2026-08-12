#pragma once

#include "application/session.hpp"
#include "application/subscription_handler.hpp"
#include "security/throttle.hpp"
#include "security/token_store.hpp"
#include "transport/websocket_session.hpp"

// This file depends on both application:: and transport:: types, an edge
// ai/context/skse/architecture.md's internal-shape diagram does not list
// for ordinary components ("transport → protocol messages" is the only
// transport-facing edge shown). It is deliberately here anyway: the same
// diagram names the coordinator as the one place allowed to depend on both
// ("coordinator → application state", "coordinator → transport"),
// documenting it as owning "callback registration, worker lifecycle, and
// transport lifecycle for the bridge." RunConnectionSession is that
// ownership made concrete for one connection's message loop -- a free
// function rather than a Coordinator method so Coordinator itself (bridge/
// application/coordinator.hpp) stays decoupled from the concrete transport
// types via its existing TransportLifecycle/WorkerPool seams; the plugin
// entry point step is expected to call this from whatever fulfills
// WorkerPool for a newly accepted connection.
namespace dovahlink::application {

// Runs one connection's full session lifecycle synchronously, on the
// calling thread, from an already-accepted, not-yet-WebSocket-handshaked
// socket through to disconnect:
//
// 1. Performs the WebSocket accept handshake (transport::WebSocketSession::
//    Accept), closing and returning immediately on failure.
// 2. Reads the first message, decodes it, and requires it to be `hello`,
//    processed through HandleHello (application/handshake_handler.hpp).
//    Sends the resulting hello_ack or error either way; on error, closes
//    and returns -- no session was ever created, so there is nothing to
//    invalidate (ai/context/protocol/security.md: a rejected hello does
//    not get a session).
// 3. On success, switches the transport idle timeout from the 5-second
//    handshake window to the 60-second idle window (transport::
//    WebSocketSession::SwitchToIdleTimeout; the underlying reason Beast
//    cannot do this switch itself is documented on that method), sends the
//    bridge's own `capabilities` message, and enters the message loop:
//    read one message, run it through ProcessInboundMessage (application/
//    message_dispatcher.hpp), send every response it produces, and stop
//    once it signals closeConnection or a transport read fails (the peer
//    disconnected, timed out, or sent an oversized/binary frame).
// 4. Always calls sessionManager.InvalidateSession(connection) before
//    returning if a session was created, and always closes the socket --
//    regardless of which of the above paths ended the loop
//    (ai/context/protocol/security.md: "Disconnect, timeout, protocol-limit
//    closure, or bridge shutdown invalidates the session before the socket
//    is released").
//
// Constructs its own per-connection ReplayGuard, security::ViolationTracker,
// security::InboundMessageRateLimiter, and RevisionTracker internally --
// these are pure implementation details of running one session, not seams
// a caller needs to observe or inject. tokenStore and tokenThrottle are the
// exceptions: both are plugin-lifetime (the one-time token is consumed
// exactly once across the plugin's life, and the failed-token throttle is
// explicitly global, not per-connection -- see their own headers), so the
// caller owns and passes them in.
void RunConnectionSession(transport::WebSocketSession& ws, security::TokenStore& tokenStore,
                           security::FailedTokenThrottle& tokenThrottle, SessionManager& sessionManager,
                           ConnectionId connection, const CharacterStateProvider& stateProvider);

}  // namespace dovahlink::application
