#pragma once

#include <chrono>
#include <cstddef>
#include <deque>
#include <mutex>

namespace dovahlink::security {

// A thread-safe sliding-window event counter: reports how many events have
// been recorded within the trailing `window` duration, including the event
// just recorded. The caller supplies the current time explicitly on every
// call rather than this type reading a clock itself, so tests can drive it
// deterministically without real time passing (ai/context/skse/testing.md).
class RateWindowCounter {
public:
    explicit RateWindowCounter(std::chrono::steady_clock::duration window);

    [[nodiscard]] std::size_t RecordEvent(std::chrono::steady_clock::time_point now);

private:
    std::mutex mutex_;
    std::chrono::steady_clock::duration window_;
    std::deque<std::chrono::steady_clock::time_point> eventTimes_;
};

// Global failed-token attempt throttle: 5 attempts per 60 seconds
// (ai/context/protocol/security.md, bridge/security/limits.hpp). One instance
// is shared across every connection attempt, not one per connection, since
// the limit is global.
class FailedTokenThrottle {
public:
    FailedTokenThrottle();

    // Records one failed token attempt at `now`. Returns true once this
    // attempt exceeds the allowed rate and must be rejected outright without
    // being compared against the expected token.
    [[nodiscard]] bool RecordFailureAndCheckLimit(std::chrono::steady_clock::time_point now);

private:
    RateWindowCounter counter_;
};

// Per-connection protocol-violation tracker: closes the connection after 3
// violations within 30 seconds (ai/context/protocol/security.md,
// bridge/security/limits.hpp). One instance per connection.
class ViolationTracker {
public:
    ViolationTracker();

    // Records one protocol violation at `now`. Returns true once this
    // violation reaches the limit and the connection must be closed.
    [[nodiscard]] bool RecordViolationAndCheckLimit(std::chrono::steady_clock::time_point now);

private:
    RateWindowCounter counter_;
};

}  // namespace dovahlink::security
