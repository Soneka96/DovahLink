#include "application/session.hpp"

#include <catch2/catch_test_macros.hpp>

#include <atomic>
#include <optional>
#include <string>
#include <thread>
#include <type_traits>
#include <utility>
#include <vector>

using dovahlink::application::ConnectionId;
using dovahlink::application::SessionAuthMethod;
using dovahlink::application::SessionManager;
using dovahlink::application::SessionTrustTier;

static_assert(!std::is_copy_constructible_v<SessionManager::Lease>);
static_assert(!std::is_copy_assignable_v<SessionManager::Lease>);
static_assert(std::is_nothrow_move_constructible_v<SessionManager::Lease>);
static_assert(std::is_nothrow_move_assignable_v<SessionManager::Lease>);

namespace {
constexpr ConnectionId kConnectionA = 1;
constexpr ConnectionId kConnectionB = 2;
const std::string kSessionOne = "session-1";
const std::string kSessionTwo = "session-2";
const std::string kClientOne = "client-1";
const std::string kClientTwo = "client-2";
}  // namespace

TEST_CASE("TryCreateSession succeeds when no session is active", "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne);
    REQUIRE(lease.has_value());
    CHECK(sessions.IsValidForConnection(kSessionOne, kConnectionA));
}

TEST_CASE("TryCreateSession fails while a session is already active, even for a different connection",
          "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne);
    REQUIRE(lease.has_value());
    CHECK_FALSE(sessions.TryCreateSession(kConnectionB, kSessionTwo, kClientTwo).has_value());
}

TEST_CASE("TryCreateSession fails even when re-called by the connection that already owns the session",
          "[application][session]") {
    // A connection gets exactly one session for its lifetime; a second call is caller
    // misuse, not a refresh, and must not silently succeed or replace the existing ID.
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne);
    REQUIRE(lease.has_value());
    CHECK_FALSE(sessions.TryCreateSession(kConnectionA, kSessionTwo, kClientTwo).has_value());
    CHECK(sessions.IsValidForConnection(kSessionOne, kConnectionA));
}

TEST_CASE("IsValidForConnection is true for the owning connection and correct session ID",
          "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne);
    REQUIRE(lease.has_value());
    CHECK(sessions.IsValidForConnection(kSessionOne, kConnectionA));
}

TEST_CASE("IsValidForConnection is false for a foreign connection presenting the active session ID",
          "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne);
    REQUIRE(lease.has_value());
    // kConnectionB never authenticated, but presents kConnectionA's real session ID.
    CHECK_FALSE(sessions.IsValidForConnection(kSessionOne, kConnectionB));
}

TEST_CASE("IsValidForConnection is false for an unrecognized session ID from the owning connection",
          "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne);
    REQUIRE(lease.has_value());
    CHECK_FALSE(sessions.IsValidForConnection("not-the-real-session-id", kConnectionA));
}

TEST_CASE("IsValidForConnection is false when no session is active", "[application][session]") {
    SessionManager sessions;
    CHECK_FALSE(sessions.IsValidForConnection(kSessionOne, kConnectionA));
}

TEST_CASE("destroying a lease invalidates its session", "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne);
    REQUIRE(lease.has_value());
    lease.reset();
    CHECK_FALSE(sessions.IsValidForConnection(kSessionOne, kConnectionA));
}

TEST_CASE("the same connection can create a fresh session after its prior one is invalidated",
          "[application][session]") {
    SessionManager sessions;
    auto firstLease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne);
    REQUIRE(firstLease.has_value());
    firstLease.reset();

    auto secondLease = sessions.TryCreateSession(kConnectionA, kSessionTwo, kClientTwo);
    REQUIRE(secondLease.has_value());
    CHECK(sessions.IsValidForConnection(kSessionTwo, kConnectionA));
    CHECK_FALSE(sessions.IsValidForConnection(kSessionOne, kConnectionA));
    // The same connection's new session reports its own new client identity,
    // not the prior session's -- proves no leakage across a same-connection
    // session replacement, not just across different connections.
    auto clientId = sessions.ClientIdForConnection(kConnectionA);
    REQUIRE(clientId.has_value());
    CHECK(*clientId == kClientTwo);
}

TEST_CASE("a reconnect can create a fresh session after the prior one is invalidated",
          "[application][session]") {
    SessionManager sessions;
    auto firstLease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne);
    REQUIRE(firstLease.has_value());
    firstLease.reset();

    // A different connection (the reconnect) can now claim the freed one-client slot.
    auto secondLease = sessions.TryCreateSession(kConnectionB, kSessionTwo, kClientTwo);
    REQUIRE(secondLease.has_value());
    CHECK(sessions.IsValidForConnection(kSessionTwo, kConnectionB));
    // The old session ID is no longer valid for anyone, including its original connection.
    CHECK_FALSE(sessions.IsValidForConnection(kSessionOne, kConnectionA));
}

TEST_CASE("InvalidateAll clears the active session regardless of which connection holds it",
          "[application][session]") {
    SessionManager sessions;
    auto firstLease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne);
    REQUIRE(firstLease.has_value());
    sessions.InvalidateAll();
    CHECK_FALSE(sessions.IsValidForConnection(kSessionOne, kConnectionA));
    auto secondLease = sessions.TryCreateSession(kConnectionB, kSessionTwo, kClientTwo);
    REQUIRE(secondLease.has_value());
}

TEST_CASE("InvalidateAll is safe to call when no session is active", "[application][session]") {
    SessionManager sessions;
    sessions.InvalidateAll();
    CHECK(sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne).has_value());
}

TEST_CASE("a stale lease cannot invalidate a replacement session on the same connection",
          "[application][session]") {
    SessionManager sessions;
    auto staleLease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne);
    REQUIRE(staleLease.has_value());
    sessions.InvalidateAll();

    auto replacementLease = sessions.TryCreateSession(kConnectionA, kSessionTwo, kClientTwo);
    REQUIRE(replacementLease.has_value());
    staleLease.reset();

    CHECK(sessions.IsValidForConnection(kSessionTwo, kConnectionA));
    // The stale lease's destructor must not have wiped the replacement
    // session's client identity along with (what it wrongly believed was
    // still) its own session state.
    auto clientId = sessions.ClientIdForConnection(kConnectionA);
    REQUIRE(clientId.has_value());
    CHECK(*clientId == kClientTwo);
}

TEST_CASE("moving a lease transfers session ownership", "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne);
    REQUIRE(lease.has_value());

    std::optional<SessionManager::Lease> moved{std::move(*lease)};
    lease.reset();
    CHECK(sessions.IsValidForConnection(kSessionOne, kConnectionA));

    moved.reset();
    CHECK_FALSE(sessions.IsValidForConnection(kSessionOne, kConnectionA));
}

TEST_CASE("move-assigning a lease invalidates its previously owned session", "[application][session]") {
    SessionManager firstSessions;
    SessionManager secondSessions;
    auto firstLease = firstSessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne);
    auto secondLease = secondSessions.TryCreateSession(kConnectionB, kSessionTwo, kClientTwo);
    REQUIRE(firstLease.has_value());
    REQUIRE(secondLease.has_value());

    *firstLease = std::move(*secondLease);

    CHECK_FALSE(firstSessions.IsValidForConnection(kSessionOne, kConnectionA));
    CHECK(secondSessions.IsValidForConnection(kSessionTwo, kConnectionB));
    secondLease.reset();
    CHECK(secondSessions.IsValidForConnection(kSessionTwo, kConnectionB));
    firstLease.reset();
    CHECK_FALSE(secondSessions.IsValidForConnection(kSessionTwo, kConnectionB));
}

TEST_CASE("ClientIdForConnection returns the authenticated client for the owning connection",
          "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne);
    REQUIRE(lease.has_value());

    auto clientId = sessions.ClientIdForConnection(kConnectionA);
    REQUIRE(clientId.has_value());
    CHECK(*clientId == kClientOne);
}

TEST_CASE("ClientIdForConnection returns no value when no session is active",
          "[application][session]") {
    SessionManager sessions;
    CHECK_FALSE(sessions.ClientIdForConnection(kConnectionA).has_value());
}

TEST_CASE("ClientIdForConnection returns no value for a connection that does not own the active session",
          "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne);
    REQUIRE(lease.has_value());

    // kConnectionB never authenticated; it must not resolve kConnectionA's client identity.
    CHECK_FALSE(sessions.ClientIdForConnection(kConnectionB).has_value());
}

TEST_CASE("ClientIdForConnection is cleared once the owning lease is released",
          "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne);
    REQUIRE(lease.has_value());
    lease.reset();

    CHECK_FALSE(sessions.ClientIdForConnection(kConnectionA).has_value());
}

TEST_CASE("ClientIdForConnection is cleared by InvalidateAll", "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne);
    REQUIRE(lease.has_value());
    sessions.InvalidateAll();

    CHECK_FALSE(sessions.ClientIdForConnection(kConnectionA).has_value());
}

TEST_CASE("ClientIdForConnection reports the new session's client after a reconnect replaces the old one",
          "[application][session]") {
    SessionManager sessions;
    auto firstLease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne);
    REQUIRE(firstLease.has_value());
    firstLease.reset();

    auto secondLease = sessions.TryCreateSession(kConnectionB, kSessionTwo, kClientTwo);
    REQUIRE(secondLease.has_value());

    auto clientId = sessions.ClientIdForConnection(kConnectionB);
    REQUIRE(clientId.has_value());
    CHECK(*clientId == kClientTwo);
    CHECK_FALSE(sessions.ClientIdForConnection(kConnectionA).has_value());
}

TEST_CASE("TryCreateSession defaults to kFull trust for callers that predate trust tiers",
          "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne);
    REQUIRE(lease.has_value());
    CHECK(sessions.IsFullyTrusted(kConnectionA));
}

TEST_CASE("TryCreateSession honors an explicit kRestricted trust tier", "[application][session]") {
    SessionManager sessions;
    auto lease =
        sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne, SessionTrustTier::kRestricted);
    REQUIRE(lease.has_value());
    CHECK_FALSE(sessions.IsFullyTrusted(kConnectionA));
}

TEST_CASE("UpgradeToFullTrust flips the active session's owning connection to kFull",
          "[application][session]") {
    SessionManager sessions;
    auto lease =
        sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne, SessionTrustTier::kRestricted);
    REQUIRE(lease.has_value());

    sessions.UpgradeToFullTrust(kConnectionA, kSessionOne);

    CHECK(sessions.IsFullyTrusted(kConnectionA));
}

TEST_CASE("UpgradeToFullTrust is a no-op for a connection that does not own the active session",
          "[application][session]") {
    SessionManager sessions;
    auto lease =
        sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne, SessionTrustTier::kRestricted);
    REQUIRE(lease.has_value());

    sessions.UpgradeToFullTrust(kConnectionB, kSessionOne);

    CHECK_FALSE(sessions.IsFullyTrusted(kConnectionA));
}

TEST_CASE("UpgradeToFullTrust is a no-op for the right connection presenting the wrong sessionId",
          "[application][session]") {
    // Distinct from the wrong-connection case: this is the same guard InvalidateSession already
    // has, proven here because UpgradeToFullTrust must check both, not just connection.
    SessionManager sessions;
    auto lease =
        sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne, SessionTrustTier::kRestricted);
    REQUIRE(lease.has_value());

    sessions.UpgradeToFullTrust(kConnectionA, kSessionTwo);

    CHECK_FALSE(sessions.IsFullyTrusted(kConnectionA));
}

TEST_CASE("UpgradeToFullTrust is a no-op when no session is active at all",
          "[application][session]") {
    SessionManager sessions;

    sessions.UpgradeToFullTrust(kConnectionA, kSessionOne);

    CHECK_FALSE(sessions.IsFullyTrusted(kConnectionA));
}

TEST_CASE("a stale UpgradeToFullTrust cannot promote a replacement session on the same connection",
          "[application][session]") {
    // The exact scenario the sessionId check exists to prevent: a delayed upgrade call for the
    // original (now-invalidated) session arrives after the same connection already holds an
    // unrelated replacement session.
    SessionManager sessions;
    auto staleLease =
        sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne, SessionTrustTier::kRestricted);
    REQUIRE(staleLease.has_value());
    staleLease.reset();

    auto replacementLease =
        sessions.TryCreateSession(kConnectionA, kSessionTwo, kClientTwo, SessionTrustTier::kRestricted);
    REQUIRE(replacementLease.has_value());

    sessions.UpgradeToFullTrust(kConnectionA, kSessionOne);

    CHECK_FALSE(sessions.IsFullyTrusted(kConnectionA));
}

TEST_CASE("UpgradeToFullTrust on an already-kFull session leaves it kFull", "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne);
    REQUIRE(lease.has_value());

    sessions.UpgradeToFullTrust(kConnectionA, kSessionOne);

    CHECK(sessions.IsFullyTrusted(kConnectionA));
}

TEST_CASE("UpgradeToFullTrust does not change the session's identifier or client identity",
          "[application][session]") {
    SessionManager sessions;
    auto lease =
        sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne, SessionTrustTier::kRestricted);
    REQUIRE(lease.has_value());

    sessions.UpgradeToFullTrust(kConnectionA, kSessionOne);

    CHECK(sessions.IsValidForConnection(kSessionOne, kConnectionA));
    auto clientId = sessions.ClientIdForConnection(kConnectionA);
    REQUIRE(clientId.has_value());
    CHECK(*clientId == kClientOne);
}

TEST_CASE("IsFullyTrusted is false when no session is active", "[application][session]") {
    SessionManager sessions;
    CHECK_FALSE(sessions.IsFullyTrusted(kConnectionA));
}

TEST_CASE("ActiveClientId returns no value when no session is active", "[application][session]") {
    SessionManager sessions;
    CHECK_FALSE(sessions.ActiveClientId().has_value());
}

TEST_CASE("ActiveClientId returns the active session's client identity", "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne);
    REQUIRE(lease.has_value());

    auto clientId = sessions.ActiveClientId();
    REQUIRE(clientId.has_value());
    CHECK(*clientId == kClientOne);
}

TEST_CASE("ActiveClientId is cleared once the owning lease is released", "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne);
    REQUIRE(lease.has_value());
    lease.reset();

    CHECK_FALSE(sessions.ActiveClientId().has_value());
}

TEST_CASE("ActiveClientId is cleared by InvalidateAll", "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne);
    REQUIRE(lease.has_value());
    sessions.InvalidateAll();

    CHECK_FALSE(sessions.ActiveClientId().has_value());
}

TEST_CASE("ActiveClientId reports the new session's client after a reconnect replaces the old one",
          "[application][session]") {
    SessionManager sessions;
    auto firstLease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne);
    REQUIRE(firstLease.has_value());
    firstLease.reset();

    auto secondLease = sessions.TryCreateSession(kConnectionB, kSessionTwo, kClientTwo);
    REQUIRE(secondLease.has_value());

    auto clientId = sessions.ActiveClientId();
    REQUIRE(clientId.has_value());
    CHECK(*clientId == kClientTwo);
}

TEST_CASE("a stale lease cannot clear ActiveClientId for a replacement session on the same connection",
          "[application][session]") {
    SessionManager sessions;
    auto staleLease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne);
    REQUIRE(staleLease.has_value());
    sessions.InvalidateAll();

    auto replacementLease = sessions.TryCreateSession(kConnectionA, kSessionTwo, kClientTwo);
    REQUIRE(replacementLease.has_value());
    staleLease.reset();

    auto clientId = sessions.ActiveClientId();
    REQUIRE(clientId.has_value());
    CHECK(*clientId == kClientTwo);
}

TEST_CASE("ActiveClientId reports the client regardless of trust tier", "[application][session]") {
    SessionManager sessions;
    auto lease =
        sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne, SessionTrustTier::kRestricted);
    REQUIRE(lease.has_value());

    auto clientId = sessions.ActiveClientId();
    REQUIRE(clientId.has_value());
    CHECK(*clientId == kClientOne);
}

TEST_CASE("IsFullyTrusted is false for a connection that does not own the active kFull session",
          "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne);
    REQUIRE(lease.has_value());

    CHECK_FALSE(sessions.IsFullyTrusted(kConnectionB));
}

TEST_CASE("a session created after a restricted one is invalidated starts kFull by default",
          "[application][session]") {
    SessionManager sessions;
    auto firstLease =
        sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne, SessionTrustTier::kRestricted);
    REQUIRE(firstLease.has_value());
    firstLease.reset();

    auto secondLease = sessions.TryCreateSession(kConnectionA, kSessionTwo, kClientTwo);
    REQUIRE(secondLease.has_value());
    CHECK(sessions.IsFullyTrusted(kConnectionA));
}

TEST_CASE("exactly one concurrent TryCreateSession attempt succeeds", "[application][session]") {
    // Uses a spin barrier to maximize actual thread overlap rather than a timing
    // sleep to approximate concurrency (ai/context/skse/testing.md).
    SessionManager sessions;

    constexpr int kAttempts = 16;
    std::atomic<int> readyCount{0};
    std::atomic<bool> go{false};
    std::atomic<int> attemptedCount{0};
    std::atomic<int> successCount{0};
    std::vector<std::thread> threads;
    threads.reserve(kAttempts);

    for (int i = 0; i < kAttempts; ++i) {
        threads.emplace_back([&, i]() {
            readyCount.fetch_add(1, std::memory_order_relaxed);
            while (!go.load(std::memory_order_acquire)) {
            }
            auto lease = sessions.TryCreateSession(static_cast<ConnectionId>(i), "session-" + std::to_string(i),
                                                   "client-" + std::to_string(i));
            if (lease.has_value()) {
                successCount.fetch_add(1, std::memory_order_relaxed);
            }
            attemptedCount.fetch_add(1, std::memory_order_release);
            while (attemptedCount.load(std::memory_order_acquire) < kAttempts) {
            }
        });
    }

    while (readyCount.load(std::memory_order_relaxed) < kAttempts) {
    }
    go.store(true, std::memory_order_release);

    for (std::thread& t : threads) {
        t.join();
    }

    CHECK(successCount.load() == 1);
}

TEST_CASE("ActiveSessionId returns no value when no session is active", "[application][session]") {
    SessionManager sessions;
    CHECK_FALSE(sessions.ActiveSessionId().has_value());
}

TEST_CASE("ActiveSessionId returns the active session's identifier", "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne);
    REQUIRE(lease.has_value());

    auto sessionId = sessions.ActiveSessionId();
    REQUIRE(sessionId.has_value());
    CHECK(*sessionId == kSessionOne);
}

TEST_CASE("ActiveSessionId is cleared once the owning lease is released", "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne);
    REQUIRE(lease.has_value());
    lease.reset();

    CHECK_FALSE(sessions.ActiveSessionId().has_value());
}

TEST_CASE("ActiveSessionId is cleared by InvalidateAll", "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne);
    REQUIRE(lease.has_value());
    sessions.InvalidateAll();

    CHECK_FALSE(sessions.ActiveSessionId().has_value());
}

TEST_CASE("ActiveSessionId reports the new session's identifier after a reconnect replaces the old one",
          "[application][session]") {
    SessionManager sessions;
    auto firstLease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne);
    REQUIRE(firstLease.has_value());
    firstLease.reset();

    auto secondLease = sessions.TryCreateSession(kConnectionB, kSessionTwo, kClientTwo);
    REQUIRE(secondLease.has_value());

    auto sessionId = sessions.ActiveSessionId();
    REQUIRE(sessionId.has_value());
    CHECK(*sessionId == kSessionTwo);
}

TEST_CASE("a stale lease cannot clear ActiveSessionId for a replacement session on the same connection",
          "[application][session]") {
    SessionManager sessions;
    auto staleLease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne);
    REQUIRE(staleLease.has_value());
    sessions.InvalidateAll();

    auto replacementLease = sessions.TryCreateSession(kConnectionA, kSessionTwo, kClientTwo);
    REQUIRE(replacementLease.has_value());
    staleLease.reset();

    auto sessionId = sessions.ActiveSessionId();
    REQUIRE(sessionId.has_value());
    CHECK(*sessionId == kSessionTwo);
}

TEST_CASE("TryCreateSession defaults to kTrustedDeviceCredential for callers that predate the "
          "distinction",
          "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne);
    REQUIRE(lease.has_value());
    auto authMethod = sessions.AuthMethodForConnection(kConnectionA);
    REQUIRE(authMethod.has_value());
    CHECK(*authMethod == SessionAuthMethod::kTrustedDeviceCredential);
}

TEST_CASE("TryCreateSession honors an explicit kDeveloperToken session", "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne, SessionTrustTier::kFull,
                                            SessionAuthMethod::kDeveloperToken);
    REQUIRE(lease.has_value());
    auto authMethod = sessions.AuthMethodForConnection(kConnectionA);
    REQUIRE(authMethod.has_value());
    CHECK(*authMethod == SessionAuthMethod::kDeveloperToken);
}

TEST_CASE("TryCreateSession honors an explicit kUnpaired session", "[application][session]") {
    // Previously indistinguishable from kTrustedDeviceCredential under the old bool: both mapped to
    // isDeveloperAuthenticated=false. The enum makes this a real, separately observable state.
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne, SessionTrustTier::kRestricted,
                                            SessionAuthMethod::kUnpaired);
    REQUIRE(lease.has_value());
    auto authMethod = sessions.AuthMethodForConnection(kConnectionA);
    REQUIRE(authMethod.has_value());
    CHECK(*authMethod == SessionAuthMethod::kUnpaired);
}

TEST_CASE("AuthMethodForConnection returns no value when no session is active", "[application][session]") {
    SessionManager sessions;
    CHECK_FALSE(sessions.AuthMethodForConnection(kConnectionA).has_value());
}

TEST_CASE("AuthMethodForConnection returns no value for a connection that does not own the active "
          "session",
          "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne, SessionTrustTier::kFull,
                                            SessionAuthMethod::kDeveloperToken);
    REQUIRE(lease.has_value());
    CHECK_FALSE(sessions.AuthMethodForConnection(kConnectionB).has_value());
}

TEST_CASE("AuthMethodForConnection is cleared when the session is invalidated", "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne, SessionTrustTier::kFull,
                                            SessionAuthMethod::kDeveloperToken);
    REQUIRE(lease.has_value());
    lease.reset();
    CHECK_FALSE(sessions.AuthMethodForConnection(kConnectionA).has_value());
}

TEST_CASE("AuthMethodForConnection is cleared by InvalidateAll", "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne, SessionTrustTier::kFull,
                                            SessionAuthMethod::kDeveloperToken);
    REQUIRE(lease.has_value());
    sessions.InvalidateAll();
    CHECK_FALSE(sessions.AuthMethodForConnection(kConnectionA).has_value());
}

TEST_CASE("a session created after a kDeveloperToken one is invalidated defaults to "
          "kTrustedDeviceCredential",
          "[application][session]") {
    SessionManager sessions;
    auto firstLease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne, SessionTrustTier::kFull,
                                                 SessionAuthMethod::kDeveloperToken);
    REQUIRE(firstLease.has_value());
    firstLease.reset();

    auto secondLease = sessions.TryCreateSession(kConnectionA, kSessionTwo, kClientTwo);
    REQUIRE(secondLease.has_value());
    auto authMethod = sessions.AuthMethodForConnection(kConnectionA);
    REQUIRE(authMethod.has_value());
    CHECK(*authMethod == SessionAuthMethod::kTrustedDeviceCredential);
}

TEST_CASE("AuthMethodForConnection is independent of trust tier: a kRestricted developer session "
          "reports both",
          "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne, SessionTrustTier::kRestricted,
                                            SessionAuthMethod::kDeveloperToken);
    REQUIRE(lease.has_value());
    CHECK_FALSE(sessions.IsFullyTrusted(kConnectionA));
    auto authMethod = sessions.AuthMethodForConnection(kConnectionA);
    REQUIRE(authMethod.has_value());
    CHECK(*authMethod == SessionAuthMethod::kDeveloperToken);
}

TEST_CASE("UpgradeToFullTrust does not change AuthMethodForConnection", "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne, SessionTrustTier::kRestricted,
                                            SessionAuthMethod::kUnpaired);
    REQUIRE(lease.has_value());

    sessions.UpgradeToFullTrust(kConnectionA, kSessionOne);

    CHECK(sessions.IsFullyTrusted(kConnectionA));
    // authMethod reflects how the session authenticated at hello, not its current trust tier -- an
    // in-place pairing upgrade leaves it kUnpaired even though the tier is now kFull, matching the
    // real production path (only a session that started kUnpaired ever calls UpgradeToFullTrust).
    auto authMethod = sessions.AuthMethodForConnection(kConnectionA);
    REQUIRE(authMethod.has_value());
    CHECK(*authMethod == SessionAuthMethod::kUnpaired);
}

TEST_CASE("a stale lease cannot clear AuthMethodForConnection for a replacement session on the same "
          "connection",
          "[application][session]") {
    SessionManager sessions;
    auto staleLease = sessions.TryCreateSession(kConnectionA, kSessionOne, kClientOne, SessionTrustTier::kFull,
                                                 SessionAuthMethod::kDeveloperToken);
    REQUIRE(staleLease.has_value());
    sessions.InvalidateAll();

    auto replacementLease = sessions.TryCreateSession(kConnectionA, kSessionTwo, kClientTwo, SessionTrustTier::kFull,
                                                       SessionAuthMethod::kDeveloperToken);
    REQUIRE(replacementLease.has_value());
    staleLease.reset();

    auto authMethod = sessions.AuthMethodForConnection(kConnectionA);
    REQUIRE(authMethod.has_value());
    CHECK(*authMethod == SessionAuthMethod::kDeveloperToken);
}
