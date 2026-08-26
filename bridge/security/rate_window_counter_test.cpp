#include "security/rate_window_counter.hpp"

#include <catch2/catch_test_macros.hpp>

#include <atomic>
#include <chrono>
#include <thread>
#include <vector>

using dovahlink::security::RateWindowCounter;

namespace {
///  Clock type used for deterministic sliding-window assertions.
using Clock = std::chrono::steady_clock;
} //  namespace

TEST_CASE("RateWindowCounter counts events within the window",
          "[security][rate_window_counter]") {
    RateWindowCounter counter(std::chrono::seconds(10));
    Clock::time_point t0 = Clock::now();

    CHECK(counter.RecordEvent(t0) == 1);
    CHECK(counter.RecordEvent(t0 + std::chrono::seconds(1)) == 2);
    CHECK(counter.RecordEvent(t0 + std::chrono::seconds(2)) == 3);
}

TEST_CASE("RateWindowCounter prunes events once the window has passed",
          "[security][rate_window_counter]") {
    RateWindowCounter counter(std::chrono::seconds(10));
    Clock::time_point t0 = Clock::now();

    CHECK(counter.RecordEvent(t0) == 1);
    CHECK(counter.RecordEvent(t0 + std::chrono::seconds(5)) == 2);
    //  25s after t0: both prior events (0s, 5s) are more than 10s old and prune
    //  away, leaving only this event.
    CHECK(counter.RecordEvent(t0 + std::chrono::seconds(25)) == 1);
}

TEST_CASE("RateWindowCounter prunes an event exactly `window` old",
          "[security][rate_window_counter]") {
    //  The pruning rule is `t <= now - window`, so an event exactly `window` old
    //  is treated as just outside the window (a half-open (now-window, now]
    //  interval).
    RateWindowCounter counter(std::chrono::seconds(10));
    Clock::time_point t0 = Clock::now();

    CHECK(counter.RecordEvent(t0) == 1);
    CHECK(counter.RecordEvent(t0 + std::chrono::seconds(10)) == 1);
}

TEST_CASE("RateWindowCounter reports active events without recording another",
          "[security][rate_window_counter]") {
    RateWindowCounter counter(std::chrono::seconds(10));
    Clock::time_point t0 = Clock::now();

    REQUIRE(counter.RecordEvent(t0) == 1);
    CHECK(counter.ActiveCount(t0 + std::chrono::seconds(1)) == 1);
    CHECK(counter.ActiveCount(t0 + std::chrono::seconds(2)) == 1);
}

TEST_CASE("RateWindowCounter is safe under concurrent recording",
          "[security][rate_window_counter]") {
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
