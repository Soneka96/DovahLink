#pragma once

#include "security/enums.hpp"

namespace dovahlink::security {

/// Result of a `PairingSession::TryConfirmCode` call.
struct ConfirmCodeResult {
    /// Which of the documented outcomes occurred.
    ConfirmResult outcome;
    /// `true` only when `outcome == ConfirmResult::kInvalid` for a genuine wrong-code attempt
    /// against the caller's own active challenge (never for "no challenge" or "not the owner"),
    /// and this attempt's own auto-renotify cooldown allows redisplaying the code in Skyrim now.
    /// The caller (the pairing handler) is responsible for actually redisplaying it;
    /// `PairingSession` never touches Skyrim or any notification sink itself.
    bool shouldAutoRenotify = false;
};

}  // namespace dovahlink::security
