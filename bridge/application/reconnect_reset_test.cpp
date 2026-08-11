// This step is deliberately test-only: it proves the emergent "reconnect ==
// fresh session == fresh everything" property required by
// protocol/schema/README.md ("A reconnect creates a new session... a fresh
// snapshot establishes each new baseline") and
// ai/context/skse/architecture.md, using the per-session components already
// built and unit-tested individually in Steps 15, 16, 19, and 21
// (SessionManager, ReplayGuard, EventCoalescer/OutboundQueue,
// RevisionTracker). No new production code is introduced here: each of
// those types is already documented as "one instance per session," so
// reconnect reset is achieved by constructing fresh instances for the new
// connection, not by any explicit reset method. This test is the seam that
// would catch a future refactor accidentally sharing state across sessions.

#include "application/event_coalescer.hpp"
#include "application/outbound_queue.hpp"
#include "application/replay_guard.hpp"
#include "application/revision_tracker.hpp"
#include "application/session.hpp"

#include <catch2/catch_test_macros.hpp>

#include <string>

using dovahlink::application::ConnectionId;
using dovahlink::application::EventCoalescer;
using dovahlink::application::MessageIdCheckResult;
using dovahlink::application::OutboundQueue;
using dovahlink::application::ReplayGuard;
using dovahlink::application::RevisionTracker;
using dovahlink::application::SessionManager;

namespace {
constexpr ConnectionId kConnectionA = 1;
constexpr ConnectionId kConnectionB = 2;
const std::string kCharacter = "character";
}  // namespace

TEST_CASE("a reconnect after disconnect frees the one-connected-client slot for a new session",
          "[application][reconnect_reset]") {
    SessionManager sessions;
    REQUIRE(sessions.TryCreateSession(kConnectionA, "session-1"));

    // Connection A disconnects (timeout, protocol violation, or a clean close).
    sessions.InvalidateSession(kConnectionA);

    // Connection B's reconnect claims a brand new session, not a resumption of A's.
    REQUIRE(sessions.TryCreateSession(kConnectionB, "session-2"));
    CHECK(sessions.IsValidForConnection("session-2", kConnectionB));
    CHECK_FALSE(sessions.IsValidForConnection("session-1", kConnectionA));
}

TEST_CASE("a new session's RevisionTracker starts a fresh baseline, not a continuation",
          "[application][reconnect_reset]") {
    RevisionTracker sessionA;
    sessionA.StartSnapshot(kCharacter);
    sessionA.NextEvent(kCharacter);
    sessionA.NextEvent(kCharacter);
    REQUIRE(sessionA.CurrentRevision(kCharacter) == 3);

    // The reconnect owns an entirely new tracker instance -- this is what "reset"
    // means here: there is no shared state for a new session to inherit from.
    RevisionTracker sessionB;
    CHECK_FALSE(sessionB.CurrentRevision(kCharacter).has_value());
    CHECK(sessionB.StartSnapshot(kCharacter) == 1);
}

TEST_CASE("a new session's ReplayGuard has no memory of the previous session's messageIds",
          "[application][reconnect_reset]") {
    ReplayGuard sessionA;
    REQUIRE(sessionA.RecordMessage("message-1") == MessageIdCheckResult::kAccepted);
    REQUIRE(sessionA.RecordMessage("message-1") == MessageIdCheckResult::kReplayed);

    // A fresh session's guard does not inherit A's seen-ID set, so the same
    // messageId presented on a new connection's own session is not a replay.
    ReplayGuard sessionB;
    CHECK(sessionB.RecordMessage("message-1") == MessageIdCheckResult::kAccepted);
}

TEST_CASE("a new session's outbound queue and coalescer start empty regardless of the prior session's state",
          "[application][reconnect_reset]") {
    OutboundQueue queueA;
    EventCoalescer coalescerA(queueA);
    coalescerA.PublishEvent(kCharacter, "stale-event-from-session-a");
    coalescerA.Flush();
    REQUIRE(queueA.EnqueueControl("stale-control-from-session-a") ==
            dovahlink::application::EnqueueResult::kEnqueued);
    REQUIRE(queueA.EventLaneSize() == 1);
    REQUIRE(queueA.ControlLaneSize() == 1);

    OutboundQueue queueB;
    EventCoalescer coalescerB(queueB);
    CHECK(queueB.EventLaneSize() == 0);
    CHECK(queueB.ControlLaneSize() == 0);
    CHECK(coalescerB.PendingCount() == 0);
    CHECK_FALSE(coalescerB.NeedsRecovery(kCharacter));
}

TEST_CASE("the same connection reconnecting after invalidation still gets an entirely fresh session",
          "[application][reconnect_reset]") {
    // A real ConnectionId could plausibly be reused (e.g. a recycled counter or
    // socket handle); this proves reuse of the identifier itself never resumes
    // the old session or its per-session state.
    SessionManager sessions;
    REQUIRE(sessions.TryCreateSession(kConnectionA, "session-1"));
    RevisionTracker revisionsA;
    revisionsA.StartSnapshot(kCharacter);
    revisionsA.NextEvent(kCharacter);
    REQUIRE(revisionsA.CurrentRevision(kCharacter) == 2);

    sessions.InvalidateSession(kConnectionA);

    // kConnectionA itself reconnects, not a different connection.
    REQUIRE(sessions.TryCreateSession(kConnectionA, "session-2"));
    CHECK(sessions.IsValidForConnection("session-2", kConnectionA));
    CHECK_FALSE(sessions.IsValidForConnection("session-1", kConnectionA));

    RevisionTracker revisionsReconnected;
    CHECK(revisionsReconnected.StartSnapshot(kCharacter) == 1);
}

TEST_CASE("the full reconnect flow establishes an independent session end to end",
          "[application][reconnect_reset]") {
    SessionManager sessions;
    REQUIRE(sessions.TryCreateSession(kConnectionA, "session-1"));
    RevisionTracker revisionsA;
    ReplayGuard replayA;
    revisionsA.StartSnapshot(kCharacter);
    revisionsA.NextEvent(kCharacter);
    replayA.RecordMessage("message-1");

    // Connection A is gone; its session is invalidated and its per-session state
    // (owned by whatever constructed it, e.g. the coordinator) is simply dropped.
    sessions.InvalidateSession(kConnectionA);

    // Connection B reconnects with entirely fresh per-session state.
    REQUIRE(sessions.TryCreateSession(kConnectionB, "session-2"));
    RevisionTracker revisionsB;
    ReplayGuard replayB;

    CHECK(revisionsB.StartSnapshot(kCharacter) == 1);
    CHECK(replayB.RecordMessage("message-1") == MessageIdCheckResult::kAccepted);
    CHECK_FALSE(sessions.IsValidForConnection("session-1", kConnectionA));
}
