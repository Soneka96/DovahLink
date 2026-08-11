#include "application/connection_timeout_tracker.hpp"

#include <catch2/catch_test_macros.hpp>

#include <chrono>

using dovahlink::application::ConnectionTimeoutTracker;

namespace {
using Clock = std::chrono::steady_clock;
}  // namespace

TEST_CASE("a fresh connection is not timed out immediately", "[application][connection_timeout_tracker]") {
    Clock::time_point t0 = Clock::now();
    ConnectionTimeoutTracker tracker(t0);
    CHECK_FALSE(tracker.IsTimedOut(t0));
}

TEST_CASE("an unauthenticated connection is not timed out just before the 5s handshake deadline",
          "[application][connection_timeout_tracker]") {
    Clock::time_point t0 = Clock::now();
    ConnectionTimeoutTracker tracker(t0);
    CHECK_FALSE(tracker.IsTimedOut(t0 + std::chrono::seconds(4)));
}

TEST_CASE("an unauthenticated connection times out at the 5s handshake deadline",
          "[application][connection_timeout_tracker]") {
    Clock::time_point t0 = Clock::now();
    ConnectionTimeoutTracker tracker(t0);
    CHECK(tracker.IsTimedOut(t0 + std::chrono::seconds(5)));
}

TEST_CASE("MarkAuthenticated switches tracking to the 60s idle timeout",
          "[application][connection_timeout_tracker]") {
    Clock::time_point t0 = Clock::now();
    ConnectionTimeoutTracker tracker(t0);

    // Authenticate just before the 5s handshake deadline would have fired.
    tracker.MarkAuthenticated(t0 + std::chrono::seconds(4));

    // Well past the old handshake deadline, but well within the new 60s idle window.
    CHECK_FALSE(tracker.IsTimedOut(t0 + std::chrono::seconds(10)));
}

TEST_CASE("an authenticated, idle connection times out at 60s since authentication",
          "[application][connection_timeout_tracker]") {
    Clock::time_point t0 = Clock::now();
    ConnectionTimeoutTracker tracker(t0);
    tracker.MarkAuthenticated(t0);

    CHECK_FALSE(tracker.IsTimedOut(t0 + std::chrono::seconds(59)));
    CHECK(tracker.IsTimedOut(t0 + std::chrono::seconds(60)));
}

TEST_CASE("RecordActivity resets the idle deadline forward", "[application][connection_timeout_tracker]") {
    Clock::time_point t0 = Clock::now();
    ConnectionTimeoutTracker tracker(t0);
    tracker.MarkAuthenticated(t0);

    tracker.RecordActivity(t0 + std::chrono::seconds(50));

    // 100s after authentication, but only 50s after the last activity: still alive.
    CHECK_FALSE(tracker.IsTimedOut(t0 + std::chrono::seconds(100)));
    // 110s after authentication is 60s after the last activity: now timed out.
    CHECK(tracker.IsTimedOut(t0 + std::chrono::seconds(110)));
}

TEST_CASE("RecordActivity before authentication does not extend the handshake deadline",
          "[application][connection_timeout_tracker]") {
    Clock::time_point t0 = Clock::now();
    ConnectionTimeoutTracker tracker(t0);

    tracker.RecordActivity(t0 + std::chrono::seconds(1));

    // The handshake deadline is still 5s from connection start, unaffected.
    CHECK(tracker.IsTimedOut(t0 + std::chrono::seconds(5)));
}

TEST_CASE("MarkAuthenticated cannot revive a connection past the handshake deadline",
          "[application][connection_timeout_tracker]") {
    Clock::time_point t0 = Clock::now();
    ConnectionTimeoutTracker tracker(t0);

    REQUIRE(tracker.IsTimedOut(t0 + std::chrono::seconds(5)));
    tracker.MarkAuthenticated(t0 + std::chrono::seconds(5));

    // Must still be timed out: authenticating this late does not grant a fresh 60s window.
    CHECK(tracker.IsTimedOut(t0 + std::chrono::seconds(5)));
}

TEST_CASE("RecordActivity cannot revive a connection past the idle deadline",
          "[application][connection_timeout_tracker]") {
    Clock::time_point t0 = Clock::now();
    ConnectionTimeoutTracker tracker(t0);
    tracker.MarkAuthenticated(t0);

    REQUIRE(tracker.IsTimedOut(t0 + std::chrono::seconds(60)));
    tracker.RecordActivity(t0 + std::chrono::seconds(60));

    // Must still be timed out: activity this late does not push the deadline forward.
    CHECK(tracker.IsTimedOut(t0 + std::chrono::seconds(60)));
}

TEST_CASE("RecordActivity resets the deadline correctly across repeated calls",
          "[application][connection_timeout_tracker]") {
    Clock::time_point t0 = Clock::now();
    ConnectionTimeoutTracker tracker(t0);
    tracker.MarkAuthenticated(t0);

    tracker.RecordActivity(t0 + std::chrono::seconds(30));
    tracker.RecordActivity(t0 + std::chrono::seconds(60));

    CHECK_FALSE(tracker.IsTimedOut(t0 + std::chrono::seconds(119)));
    CHECK(tracker.IsTimedOut(t0 + std::chrono::seconds(120)));
}

TEST_CASE("MarkAuthenticated is idempotent and does not push the idle deadline back further",
          "[application][connection_timeout_tracker]") {
    Clock::time_point t0 = Clock::now();
    ConnectionTimeoutTracker tracker(t0);

    tracker.MarkAuthenticated(t0 + std::chrono::seconds(1));  // idle deadline: t0+61s
    tracker.MarkAuthenticated(t0 + std::chrono::seconds(50));  // must be a no-op

    // If the second call had reset the deadline, this would still be alive (t0+50+60=t0+110s).
    // Since it is a no-op, the original t0+61s deadline governs.
    CHECK(tracker.IsTimedOut(t0 + std::chrono::seconds(61)));
}
