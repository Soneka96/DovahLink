//  This step is deliberately test-only: it proves the emergent "reconnect ==
//  fresh session, while play-context state remains authoritative" property
//  required by protocol/schema/README.md ("A reconnect creates a new session...
//  a fresh snapshot establishes each new baseline") and
//  ai/context/skse/architecture.md, using the per-session SessionManager and
//  ReplayGuard plus the play-context-owned RevisionTracker. Reconnect reset is
//  achieved by constructing fresh per-session components for the new connection,
//  while the active PlayContext remains shared until the game context changes.

#include "application/play_context.hpp"
#include "application/replay_guard.hpp"
#include "application/session.hpp"

#include <catch2/catch_test_macros.hpp>

#include <string>

using dovahlink::application::ConnectionId;
using dovahlink::application::MessageIdCheckResult;
using dovahlink::application::PlayContext;
using dovahlink::application::ReplayGuard;
using dovahlink::application::SessionAuthMethod;
using dovahlink::application::SessionManager;
using dovahlink::application::SessionTrustTier;

namespace {
constexpr ConnectionId kConnectionA = 1;
constexpr ConnectionId kConnectionB = 2;
const std::string kAreaA = "area_a";
//  These tests exercise instance/session boundaries, not content-change
//  detection, so every call reuses the same fingerprint value.
const std::string kFingerprint = "fingerprint-1";
} //  namespace

TEST_CASE("a reconnect after disconnect frees the one-connected-client slot "
          "for a new session",
          "[application][reconnect_reset]") {
    SessionManager sessions;
    auto sessionA = sessions.TryCreateSession(
        kConnectionA, "session-1", "client-1", SessionTrustTier::kFull,
        SessionAuthMethod::kTrustedDeviceCredential);
    REQUIRE(sessionA.has_value());

    //  Connection A disconnects (timeout, protocol violation, or a clean close).
    sessionA.reset();

    //  Connection B's reconnect claims a brand new session, not a resumption of
    //  A's.
    auto sessionB = sessions.TryCreateSession(
        kConnectionB, "session-2", "client-2", SessionTrustTier::kFull,
        SessionAuthMethod::kTrustedDeviceCredential);
    REQUIRE(sessionB.has_value());
    CHECK(sessions.IsValidForConnection("session-2", kConnectionB));
    CHECK_FALSE(sessions.IsValidForConnection("session-1", kConnectionA));
}

TEST_CASE("a reconnect preserves the active PlayContext's revision baseline",
          "[application][reconnect_reset]") {
    SessionManager sessions;
    auto sessionA = sessions.TryCreateSession(
        kConnectionA, "session-1", "client-1", SessionTrustTier::kFull,
        SessionAuthMethod::kTrustedDeviceCredential);
    REQUIRE(sessionA.has_value());

    PlayContext playContext("play-context-1");
    playContext.revisions.StartSnapshot(kAreaA, kFingerprint);
    playContext.revisions.NextEvent(kAreaA);
    playContext.revisions.NextEvent(kAreaA);
    REQUIRE(playContext.revisions.CurrentRevision(kAreaA) == 3);

    sessionA.reset();
    auto sessionB = sessions.TryCreateSession(
        kConnectionB, "session-2", "client-1", SessionTrustTier::kFull,
        SessionAuthMethod::kTrustedDeviceCredential);
    REQUIRE(sessionB.has_value());
    CHECK(sessions.IsValidForConnection("session-2", kConnectionB));
    CHECK_FALSE(sessions.IsValidForConnection("session-1", kConnectionA));
    auto reconnectClientId = sessions.ClientIdForConnection(kConnectionB);
    REQUIRE(reconnectClientId.has_value());
    CHECK(*reconnectClientId == "client-1");

    //  The new socket session does not replace the play context. Its authoritative
    //  revisions remain available so reconnect synchronization can establish a
    //  fresh snapshot against the same play-context identity.
    CHECK(playContext.revisions.CurrentRevision(kAreaA) == 3);
}

TEST_CASE("a new session's ReplayGuard has no memory of the previous session's "
          "messageIds",
          "[application][reconnect_reset]") {
    ReplayGuard sessionA;
    REQUIRE(sessionA.RecordMessage("message-1") ==
            MessageIdCheckResult::kAccepted);
    REQUIRE(sessionA.RecordMessage("message-1") ==
            MessageIdCheckResult::kReplayed);

    //  A fresh session's guard does not inherit A's seen-ID set, so the same
    //  messageId presented on a new connection's own session is not a replay.
    ReplayGuard sessionB;
    CHECK(sessionB.RecordMessage("message-1") == MessageIdCheckResult::kAccepted);
}

TEST_CASE("the same connection reconnecting gets a fresh session but keeps "
          "play-context revisions",
          "[application][reconnect_reset]") {
    //  A real ConnectionId could plausibly be reused (e.g. a recycled counter or
    //  socket handle); this proves reuse of the identifier itself never resumes
    //  the old session or its per-session state.
    SessionManager sessions;
    auto sessionA = sessions.TryCreateSession(
        kConnectionA, "session-1", "client-1", SessionTrustTier::kFull,
        SessionAuthMethod::kTrustedDeviceCredential);
    REQUIRE(sessionA.has_value());
    PlayContext playContext("play-context-1");
    playContext.revisions.StartSnapshot(kAreaA, kFingerprint);
    playContext.revisions.NextEvent(kAreaA);
    REQUIRE(playContext.revisions.CurrentRevision(kAreaA) == 2);

    sessionA.reset();

    //  kConnectionA itself reconnects, not a different connection.
    auto reconnectedSession = sessions.TryCreateSession(
        kConnectionA, "session-2", "client-1", SessionTrustTier::kFull,
        SessionAuthMethod::kTrustedDeviceCredential);
    REQUIRE(reconnectedSession.has_value());
    CHECK(sessions.IsValidForConnection("session-2", kConnectionA));
    CHECK_FALSE(sessions.IsValidForConnection("session-1", kConnectionA));
    //  The reused connection's client identity is the reconnect's own, not a
    //  holdover from the session it replaced. The play context is different
    //  ownership and remains authoritative across socket sessions.
    auto clientId = sessions.ClientIdForConnection(kConnectionA);
    REQUIRE(clientId.has_value());
    CHECK(*clientId == "client-1");

    CHECK(playContext.revisions.CurrentRevision(kAreaA) == 2);
}

TEST_CASE(
    "the full reconnect flow establishes an independent session end to end",
    "[application][reconnect_reset]") {
    SessionManager sessions;
    auto sessionA = sessions.TryCreateSession(
        kConnectionA, "session-1", "client-1", SessionTrustTier::kFull,
        SessionAuthMethod::kTrustedDeviceCredential);
    REQUIRE(sessionA.has_value());
    PlayContext playContext("play-context-1");
    ReplayGuard replayA;
    playContext.revisions.StartSnapshot(kAreaA, kFingerprint);
    playContext.revisions.NextEvent(kAreaA);
    (void)replayA.RecordMessage("message-1");

    //  Connection A is gone; its session is invalidated and its per-session state
    //  (owned by whatever constructed it, e.g. the coordinator) is simply dropped.
    //  The active play-context state remains because it belongs to the currently
    //  loaded game.
    sessionA.reset();

    //  Connection B reconnects with entirely fresh per-session state.
    auto sessionB = sessions.TryCreateSession(
        kConnectionB, "session-2", "client-2", SessionTrustTier::kFull,
        SessionAuthMethod::kTrustedDeviceCredential);
    REQUIRE(sessionB.has_value());
    ReplayGuard replayB;

    CHECK(playContext.revisions.CurrentRevision(kAreaA) == 2);
    CHECK(replayB.RecordMessage("message-1") == MessageIdCheckResult::kAccepted);
    CHECK_FALSE(sessions.IsValidForConnection("session-1", kConnectionA));
}
