#pragma once

#include <chrono>
#include <cstddef>
#include <deque>
#include <mutex>

namespace dovahlink::security {

/// Thread-safe sliding-window counter with caller-supplied timestamps.
class RateWindowCounter {
public:
    /// Creates a counter whose recorded events remain active for `window`.
    explicit RateWindowCounter(std::chrono::steady_clock::duration window);

    /// Records an event and returns the active event count including it.
    [[nodiscard]] std::size_t RecordEvent(std::chrono::steady_clock::time_point now);

private:
    /// Serializes access to the counter state.
    std::mutex mutex_;
    /// Duration for which an event remains in the active window.
    std::chrono::steady_clock::duration window_;
    /// Recorded event timestamps, including entries not yet pruned.
    std::deque<std::chrono::steady_clock::time_point> eventTimes_;
};

/// Global failed-token attempt throttle shared across connection attempts.
class FailedTokenThrottle {
public:
    /// Creates a throttle using the configured failed-token window.
    FailedTokenThrottle();

    /// Records a failed attempt and reports whether the configured limit is exceeded.
    [[nodiscard]] bool RecordFailureAndCheckLimit(std::chrono::steady_clock::time_point now);

private:
    /// Counter tracking failed attempts across all connections.
    RateWindowCounter counter_;
};

/// Per-connection protocol-violation tracker.
class ViolationTracker {
public:
    /// Creates a tracker using the configured violation window.
    ViolationTracker();

    /// Records a violation and reports whether the connection must close.
    [[nodiscard]] bool RecordViolationAndCheckLimit(std::chrono::steady_clock::time_point now);

private:
    /// Counter tracking violations for one connection.
    RateWindowCounter counter_;
};

/// Per-connection inbound message-rate limiter.
class InboundMessageRateLimiter {
public:
    /// Creates a limiter using the configured inbound-message window.
    InboundMessageRateLimiter();

    /// Records a message and reports whether the rate limit is exceeded.
    [[nodiscard]] bool RecordMessageAndCheckLimit(std::chrono::steady_clock::time_point now);

private:
    /// Counter tracking messages for one connection.
    RateWindowCounter counter_;
};

}  // namespace dovahlink::security
