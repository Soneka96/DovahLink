#include "security/failed_token_throttle.hpp"

#include <catch2/catch_test_macros.hpp>

#include <chrono>

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
