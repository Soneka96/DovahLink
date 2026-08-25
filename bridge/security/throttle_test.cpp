#include "security/throttle.hpp"

#include <catch2/catch_test_macros.hpp>

#include <atomic>
#include <chrono>
#include <thread>
#include <vector>

using dovahlink::security::FailedTokenThrottle;
using dovahlink::security::InboundMessageRateLimiter;
using dovahlink::security::RateWindowCounter;
using dovahlink::security::ViolationTracker;

namespace {
///  Clock type used for deterministic sliding-window assertions.
using Clock = std::chrono::steady_clock;
} //  namespace

TEST_CASE("RateWindowCounter counts events within the window",
          "[security][throttle]") {
    RateWindowCounter counter(std::chrono::seconds(10));
    Clock::time_point t0 = Clock::now();

    CHECK(counter.RecordEvent(t0) == 1);
    CHECK(counter.RecordEvent(t0 + std::chrono::seconds(1)) == 2);
    CHECK(counter.RecordEvent(t0 + std::chrono::seconds(2)) == 3);
}

TEST_CASE("RateWindowCounter prunes events once the window has passed",
          "[security][throttle]") {
    RateWindowCounter counter(std::chrono::seconds(10));
    Clock::time_point t0 = Clock::now();

    CHECK(counter.RecordEvent(t0) == 1);
    CHECK(counter.RecordEvent(t0 + std::chrono::seconds(5)) == 2);
    //  25s after t0: both prior events (0s, 5s) are more than 10s old and prune
    //  away, leaving only this event.
    CHECK(counter.RecordEvent(t0 + std::chrono::seconds(25)) == 1);
}

TEST_CASE("RateWindowCounter prunes an event exactly `window` old",
          "[security][throttle]") {
    //  The pruning rule is `t <= now - window`, so an event exactly `window` old
    //  is treated as just outside the window (a half-open (now-window, now]
    //  interval).
    RateWindowCounter counter(std::chrono::seconds(10));
    Clock::time_point t0 = Clock::now();

    CHECK(counter.RecordEvent(t0) == 1);
    CHECK(counter.RecordEvent(t0 + std::chrono::seconds(10)) == 1);
}

TEST_CASE("RateWindowCounter reports active events without recording another",
          "[security][throttle]") {
    RateWindowCounter counter(std::chrono::seconds(10));
    Clock::time_point t0 = Clock::now();

    REQUIRE(counter.RecordEvent(t0) == 1);
    CHECK(counter.ActiveCount(t0 + std::chrono::seconds(1)) == 1);
    CHECK(counter.ActiveCount(t0 + std::chrono::seconds(2)) == 1);
}

TEST_CASE("RateWindowCounter is safe under concurrent recording",
          "[security][throttle]") {
    //  Uses a spin barrier to maximize actual thread overlap rather than a timing
    //  sleep to approximate concurrency. This proves
    //  the mutex prevents corruption/crashes under contention; it does not depend
    //  on a specific interleaving, since concurrent callers may race on which
    //  steady_clock::now() reading reaches the lock first.
    RateWindowCounter counter(std::chrono::seconds(60));

    constexpr int kThreads = 16;
    std::atomic<int> readyCount{0};
    std::atomic<bool> go{false};
    std::vector<std::thread> threads;
    threads.reserve(kThreads);

    for (int i = 0; i < kThreads; ++i) {
        threads.emplace_back([&]() {
            readyCount.fetch_add(1, std::memory_order_relaxed);
            while (!go.load(std::memory_order_acquire)) {
            }
            (void)counter.RecordEvent(Clock::now());
        });
    }

    while (readyCount.load(std::memory_order_relaxed) < kThreads) {
    }
    go.store(true, std::memory_order_release);

    for (std::thread& t : threads) {
        t.join();
    }

    //  All kThreads events are within a 60s window recorded moments apart; the
    //  final recorded count must reflect every one of them, with none lost or
    //  duplicated.
    CHECK(counter.RecordEvent(Clock::now()) == kThreads + 1);
}

TEST_CASE("FailedTokenThrottle allows the first 5 failures within the window",
          "[security][throttle]") {
    FailedTokenThrottle throttle;
    Clock::time_point t0 = Clock::now();

    for (int i = 0; i < 5; ++i) {
        CHECK_FALSE(throttle.IsBlocked(t0 + std::chrono::seconds(i)));
        throttle.RecordFailure(t0 + std::chrono::seconds(i));
    }
}

TEST_CASE("FailedTokenThrottle blocks the 6th attempt before validation",
          "[security][throttle]") {
    FailedTokenThrottle throttle;
    Clock::time_point t0 = Clock::now();

    for (int i = 0; i < 5; ++i) {
        REQUIRE_FALSE(throttle.IsBlocked(t0 + std::chrono::seconds(i)));
        throttle.RecordFailure(t0 + std::chrono::seconds(i));
    }
    CHECK(throttle.IsBlocked(t0 + std::chrono::seconds(5)));
}

TEST_CASE("FailedTokenThrottle allows attempts again once the window has fully "
          "passed",
          "[security][throttle]") {
    FailedTokenThrottle throttle;
    Clock::time_point t0 = Clock::now();

    for (int i = 0; i < 5; ++i) {
        REQUIRE_FALSE(throttle.IsBlocked(t0 + std::chrono::seconds(i)));
        throttle.RecordFailure(t0 + std::chrono::seconds(i));
    }
    REQUIRE(throttle.IsBlocked(t0 + std::chrono::seconds(5)));

    //  Blocked checks do not record another failure or extend the window.
    CHECK(throttle.IsBlocked(t0 + std::chrono::seconds(6)));
    CHECK_FALSE(throttle.IsBlocked(t0 + std::chrono::seconds(65)));
}

TEST_CASE("ViolationTracker does not close the connection for the first two "
          "violations",
          "[security][throttle]") {
    ViolationTracker tracker;
    Clock::time_point t0 = Clock::now();

    CHECK_FALSE(tracker.RecordViolationAndCheckLimit(t0));
    CHECK_FALSE(
        tracker.RecordViolationAndCheckLimit(t0 + std::chrono::seconds(1)));
}

TEST_CASE("ViolationTracker closes the connection on the 3rd violation within "
          "the window",
          "[security][throttle]") {
    ViolationTracker tracker;
    Clock::time_point t0 = Clock::now();

    REQUIRE_FALSE(tracker.RecordViolationAndCheckLimit(t0));
    REQUIRE_FALSE(
        tracker.RecordViolationAndCheckLimit(t0 + std::chrono::seconds(1)));
    CHECK(tracker.RecordViolationAndCheckLimit(t0 + std::chrono::seconds(2)));
}

TEST_CASE(
    "ViolationTracker does not close the connection once the window has passed",
    "[security][throttle]") {
    ViolationTracker tracker;
    Clock::time_point t0 = Clock::now();

    REQUIRE_FALSE(tracker.RecordViolationAndCheckLimit(t0));
    REQUIRE_FALSE(
        tracker.RecordViolationAndCheckLimit(t0 + std::chrono::seconds(1)));

    //  61s after t0: both prior violations (0s, 1s) are more than 30s old and
    //  prune away, so this is only the 1st violation within the window.
    CHECK_FALSE(
        tracker.RecordViolationAndCheckLimit(t0 + std::chrono::seconds(61)));
}

TEST_CASE(
    "InboundMessageRateLimiter allows the first 100 messages within one second",
    "[security][throttle]") {
    InboundMessageRateLimiter limiter;
    Clock::time_point t0 = Clock::now();

    for (int i = 0; i < 100; ++i) {
        CHECK_FALSE(limiter.RecordMessageAndCheckLimit(t0));
    }
}

TEST_CASE(
    "InboundMessageRateLimiter rejects the 101st message within one second",
    "[security][throttle]") {
    InboundMessageRateLimiter limiter;
    Clock::time_point t0 = Clock::now();

    for (int i = 0; i < 100; ++i) {
        REQUIRE_FALSE(limiter.RecordMessageAndCheckLimit(t0));
    }
    CHECK(limiter.RecordMessageAndCheckLimit(t0));
}

TEST_CASE(
    "InboundMessageRateLimiter allows messages again once a second has passed",
    "[security][throttle]") {
    InboundMessageRateLimiter limiter;
    Clock::time_point t0 = Clock::now();

    for (int i = 0; i < 100; ++i) {
        REQUIRE_FALSE(limiter.RecordMessageAndCheckLimit(t0));
    }
    REQUIRE(limiter.RecordMessageAndCheckLimit(t0)); //  101st, blocked

    CHECK_FALSE(limiter.RecordMessageAndCheckLimit(t0 + std::chrono::seconds(1) +
                                                   std::chrono::milliseconds(1)));
}
