#include "application/connection_timeout_tracker.hpp"

#include <catch2/catch_test_macros.hpp>

#include <chrono>

using dovahlink::application::ConnectionTimeoutTracker;

namespace {
/// Clock type used to make timeout assertions deterministic.
using Clock = std::chrono::steady_clock;
} // namespace

TEST_CASE("a fresh connection is not timed out immediately",
          "[application][connection_timeout_tracker]") {
  Clock::time_point t0 = Clock::now();
  ConnectionTimeoutTracker tracker(t0);
  CHECK_FALSE(tracker.IsTimedOut(t0));
}

TEST_CASE("an unauthenticated connection is not timed out just before the 5s "
          "handshake deadline",
          "[application][connection_timeout_tracker]") {
  Clock::time_point t0 = Clock::now();
  ConnectionTimeoutTracker tracker(t0);
  CHECK_FALSE(tracker.IsTimedOut(t0 + std::chrono::seconds(4)));
}

TEST_CASE(
    "an unauthenticated connection times out at the 5s handshake deadline",
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

  // Well past the old handshake deadline, but well within the new 60s idle
  // window.
  CHECK_FALSE(tracker.IsTimedOut(t0 + std::chrono::seconds(10)));
}

TEST_CASE(
    "an authenticated, idle connection times out at 60s since authentication",
    "[application][connection_timeout_tracker]") {
  Clock::time_point t0 = Clock::now();
  ConnectionTimeoutTracker tracker(t0);
  tracker.MarkAuthenticated(t0);

  CHECK_FALSE(tracker.IsTimedOut(t0 + std::chrono::seconds(59)));
  CHECK(tracker.IsTimedOut(t0 + std::chrono::seconds(60)));
}

TEST_CASE("RecordActivity resets the idle deadline forward",
          "[application][connection_timeout_tracker]") {
  Clock::time_point t0 = Clock::now();
  ConnectionTimeoutTracker tracker(t0);
  tracker.MarkAuthenticated(t0);

  tracker.RecordActivity(t0 + std::chrono::seconds(50));

  // 100s after authentication, but only 50s after the last activity: still
  // alive.
  CHECK_FALSE(tracker.IsTimedOut(t0 + std::chrono::seconds(100)));
  // 110s after authentication is 60s after the last activity: now timed out.
  CHECK(tracker.IsTimedOut(t0 + std::chrono::seconds(110)));
}

TEST_CASE("RecordActivity before authentication does not extend the handshake "
          "deadline",
          "[application][connection_timeout_tracker]") {
  Clock::time_point t0 = Clock::now();
  ConnectionTimeoutTracker tracker(t0);

  tracker.RecordActivity(t0 + std::chrono::seconds(1));

  // The handshake deadline is still 5s from connection start, unaffected.
  CHECK(tracker.IsTimedOut(t0 + std::chrono::seconds(5)));
}

TEST_CASE(
    "MarkAuthenticated cannot revive a connection past the handshake deadline",
    "[application][connection_timeout_tracker]") {
  Clock::time_point t0 = Clock::now();
  ConnectionTimeoutTracker tracker(t0);

  REQUIRE(tracker.IsTimedOut(t0 + std::chrono::seconds(5)));
  tracker.MarkAuthenticated(t0 + std::chrono::seconds(5));

  // Must still be timed out: authenticating this late does not grant a fresh
  // 60s window.
  CHECK(tracker.IsTimedOut(t0 + std::chrono::seconds(5)));
}

TEST_CASE("RecordActivity cannot revive a connection past the idle deadline",
          "[application][connection_timeout_tracker]") {
  Clock::time_point t0 = Clock::now();
  ConnectionTimeoutTracker tracker(t0);
  tracker.MarkAuthenticated(t0);

  REQUIRE(tracker.IsTimedOut(t0 + std::chrono::seconds(60)));
  tracker.RecordActivity(t0 + std::chrono::seconds(60));

  // Must still be timed out: activity this late does not push the deadline
  // forward.
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

TEST_CASE("Deadline reports the handshake deadline before authentication",
          "[application][connection_timeout_tracker]") {
  Clock::time_point t0 = Clock::now();
  ConnectionTimeoutTracker tracker(t0);

  CHECK(tracker.Deadline() == t0 + std::chrono::seconds(5));
}

TEST_CASE("Deadline reflects the idle deadline after MarkAuthenticated",
          "[application][connection_timeout_tracker]") {
  Clock::time_point t0 = Clock::now();
  ConnectionTimeoutTracker tracker(t0);

  tracker.MarkAuthenticated(t0 + std::chrono::seconds(2));

  CHECK(tracker.Deadline() ==
        t0 + std::chrono::seconds(2) + std::chrono::seconds(60));
}

TEST_CASE("Deadline reflects the most recent RecordActivity call",
          "[application][connection_timeout_tracker]") {
  Clock::time_point t0 = Clock::now();
  ConnectionTimeoutTracker tracker(t0);
  tracker.MarkAuthenticated(t0);

  tracker.RecordActivity(t0 + std::chrono::seconds(30));

  CHECK(tracker.Deadline() ==
        t0 + std::chrono::seconds(30) + std::chrono::seconds(60));
}

TEST_CASE("IsTimedOut(Deadline()) is always true, matching the inclusive '>=' "
          "boundary",
          "[application][connection_timeout_tracker]") {
  Clock::time_point t0 = Clock::now();
  ConnectionTimeoutTracker tracker(t0);
  CHECK(tracker.IsTimedOut(tracker.Deadline()));

  tracker.MarkAuthenticated(t0);
  CHECK(tracker.IsTimedOut(tracker.Deadline()));
}

TEST_CASE("Deadline stays at the stale handshake deadline when "
          "MarkAuthenticated arrives too late",
          "[application][connection_timeout_tracker]") {
  Clock::time_point t0 = Clock::now();
  ConnectionTimeoutTracker tracker(t0);
  auto handshakeDeadline = tracker.Deadline();

  tracker.MarkAuthenticated(t0 + std::chrono::seconds(5));

  // MarkAuthenticated is a no-op past the handshake deadline (already-tested
  // IsTimedOut behavior); Deadline() itself must reflect that no-op too, not
  // just IsTimedOut's answer.
  CHECK(tracker.Deadline() == handshakeDeadline);
}

TEST_CASE("Deadline stays at the stale idle deadline when RecordActivity "
          "arrives too late",
          "[application][connection_timeout_tracker]") {
  Clock::time_point t0 = Clock::now();
  ConnectionTimeoutTracker tracker(t0);
  tracker.MarkAuthenticated(t0);
  auto idleDeadline = tracker.Deadline();

  tracker.RecordActivity(t0 + std::chrono::seconds(60));

  CHECK(tracker.Deadline() == idleDeadline);
}

TEST_CASE(
    "Deadline is unaffected by RecordActivity called before authentication",
    "[application][connection_timeout_tracker]") {
  Clock::time_point t0 = Clock::now();
  ConnectionTimeoutTracker tracker(t0);
  auto handshakeDeadline = tracker.Deadline();

  tracker.RecordActivity(t0 + std::chrono::seconds(1));

  CHECK(tracker.Deadline() == handshakeDeadline);
}

TEST_CASE("MarkAuthenticated is idempotent and does not push the idle deadline "
          "back further",
          "[application][connection_timeout_tracker]") {
  Clock::time_point t0 = Clock::now();
  ConnectionTimeoutTracker tracker(t0);

  tracker.MarkAuthenticated(t0 +
                            std::chrono::seconds(1)); // idle deadline: t0+61s
  tracker.MarkAuthenticated(t0 + std::chrono::seconds(50)); // must be a no-op

  // If the second call had reset the deadline, this would still be alive
  // (t0+50+60=t0+110s). Since it is a no-op, the original t0+61s deadline
  // governs.
  CHECK(tracker.IsTimedOut(t0 + std::chrono::seconds(61)));
  CHECK(tracker.Deadline() == t0 + std::chrono::seconds(61));
}
