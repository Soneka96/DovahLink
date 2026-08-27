#include "application/connection_session.hpp"

#include "application/connection_timeout_tracker.hpp"
#include "application/replay_guard.hpp"
#include "application/subscription_handler.hpp"
#include "protocol/bounded_json.hpp"
#include "protocol/envelope.hpp"
#include "security/inbound_message_rate_limiter.hpp"
#include "security/violation_tracker.hpp"

#include <chrono>
#include <cstddef>
#include <utility>

namespace dovahlink::application {

namespace {

//  A write failure here is handled uniformly by the next ReadMessage() call,
//  which ends the session loop.
///  Sends an encoded envelope and leaves write failures to the read-loop
///  cleanup.
void SendIfPossible(transport::IWebSocketSession& ws,
                    const protocol::Envelope& envelope) {
    (void)ws.WriteMessage(protocol::EncodeEnvelope(envelope));
}

} //  namespace

ConnectionSession::ConnectionSession(
    IHandshakeHandler& handshakeHandler, IMessageDispatcher& messageDispatcher,
    const IActivePlayContextReader& activePlayContext,
    security::IPairingSession& pairingSession,
    std::optional<std::string> bridgeInstanceId)
    : handshakeHandler_(handshakeHandler),
      messageDispatcher_(messageDispatcher),
      activePlayContext_(activePlayContext), pairingSession_(pairingSession),
      bridgeInstanceId_(std::move(bridgeInstanceId)) {}

void ConnectionSession::Run(transport::IWebSocketSession& ws,
                            ConnectionId connection,
                            SteadyNowProvider steadyNow) {
    if (!ws.Accept().has_value()) {
        return;
    }

    ConnectionTimeoutTracker timeout(steadyNow());

    auto rawHello = ws.ReadMessage(timeout.Deadline());
    if (!rawHello.has_value()) {
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

    auto postReadNow = steadyNow();
    if (timeout.IsTimedOut(postReadNow)) {
        //  The hello itself arrived, but only after trickling in slowly
        //  enough to keep the WebSocket operation's inactivity timeout from
        //  firing on its own. This is the message-layer backstop
        //  `IHandshakeHandler::Handle`'s own doc comment expects "whatever
        //  owns the read loop" to provide.
        ws.Close();
        return;
    }

    auto handshake =
        handshakeHandler_.Handle(*helloEnvelope, connection, timeout, postReadNow);
    SendIfPossible(ws, handshake.response);
    if (handshake.closeConnection) {
        ws.Close();
        return;
    }

    if (!handshake.sessionLease.has_value() ||
        !handshake.response.sessionId.has_value()) {
        ws.Close();
        return;
    }

    //  The lease keeps the authenticated session valid for exactly this
    //  connection scope and invalidates it on every exit path.
    auto sessionLease = std::move(handshake.sessionLease);

    std::string sessionId = *handshake.response.sessionId;
    //  Always present on this success path (echoed from the decoded hello's own
    //  required field); guarded defensively so a hypothetically absent value
    //  degrades to skipping notification rather than dereferencing nothing.
    std::optional<std::string> clientId = handshake.response.clientId;
    if (clientId.has_value()) {
        pairingSession_.NotifyReconnected(*clientId, postReadNow);
    }

    ws.SwitchToIdleTimeout();

    auto capabilities = BuildBridgeCapabilities(sessionId);
    if (capabilities.has_value()) {
        capabilities->bridgeInstanceId = bridgeInstanceId_;
        capabilities->playContextId = activePlayContext_.CurrentPlayContextId();
        SendIfPossible(ws, *capabilities);
    }
    //  If BuildBridgeCapabilities itself failed (the same unreachable-in-
    //  practice CSPRNG failure every envelope-building path in this
    //  codebase shares), there is no safe fallback message to send in its
    //  place. The connection continues without ever having advertised its
    //  capabilities -- a fixed, extremely unlikely degradation, not a
    //  security concern: subscribe/snapshot_request do not depend on the
    //  client having received it (protocol/schema/README.md: "A missing
    //  capability means the feature is unavailable and the client must
    //  remain usable without it").

    ReplayGuard replayGuard;
    security::ViolationTracker violations;
    security::InboundMessageRateLimiter rateLimiter;
    std::size_t receivedMessageCount = 0;

    while (true) {
        auto raw = ws.ReadMessage(timeout.Deadline());
        if (!raw.has_value()) {
            break;
        }

        auto dispatch = messageDispatcher_.Process(
            *raw, receivedMessageCount, sessionId, connection, replayGuard,
            violations, rateLimiter, timeout, steadyNow());
        for (const protocol::Envelope& response : dispatch.responses) {
            SendIfPossible(ws, response);
        }
        if (dispatch.closeConnection) {
            break;
        }
    }

    if (clientId.has_value()) {
        pairingSession_.NotifyDisconnected(*clientId, steadyNow());
    }
    sessionLease.reset();
    ws.Close();
}

} //  namespace dovahlink::application
