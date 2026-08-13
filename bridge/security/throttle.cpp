#include "security/throttle.hpp"

#include "security/limits.hpp"

namespace dovahlink::security {

/**
 * @brief Initializes a counter for a specified event-tracking window.
 *
 * @param window Duration for which recorded events remain active.
 */
RateWindowCounter::RateWindowCounter(std::chrono::steady_clock::duration window) : window_(window) {}

/**
 * @brief Records an event and counts events within the active time window.
 *
 * @param now Timestamp of the event.
 * @return Number of recorded events newer than the window cutoff, including
 *         the newly recorded event.
 */
std::size_t RateWindowCounter::RecordEvent(std::chrono::steady_clock::time_point now) {
    std::lock_guard<std::mutex> lock(mutex_);

    // Concurrent callers each read steady_clock::now() independently before
    // acquiring the lock, so insertion order is not guaranteed to match
    // timestamp order; erase every stale entry rather than assuming the
    // oldest is always at the front.
    std::erase_if(eventTimes_,
                   [&](std::chrono::steady_clock::time_point t) { return t <= now - window_; });
    eventTimes_.push_back(now);
    return eventTimes_.size();
}

/**
 * @brief Initializes failed-token attempt tracking.
 */
FailedTokenThrottle::FailedTokenThrottle() : counter_(kFailedTokenAttemptWindow) {}

/**
 * @brief Records a failed-token attempt and checks whether the attempt limit has been exceeded.
 *
 * @param now Timestamp of the failed-token attempt.
 * @return true if the number of attempts within the configured window exceeds the maximum, false otherwise.
 */
bool FailedTokenThrottle::RecordFailureAndCheckLimit(std::chrono::steady_clock::time_point now) {
    return counter_.RecordEvent(now) > kMaxFailedTokenAttempts;
}

/**
 * @brief Initializes the protocol-violation tracker.
 */
ViolationTracker::ViolationTracker() : counter_(kProtocolViolationWindow) {}

/**
 * @brief Records a protocol violation and checks whether the allowed limit has been reached.
 *
 * @param now Timestamp of the violation.
 * @return true if the number of violations in the configured window is greater than or equal to the maximum allowed, false otherwise.
 */
bool ViolationTracker::RecordViolationAndCheckLimit(std::chrono::steady_clock::time_point now) {
    return counter_.RecordEvent(now) >= kMaxProtocolViolations;
}

InboundMessageRateLimiter::InboundMessageRateLimiter() : counter_(kInboundMessageRateWindow) {}

/**
 * @brief Records an inbound message and determines whether the rate limit is exceeded.
 *
 * @param now Timestamp of the inbound message.
 * @return `true` if the active message count exceeds the configured per-second maximum, `false` otherwise.
 */
bool InboundMessageRateLimiter::RecordMessageAndCheckLimit(std::chrono::steady_clock::time_point now) {
    return counter_.RecordEvent(now) > kMaxInboundMessagesPerSecond;
}

}  // namespace dovahlink::security
