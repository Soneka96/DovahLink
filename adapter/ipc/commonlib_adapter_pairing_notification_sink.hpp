#pragma once

#include "ipc/adapter_pairing_notification_sink.hpp"

#include <string>

namespace dovahlink::adapter::ipc {

///  Displays a pairing code through Skyrim's built-in on-screen notification
///  (`RE::DebugNotification`) -- the same base-engine channel console
///  commands and vanilla messages use, with no SkyUI or other UI-mod
///  dependency. Carries no pairing state or timing logic of its own; the
///  host alone owns when a code is generated, how long it stays valid, and
///  which display intent applies, per `IAdapterPairingNotificationSink`.
class CommonLibAdapterPairingNotificationSink final
    : public IAdapterPairingNotificationSink {
public:
  ///  @copydoc IAdapterPairingNotificationSink::Display
  bool Display(const std::string &code, PairingDisplayMode mode) override;

  ///  @copydoc IAdapterPairingNotificationSink::NotifyAttemptsExhausted
  void NotifyAttemptsExhausted() override;
};

} //  namespace dovahlink::adapter::ipc
