#include "application/session.hpp"

#include <catch2/catch_test_macros.hpp>

#include <atomic>
#include <string>
#include <thread>
#include <vector>

using dovahlink::application::ConnectionId;
using dovahlink::application::SessionManager;

namespace {
constexpr ConnectionId kConnectionA = 1;
constexpr ConnectionId kConnectionB = 2;
const std::string kSessionOne = "session-1";
const std::string kSessionTwo = "session-2";
}  // namespace

TEST_CASE("TryCreateSession succeeds when no session is active", "[application][session]") {
    SessionManager sessions;
    CHECK(sessions.TryCreateSession(kConnectionA, kSessionOne));
}

TEST_CASE("TryCreateSession fails while a session is already active, even for a different connection",
          "[application][session]") {
    SessionManager sessions;
    REQUIRE(sessions.TryCreateSession(kConnectionA, kSessionOne));
    CHECK_FALSE(sessions.TryCreateSession(kConnectionB, kSessionTwo));
}

TEST_CASE("TryCreateSession fails even when re-called by the connection that already owns the session",
          "[application][session]") {
    // A connection gets exactly one session for its lifetime; a second call is caller
    // misuse, not a refresh, and must not silently succeed or replace the existing ID.
    SessionManager sessions;
    REQUIRE(sessions.TryCreateSession(kConnectionA, kSessionOne));
    CHECK_FALSE(sessions.TryCreateSession(kConnectionA, kSessionTwo));
    CHECK(sessions.IsValidForConnection(kSessionOne, kConnectionA));
}

TEST_CASE("IsValidForConnection is true for the owning connection and correct session ID",
          "[application][session]") {
    SessionManager sessions;
    REQUIRE(sessions.TryCreateSession(kConnectionA, kSessionOne));
    CHECK(sessions.IsValidForConnection(kSessionOne, kConnectionA));
}

TEST_CASE("IsValidForConnection is false for a foreign connection presenting the active session ID",
          "[application][session]") {
    SessionManager sessions;
    REQUIRE(sessions.TryCreateSession(kConnectionA, kSessionOne));
    // kConnectionB never authenticated, but presents kConnectionA's real session ID.
    CHECK_FALSE(sessions.IsValidForConnection(kSessionOne, kConnectionB));
}

TEST_CASE("IsValidForConnection is false for an unrecognized session ID from the owning connection",
          "[application][session]") {
    SessionManager sessions;
    REQUIRE(sessions.TryCreateSession(kConnectionA, kSessionOne));
    CHECK_FALSE(sessions.IsValidForConnection("not-the-real-session-id", kConnectionA));
}

TEST_CASE("IsValidForConnection is false when no session is active", "[application][session]") {
    SessionManager sessions;
    CHECK_FALSE(sessions.IsValidForConnection(kSessionOne, kConnectionA));
}

TEST_CASE("InvalidateSession is a no-op for a connection that does not own the active session",
          "[application][session]") {
    SessionManager sessions;
    REQUIRE(sessions.TryCreateSession(kConnectionA, kSessionOne));
    sessions.InvalidateSession(kConnectionB);
    CHECK(sessions.IsValidForConnection(kSessionOne, kConnectionA));
}

TEST_CASE("InvalidateSession clears the session for its owning connection", "[application][session]") {
    SessionManager sessions;
    REQUIRE(sessions.TryCreateSession(kConnectionA, kSessionOne));
    sessions.InvalidateSession(kConnectionA);
    CHECK_FALSE(sessions.IsValidForConnection(kSessionOne, kConnectionA));
}

TEST_CASE("the same connection can create a fresh session after its prior one is invalidated",
          "[application][session]") {
    SessionManager sessions;
    REQUIRE(sessions.TryCreateSession(kConnectionA, kSessionOne));
    sessions.InvalidateSession(kConnectionA);

    CHECK(sessions.TryCreateSession(kConnectionA, kSessionTwo));
    CHECK(sessions.IsValidForConnection(kSessionTwo, kConnectionA));
    CHECK_FALSE(sessions.IsValidForConnection(kSessionOne, kConnectionA));
}

TEST_CASE("a reconnect can create a fresh session after the prior one is invalidated",
          "[application][session]") {
    SessionManager sessions;
    REQUIRE(sessions.TryCreateSession(kConnectionA, kSessionOne));
    sessions.InvalidateSession(kConnectionA);

    // A different connection (the reconnect) can now claim the freed one-client slot.
    CHECK(sessions.TryCreateSession(kConnectionB, kSessionTwo));
    CHECK(sessions.IsValidForConnection(kSessionTwo, kConnectionB));
    // The old session ID is no longer valid for anyone, including its original connection.
    CHECK_FALSE(sessions.IsValidForConnection(kSessionOne, kConnectionA));
}

TEST_CASE("InvalidateAll clears the active session regardless of which connection holds it",
          "[application][session]") {
    SessionManager sessions;
    REQUIRE(sessions.TryCreateSession(kConnectionA, kSessionOne));
    sessions.InvalidateAll();
    CHECK_FALSE(sessions.IsValidForConnection(kSessionOne, kConnectionA));
    CHECK(sessions.TryCreateSession(kConnectionB, kSessionTwo));
}

TEST_CASE("InvalidateAll is safe to call when no session is active", "[application][session]") {
    SessionManager sessions;
    sessions.InvalidateAll();
    CHECK(sessions.TryCreateSession(kConnectionA, kSessionOne));
}

TEST_CASE("exactly one concurrent TryCreateSession attempt succeeds", "[application][session]") {
    // Uses a spin barrier to maximize actual thread overlap rather than a timing
    // sleep to approximate concurrency (ai/context/skse/testing.md).
    SessionManager sessions;

    constexpr int kAttempts = 16;
    std::atomic<int> readyCount{0};
    std::atomic<bool> go{false};
    std::atomic<int> successCount{0};
    std::vector<std::thread> threads;
    threads.reserve(kAttempts);

    for (int i = 0; i < kAttempts; ++i) {
        threads.emplace_back([&, i]() {
            readyCount.fetch_add(1, std::memory_order_relaxed);
            while (!go.load(std::memory_order_acquire)) {
            }
            if (sessions.TryCreateSession(static_cast<ConnectionId>(i), "session-" + std::to_string(i))) {
                successCount.fetch_add(1, std::memory_order_relaxed);
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
