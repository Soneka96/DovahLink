#pragma once

#include "security/enums.hpp"

#include <chrono>
#include <optional>

namespace dovahlink::security {

/// Result of a `PairingSession::TryRenotify` call.
struct RenotifyResult {
    /// Which of the three documented outcomes occurred.
    RenotifyOutcome outcome;
    /// The remaining cooldown, populated only when `outcome == RenotifyOutcome::kCooldown`.
    std::optional<std::chrono::seconds> retryAfterSeconds;
};

}  // namespace dovahlink::security
