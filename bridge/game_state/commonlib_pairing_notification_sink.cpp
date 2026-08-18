#include "game_state/commonlib_pairing_notification_sink.hpp"

#include "RE/Skyrim.h"

#include <string>

namespace dovahlink::game_state {

void CommonLibPairingNotificationSink::NotifyPairingCodeAvailable(std::string_view sixDigitCode) {
    std::string message = "DovahLink pairing code: " + std::string(sixDigitCode);
    RE::DebugNotification(message.c_str());
}

}  // namespace dovahlink::game_state
