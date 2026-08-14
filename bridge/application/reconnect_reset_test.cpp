// This step is deliberately test-only: it proves the emergent "reconnect ==
// fresh session == fresh everything" property required by
// protocol/schema/README.md ("A reconnect creates a new session... a fresh
// snapshot establishes each new baseline") and
// ai/context/skse/architecture.md, using the per-session SessionManager,
// ReplayGuard, and RevisionTracker components. Reconnect reset is achieved by
// constructing fresh instances for the new connection, not by an explicit
// reset method. These tests catch a future refactor that accidentally shares
// state across sessions.

#include "application/replay_guard.hpp"
#include "application/revision_tracker.hpp"
#include "application/session.hpp"

#include <catch2/catch_test_macros.hpp>

#include <string>

using dovahlink::application::ConnectionId;
using dovahlink::application::MessageIdCheckResult;
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
    auto sessionA = sessions.TryCreateSession(kConnectionA, "session-1");
    REQUIRE(sessionA.has_value());

    // Connection A disconnects (timeout, protocol violation, or a clean close).
    sessionA.reset();

    // Connection B's reconnect claims a brand new session, not a resumption of A's.
    auto sessionB = sessions.TryCreateSession(kConnectionB, "session-2");
    REQUIRE(sessionB.has_value());
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

TEST_CASE("the same connection reconnecting after invalidation still gets an entirely fresh session",
          "[application][reconnect_reset]") {
    // A real ConnectionId could plausibly be reused (e.g. a recycled counter or
    // socket handle); this proves reuse of the identifier itself never resumes
    // the old session or its per-session state.
    SessionManager sessions;
    auto sessionA = sessions.TryCreateSession(kConnectionA, "session-1");
    REQUIRE(sessionA.has_value());
    RevisionTracker revisionsA;
    revisionsA.StartSnapshot(kCharacter);
    revisionsA.NextEvent(kCharacter);
    REQUIRE(revisionsA.CurrentRevision(kCharacter) == 2);

    sessionA.reset();

    // kConnectionA itself reconnects, not a different connection.
    auto reconnectedSession = sessions.TryCreateSession(kConnectionA, "session-2");
    REQUIRE(reconnectedSession.has_value());
    CHECK(sessions.IsValidForConnection("session-2", kConnectionA));
    CHECK_FALSE(sessions.IsValidForConnection("session-1", kConnectionA));

    RevisionTracker revisionsReconnected;
    CHECK(revisionsReconnected.StartSnapshot(kCharacter) == 1);
}

TEST_CASE("the full reconnect flow establishes an independent session end to end",
          "[application][reconnect_reset]") {
    SessionManager sessions;
    auto sessionA = sessions.TryCreateSession(kConnectionA, "session-1");
    REQUIRE(sessionA.has_value());
    RevisionTracker revisionsA;
    ReplayGuard replayA;
    revisionsA.StartSnapshot(kCharacter);
    revisionsA.NextEvent(kCharacter);
    replayA.RecordMessage("message-1");

    // Connection A is gone; its session is invalidated and its per-session state
    // (owned by whatever constructed it, e.g. the coordinator) is simply dropped.
    sessionA.reset();

    // Connection B reconnects with entirely fresh per-session state.
    auto sessionB = sessions.TryCreateSession(kConnectionB, "session-2");
    REQUIRE(sessionB.has_value());
    RevisionTracker revisionsB;
    ReplayGuard replayB;

    CHECK(revisionsB.StartSnapshot(kCharacter) == 1);
    CHECK(replayB.RecordMessage("message-1") == MessageIdCheckResult::kAccepted);
    CHECK_FALSE(sessions.IsValidForConnection("session-1", kConnectionA));
}
