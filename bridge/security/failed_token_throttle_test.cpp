#include "security/failed_token_throttle.hpp"

#include <catch2/catch_test_macros.hpp>

#include <atomic>
#include <chrono>
#include <thread>
#include <vector>

using dovahlink::security::FailedTokenThrottle;

namespace {
///  Clock type used for deterministic sliding-window assertions.
using Clock = std::chrono::steady_clock;
} //  namespace

TEST_CASE("FailedTokenThrottle allows the first 5 failures within the window",
          "[security][failed_token_throttle]") {
    FailedTokenThrottle throttle;
    Clock::time_point t0 = Clock::now();

    for (int i = 0; i < 5; ++i) {
        CHECK_FALSE(throttle.IsBlocked(t0 + std::chrono::seconds(i)));
        throttle.RecordFailure(t0 + std::chrono::seconds(i));
    }
}

TEST_CASE("FailedTokenThrottle blocks the 6th attempt before validation",
          "[security][failed_token_throttle]") {
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
          "[security][failed_token_throttle]") {
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

TEST_CASE("FailedTokenThrottle reservations enforce the exact attempt limit",
          "[security][failed_token_throttle]") {
    FailedTokenThrottle throttle;
    Clock::time_point t0 = Clock::now();
    std::vector<std::optional<dovahlink::security::FailedTokenReservation>>
        reservations;
    reservations.reserve(5);

    for (int i = 0; i < 5; ++i) {
        auto reservation =
            throttle.TryReserve(t0 + std::chrono::seconds(i));
        REQUIRE(reservation.has_value());
        reservations.push_back(std::move(reservation));
    }

    CHECK_FALSE(throttle.TryReserve(t0 + std::chrono::seconds(5)).has_value());
    CHECK(throttle.IsBlocked(t0 + std::chrono::seconds(5)));

    for (auto& reservation : reservations) {
        reservation->Commit();
    }
    CHECK(throttle.IsBlocked(t0 + std::chrono::seconds(5)));
}

TEST_CASE("FailedTokenThrottle releases successful reservations and expires "
          "committed failures",
          "[security][failed_token_throttle]") {
    FailedTokenThrottle throttle;
    Clock::time_point t0 = Clock::now();

    {
        auto successfulAttempt = throttle.TryReserve(t0);
        REQUIRE(successfulAttempt.has_value());
        CHECK_FALSE(throttle.IsBlocked(t0));
    }
    CHECK_FALSE(throttle.IsBlocked(t0));

    auto failedAttempt = throttle.TryReserve(t0);
    REQUIRE(failedAttempt.has_value());
    failedAttempt->Commit();
    CHECK(throttle.IsBlocked(t0 + std::chrono::seconds(5)) == false);
    CHECK_FALSE(throttle.IsBlocked(t0 + std::chrono::seconds(60)));
}

TEST_CASE("FailedTokenThrottle never exceeds the reservation limit under "
          "concurrency",
          "[security][failed_token_throttle]") {
    FailedTokenThrottle throttle;
    Clock::time_point now = Clock::now();

    constexpr int kThreads = 16;
    std::atomic<int> readyCount{0};
    std::atomic<bool> go{false};
    std::atomic<int> successCount{0};
    std::vector<std::thread> threads;
    threads.reserve(kThreads);

    for (int i = 0; i < kThreads; ++i) {
        threads.emplace_back([&]() {
            readyCount.fetch_add(1, std::memory_order_relaxed);
            while (!go.load(std::memory_order_acquire)) {
            }
            auto reservation = throttle.TryReserve(now);
            if (reservation.has_value()) {
                successCount.fetch_add(1, std::memory_order_relaxed);
                reservation->Commit();
            }
        });
    }

    while (readyCount.load(std::memory_order_relaxed) < kThreads) {
    }
    go.store(true, std::memory_order_release);

    for (std::thread& thread : threads) {
        thread.join();
    }

    CHECK(successCount.load() == 5);
    CHECK(throttle.IsBlocked(now));
}
