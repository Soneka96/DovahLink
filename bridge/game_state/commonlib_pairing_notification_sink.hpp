#pragma once

#include "application/pairing_notification_sink.hpp"

namespace dovahlink::game_state {

///  Displays a freshly issued pairing code through Skyrim's built-in on-screen
///  notification
///  (`RE::DebugNotification`) -- the same base-engine channel console commands
///  and vanilla messages use, with no SkyUI or other UI-mod dependency. Carries
///  no pairing state or timing logic of its own; `PairingSession` alone owns
///  when a code is generated and how long it stays valid (see
///  `application::IPairingNotificationSink`), and this adapter trusts
///  `PairingSession`'s own CSPRNG-based code generator for the six-digit shape
///  of `sixDigitCode` without re-validating it -- there is no externally
///  reachable path that supplies this argument.
class CommonLibPairingNotificationSink
    : public application::IPairingNotificationSink {
  public:
    ///  @copydoc application::IPairingNotificationSink::NotifyPairingCodeAvailable
    void NotifyPairingCodeAvailable(std::string_view sixDigitCode) override;

    ///  @copydoc application::IPairingNotificationSink::NotifyPairingCodeIncorrect
    void NotifyPairingCodeIncorrect(std::string_view sixDigitCode) override;

    ///  @copydoc
    ///  application::IPairingNotificationSink::NotifyPairingAttemptsExhausted
    void NotifyPairingAttemptsExhausted() override;
};

} //  namespace dovahlink::game_state
