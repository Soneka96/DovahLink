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
using dovahlink::application::SessionManager;

static_assert(!std::is_copy_constructible_v<SessionManager::Lease>);
static_assert(!std::is_copy_assignable_v<SessionManager::Lease>);
static_assert(std::is_nothrow_move_constructible_v<SessionManager::Lease>);
static_assert(std::is_nothrow_move_assignable_v<SessionManager::Lease>);

namespace {
constexpr ConnectionId kConnectionA = 1;
constexpr ConnectionId kConnectionB = 2;
const std::string kSessionOne = "session-1";
const std::string kSessionTwo = "session-2";
}  // namespace

TEST_CASE("TryCreateSession succeeds when no session is active", "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne);
    REQUIRE(lease.has_value());
    CHECK(sessions.IsValidForConnection(kSessionOne, kConnectionA));
}

TEST_CASE("TryCreateSession fails while a session is already active, even for a different connection",
          "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne);
    REQUIRE(lease.has_value());
    CHECK_FALSE(sessions.TryCreateSession(kConnectionB, kSessionTwo).has_value());
}

TEST_CASE("TryCreateSession fails even when re-called by the connection that already owns the session",
          "[application][session]") {
    // A connection gets exactly one session for its lifetime; a second call is caller
    // misuse, not a refresh, and must not silently succeed or replace the existing ID.
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne);
    REQUIRE(lease.has_value());
    CHECK_FALSE(sessions.TryCreateSession(kConnectionA, kSessionTwo).has_value());
    CHECK(sessions.IsValidForConnection(kSessionOne, kConnectionA));
}

TEST_CASE("IsValidForConnection is true for the owning connection and correct session ID",
          "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne);
    REQUIRE(lease.has_value());
    CHECK(sessions.IsValidForConnection(kSessionOne, kConnectionA));
}

TEST_CASE("IsValidForConnection is false for a foreign connection presenting the active session ID",
          "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne);
    REQUIRE(lease.has_value());
    // kConnectionB never authenticated, but presents kConnectionA's real session ID.
    CHECK_FALSE(sessions.IsValidForConnection(kSessionOne, kConnectionB));
}

TEST_CASE("IsValidForConnection is false for an unrecognized session ID from the owning connection",
          "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne);
    REQUIRE(lease.has_value());
    CHECK_FALSE(sessions.IsValidForConnection("not-the-real-session-id", kConnectionA));
}

TEST_CASE("IsValidForConnection is false when no session is active", "[application][session]") {
    SessionManager sessions;
    CHECK_FALSE(sessions.IsValidForConnection(kSessionOne, kConnectionA));
}

TEST_CASE("destroying a lease invalidates its session", "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne);
    REQUIRE(lease.has_value());
    lease.reset();
    CHECK_FALSE(sessions.IsValidForConnection(kSessionOne, kConnectionA));
}

TEST_CASE("the same connection can create a fresh session after its prior one is invalidated",
          "[application][session]") {
    SessionManager sessions;
    auto firstLease = sessions.TryCreateSession(kConnectionA, kSessionOne);
    REQUIRE(firstLease.has_value());
    firstLease.reset();

    auto secondLease = sessions.TryCreateSession(kConnectionA, kSessionTwo);
    REQUIRE(secondLease.has_value());
    CHECK(sessions.IsValidForConnection(kSessionTwo, kConnectionA));
    CHECK_FALSE(sessions.IsValidForConnection(kSessionOne, kConnectionA));
}

TEST_CASE("a reconnect can create a fresh session after the prior one is invalidated",
          "[application][session]") {
    SessionManager sessions;
    auto firstLease = sessions.TryCreateSession(kConnectionA, kSessionOne);
    REQUIRE(firstLease.has_value());
    firstLease.reset();

    // A different connection (the reconnect) can now claim the freed one-client slot.
    auto secondLease = sessions.TryCreateSession(kConnectionB, kSessionTwo);
    REQUIRE(secondLease.has_value());
    CHECK(sessions.IsValidForConnection(kSessionTwo, kConnectionB));
    // The old session ID is no longer valid for anyone, including its original connection.
    CHECK_FALSE(sessions.IsValidForConnection(kSessionOne, kConnectionA));
}

TEST_CASE("InvalidateAll clears the active session regardless of which connection holds it",
          "[application][session]") {
    SessionManager sessions;
    auto firstLease = sessions.TryCreateSession(kConnectionA, kSessionOne);
    REQUIRE(firstLease.has_value());
    sessions.InvalidateAll();
    CHECK_FALSE(sessions.IsValidForConnection(kSessionOne, kConnectionA));
    auto secondLease = sessions.TryCreateSession(kConnectionB, kSessionTwo);
    REQUIRE(secondLease.has_value());
}

TEST_CASE("InvalidateAll is safe to call when no session is active", "[application][session]") {
    SessionManager sessions;
    sessions.InvalidateAll();
    CHECK(sessions.TryCreateSession(kConnectionA, kSessionOne).has_value());
}

TEST_CASE("a stale lease cannot invalidate a replacement session on the same connection",
          "[application][session]") {
    SessionManager sessions;
    auto staleLease = sessions.TryCreateSession(kConnectionA, kSessionOne);
    REQUIRE(staleLease.has_value());
    sessions.InvalidateAll();

    auto replacementLease = sessions.TryCreateSession(kConnectionA, kSessionTwo);
    REQUIRE(replacementLease.has_value());
    staleLease.reset();

    CHECK(sessions.IsValidForConnection(kSessionTwo, kConnectionA));
}

TEST_CASE("moving a lease transfers session ownership", "[application][session]") {
    SessionManager sessions;
    auto lease = sessions.TryCreateSession(kConnectionA, kSessionOne);
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
    auto firstLease = firstSessions.TryCreateSession(kConnectionA, kSessionOne);
    auto secondLease = secondSessions.TryCreateSession(kConnectionB, kSessionTwo);
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
            auto lease = sessions.TryCreateSession(static_cast<ConnectionId>(i),
                                                   "session-" + std::to_string(i));
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
