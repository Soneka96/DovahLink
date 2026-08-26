#include "application/active_session_controller.hpp"

#include "protocol/envelope.hpp"
#include "protocol/session_invalidated_payload.hpp"

#include <utility>

namespace dovahlink::application {

ActiveSessionController::ActiveSessionController(
    SessionManager& sessionManager,
    IActiveSessionSocket& activeSessionSocket,
    const IActivePlayContextReader& activePlayContext,
    std::optional<std::string> bridgeInstanceId)
    : sessionManager_(sessionManager), activeSessionSocket_(activeSessionSocket),
      activePlayContext_(activePlayContext),
      bridgeInstanceId_(std::move(bridgeInstanceId)) {}

void ActiveSessionController::DisconnectIfClientActive(
    std::string_view clientId, std::string_view reason) {
    auto activeSocket = activeSessionSocket_.Capture();
    std::optional<std::string> activeSessionId;
    if (!activeSocket.has_value()) {
        return;
    }
    auto session = sessionManager_.SessionForConnection(activeSocket->connection);
    if (!session.has_value() || session->clientId != clientId) {
        return;
    }
    if (session->authMethod == SessionAuthMethod::kDeveloperToken) {
        return;
    }
    if (!sessionManager_.InvalidateSession(activeSocket->connection,
                                           session->sessionId)) {
        return;
    }
    activeSessionId = std::move(session->sessionId);
    NotifyAndShutdownActiveSocket(activeSocket->socket, std::move(activeSessionId),
                                  reason);
}

void ActiveSessionController::DisconnectActive(std::string_view reason) {
    auto activeSocket = activeSessionSocket_.Capture();
    if (!activeSocket.has_value()) {
        return;
    }
    auto session = sessionManager_.SessionForConnection(activeSocket->connection);
    if (!session.has_value()) {
        activeSocket->socket->Shutdown();
        return;
    }
    (void)sessionManager_.InvalidateSession(activeSocket->connection,
                                            session->sessionId);
    NotifyAndShutdownActiveSocket(activeSocket->socket,
                                  std::move(session->sessionId), reason);
}

void ActiveSessionController::NotifyAndShutdownActiveSocket(
    const transport::WebSocketSession::SocketHandle& socket,
    std::optional<std::string> sessionId, std::string_view reason) {
    auto envelope = protocol::BuildSessionInvalidatedEnvelope(
        std::move(sessionId), std::string(reason));
    envelope.bridgeInstanceId = bridgeInstanceId_;
    envelope.playContextId = activePlayContext_.CurrentPlayContextId();
    socket->ShutdownWithNotification(protocol::EncodeEnvelope(envelope));
}

} //  namespace dovahlink::application
