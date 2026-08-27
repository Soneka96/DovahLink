#pragma once

#include <string_view>

namespace dovahlink::application {

///  Displays a newly issued pairing code to the user. The pairing handler owns
///  when a code is generated and how long it stays valid; this seam owns only
///  how it reaches the user -- a native Skyrim notification in the real
///  implementation (a later phase), a recording double in tests and the
///  Skyrim-independent test harness.
class IPairingNotificationSink {
  public:
    ///  Releases the interface without performing work.
    virtual ~IPairingNotificationSink() = default;

    ///  Displays `sixDigitCode` to the user for this pairing attempt only. Used
    ///  both for a freshly started challenge and for a manual "show code again"
    ///  redisplay (`PairingSession:: TryRenotify`) -- both are the same
    ///  original-display wording.
    ///  @param sixDigitCode The code the client must enter to confirm pairing.
    virtual void NotifyPairingCodeAvailable(std::string_view sixDigitCode) = 0;

    ///  Redisplays `sixDigitCode` after a wrong `pairing_confirm` attempt, using
    ///  wording that distinguishes a mistake from the original display. Subject to
    ///  its own independent cooldown
    ///  (`PairingSession`'s auto-renotify cooldown), so this is not called for
    ///  every wrong attempt.
    ///  @param sixDigitCode The still-active code the user should re-enter
    ///  correctly.
    virtual void NotifyPairingCodeIncorrect(std::string_view sixDigitCode) = 0;

    ///  Displays a distinct too-many-attempts notification once the wrong-attempt
    ///  hard limit cancels the challenge. The destroyed code is never shown again,
    ///  so this carries no code.
    virtual void NotifyPairingAttemptsExhausted() = 0;
};

} //  namespace dovahlink::application
