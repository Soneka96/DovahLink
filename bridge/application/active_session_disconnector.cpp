#include "application/active_session_disconnector.hpp"

namespace dovahlink::application {

ActiveSessionDisconnector::ActiveSessionDisconnector(
    IActiveSessionController& controller)
    : controller_(controller) {}

void ActiveSessionDisconnector::DisconnectIfClientActive(
    std::string_view clientId, std::string_view reason) {
    controller_.DisconnectIfClientActive(clientId, reason);
}

void ActiveSessionDisconnector::DisconnectActive(std::string_view reason) {
    controller_.DisconnectActive(reason);
}

} //  namespace dovahlink::application
