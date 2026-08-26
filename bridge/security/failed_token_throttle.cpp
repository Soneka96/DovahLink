#include "security/failed_token_throttle.hpp"

#include "security/limits.hpp"

namespace dovahlink::security {

FailedTokenThrottle::FailedTokenThrottle()
    : counter_(kFailedTokenAttemptWindow) {}

bool FailedTokenThrottle::IsBlocked(std::chrono::steady_clock::time_point now) {
    return counter_.ActiveCount(now) >= kMaxFailedTokenAttempts;
}

void FailedTokenThrottle::RecordFailure(
    std::chrono::steady_clock::time_point now) {
    (void)counter_.RecordEvent(now);
}

std::optional<FailedTokenReservation>
FailedTokenThrottle::TryReserve(std::chrono::steady_clock::time_point now) {
    if (!counter_.TryReserve(now, kMaxFailedTokenAttempts)) {
        return std::nullopt;
    }
    return FailedTokenReservation(counter_, now);
}

} //  namespace dovahlink::security
