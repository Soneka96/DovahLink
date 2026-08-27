#include "security/violation_tracker.hpp"

#include "security/constants.hpp"

namespace dovahlink::security {

ViolationTracker::ViolationTracker() : counter_(kProtocolViolationWindow) {}

bool ViolationTracker::RecordViolationAndCheckLimit(
    std::chrono::steady_clock::time_point now) {
    return counter_.RecordEvent(now) >= kMaxProtocolViolations;
}

} //  namespace dovahlink::security
