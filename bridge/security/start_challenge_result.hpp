#pragma once

#include "shared/enums.hpp"

#include <optional>
#include <string>

namespace dovahlink::security {

/// Result of a `PairingSession::TryStartChallenge` call.
struct StartChallengeResult {
    /// Which of the four outcomes occurred.
    StartChallengeOutcome outcome;
    /// The six-digit code to display, populated only when `outcome ==
    /// StartChallengeOutcome::kStarted`.
    std::optional<std::string> code;
};

}  // namespace dovahlink::security
