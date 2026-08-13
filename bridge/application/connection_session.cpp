#include "application/connection_session.hpp"

#include "application/connection_timeout_tracker.hpp"
#include "application/handshake_handler.hpp"
#include "application/message_dispatcher.hpp"
#include "application/replay_guard.hpp"
#include "application/revision_tracker.hpp"
#include "protocol/bounded_json.hpp"
#include "protocol/envelope.hpp"

#include <chrono>

namespace dovahlink::application {

namespace {

// A write failure here (peer gone, socket error) is handled uniformly by
// the next ReadMessage() call failing too, ending the session loop; there
/**
 * @brief Sends a protocol envelope over the WebSocket.
 *
 * @param envelope Protocol envelope to encode and send.
 */
void SendIfPossible(transport::WebSocketSession& ws, const protocol::Envelope& envelope) {
    (void)ws.WriteMessage(protocol::EncodeEnvelope(envelope));
}

}  /**
 * @brief Runs the WebSocket connection lifecycle, including handshake and message processing.
 *
 * Accepts the connection, authenticates the initial hello message, processes inbound
 * messages, sends generated responses, and closes the connection when the session ends.
 *
 * @param stateProvider Provides character state used to process inbound messages.
 */

void RunConnectionSession(transport::WebSocketSession& ws, security::TokenStore& tokenStore,
                           security::FailedTokenThrottle& tokenThrottle, SessionManager& sessionManager,
                           ConnectionId connection, const CharacterStateProvider& stateProvider) {
    if (!ws.Accept().has_value()) {
        return;
    }

    auto steadyNow = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(steadyNow);

    auto rawHello = ws.ReadMessage();
    if (!rawHello.has_value()) {
        ws.Close();
        return;
    }

    if (timeout.IsTimedOut(std::chrono::steady_clock::now())) {
        // The hello itself arrived, but only after trickling in slowly
        // enough to keep each individual OS-level socket read
        // (WebSocketSession::SetReadTimeout) from firing on its own. This is
        // the message-layer backstop HandleHello's own doc comment expects
        // "whatever owns the read loop" to provide.
        ws.Close();
        return;
    }

    auto parsedHello = protocol::ParseBoundedJson(*rawHello);
    if (!parsedHello.has_value()) {
        ws.Close();
        return;
    }
    auto helloEnvelope = protocol::DecodeEnvelope(*parsedHello);
    if (!helloEnvelope.has_value()) {
        ws.Close();
        return;
    }

    auto handshake =
        HandleHello(*helloEnvelope, tokenStore, tokenThrottle, sessionManager, connection, timeout, steadyNow);
    SendIfPossible(ws, handshake.response);
    if (handshake.closeConnection) {
        // HandleHello never creates a session on a failure path; nothing to
        // invalidate.
        ws.Close();
        return;
    }

    // HandleHello's success path always sets a sessionId (see its own
    // contract); handshake.closeConnection being false already proves this
    // is the success path.
    std::string sessionId = *handshake.response.sessionId;

    ws.SwitchToIdleTimeout();

    auto capabilities = BuildBridgeCapabilities(sessionId);
    if (capabilities.has_value()) {
        SendIfPossible(ws, *capabilities);
    }
    // If BuildBridgeCapabilities itself failed (the same unreachable-in-
    // practice CSPRNG failure every envelope-building path in this
    // codebase shares), there is no safe fallback message to send in its
    // place. The connection continues without ever having advertised its
    // capabilities -- a fixed, extremely unlikely degradation, not a
    // security concern: subscribe/snapshot_request do not depend on the
    // client having received it (protocol/schema/README.md: "A missing
    // capability means the feature is unavailable and the client must
    // remain usable without it").

    ReplayGuard replayGuard;
    security::ViolationTracker violations;
    security::InboundMessageRateLimiter rateLimiter;
    RevisionTracker revisions;

    while (true) {
        auto raw = ws.ReadMessage();
        if (!raw.has_value()) {
            break;
        }

        auto dispatch = ProcessInboundMessage(*raw, sessionId, connection, sessionManager, replayGuard, violations,
                                               rateLimiter, timeout, stateProvider, revisions,
                                               std::chrono::steady_clock::now(), std::chrono::system_clock::now());
        for (const protocol::Envelope& response : dispatch.responses) {
            SendIfPossible(ws, response);
        }
        if (dispatch.closeConnection) {
            break;
        }
    }

    sessionManager.InvalidateSession(connection);
    ws.Close();
}

}  // namespace dovahlink::application
