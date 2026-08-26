#include "security/violation_tracker.hpp"

#include <catch2/catch_test_macros.hpp>

#include <chrono>

using dovahlink::security::ViolationTracker;

namespace {
///  Clock type used for deterministic sliding-window assertions.
using Clock = std::chrono::steady_clock;
} //  namespace

TEST_CASE("ViolationTracker does not close the connection for the first two "
          "violations",
          "[security][violation_tracker]") {
    ViolationTracker tracker;
    Clock::time_point t0 = Clock::now();

    CHECK_FALSE(tracker.RecordViolationAndCheckLimit(t0));
    CHECK_FALSE(
        tracker.RecordViolationAndCheckLimit(t0 + std::chrono::seconds(1)));
}

TEST_CASE("ViolationTracker closes the connection on the 3rd violation within "
          "the window",
          "[security][violation_tracker]") {
    ViolationTracker tracker;
    Clock::time_point t0 = Clock::now();

    REQUIRE_FALSE(tracker.RecordViolationAndCheckLimit(t0));
    REQUIRE_FALSE(
        tracker.RecordViolationAndCheckLimit(t0 + std::chrono::seconds(1)));
    CHECK(tracker.RecordViolationAndCheckLimit(t0 + std::chrono::seconds(2)));
}

TEST_CASE(
    "ViolationTracker does not close the connection once the window has passed",
    "[security][violation_tracker]") {
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
