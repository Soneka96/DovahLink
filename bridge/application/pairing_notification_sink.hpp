#pragma once

#include <string_view>

namespace dovahlink::application {

/// Displays a newly issued pairing code to the user. The pairing handler owns when a code is
/// generated and how long it stays valid; this seam owns only how it reaches the user -- a native
/// Skyrim notification in the real implementation (a later phase), a recording double in tests and
/// the Skyrim-independent test harness. Mirrors `CharacterStateProvider`'s existing seam pattern.
class PairingNotificationSink {
public:
    /// Releases the interface without performing work.
    virtual ~PairingNotificationSink() = default;

    /// Displays `sixDigitCode` to the user for this pairing attempt only.
    /// @param sixDigitCode The code the client must enter to confirm pairing.
    virtual void NotifyPairingCodeAvailable(std::string_view sixDigitCode) = 0;
};

}  // namespace dovahlink::application
