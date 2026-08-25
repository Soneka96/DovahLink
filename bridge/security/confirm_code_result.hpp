#pragma once

#include "shared/enums.hpp"

#include <chrono>
#include <optional>

namespace dovahlink::security {

///  Result of a `PairingSession::TryConfirmCode` call.
struct ConfirmCodeResult {
    ///  Which of the documented outcomes occurred.
    ConfirmResult outcome;
    ///  `true` only when `outcome == ConfirmResult::kInvalid` for a genuine
    ///  wrong-code attempt against the caller's own active challenge (never for
    ///  "no challenge" or "not the owner"), and this attempt's own auto-renotify
    ///  cooldown allows redisplaying the code in Skyrim now. The caller (the
    ///  pairing handler) is responsible for actually redisplaying it;
    ///  `PairingSession` never touches Skyrim or any notification sink itself.
    bool shouldAutoRenotify = false;
    ///  The minimum safe whole-second wait before retrying, populated only when
    ///  `outcome == ConfirmResult::kPacingLimited`. It is rounded upward when a
    ///  positive fractional wait remains rather than always reporting the full
    ///  pacing interval.
    std::optional<std::chrono::seconds> retryAfterSeconds;
};

} //  namespace dovahlink::security
