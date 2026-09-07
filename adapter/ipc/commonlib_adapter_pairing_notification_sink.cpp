#include "RE/Skyrim.h"

#include "ipc/commonlib_adapter_pairing_notification_sink.hpp"

namespace dovahlink::adapter::ipc {

bool CommonLibAdapterPairingNotificationSink::Display(const std::string &code,
                                                      PairingDisplayMode mode) {
  std::string message =
      mode == PairingDisplayMode::kWrongCodeRedisplay
          ? "DovahLink: wrong code. Your pairing code is: " + code
          : "DovahLink pairing code: " + code;
  RE::DebugNotification(message.c_str());
  return true;
}

void CommonLibAdapterPairingNotificationSink::NotifyAttemptsExhausted() {
  RE::DebugNotification(
      "DovahLink: too many wrong attempts. Request pairing again.");
}

} //  namespace dovahlink::adapter::ipc
