#include "security/token_store.hpp"

#include <catch2/catch_test_macros.hpp>

#include <atomic>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <thread>
#include <type_traits>
#include <utility>
#include <vector>

using dovahlink::security::TokenStore;

static_assert(!std::is_copy_constructible_v<TokenStore::Reservation>);
static_assert(!std::is_copy_assignable_v<TokenStore::Reservation>);
static_assert(std::is_nothrow_move_constructible_v<TokenStore::Reservation>);
static_assert(std::is_nothrow_move_assignable_v<TokenStore::Reservation>);

namespace {

///  Builds a deterministic token-sized byte sequence from a seed value.
std::vector<std::uint8_t> MakeToken(std::uint8_t seed) {
    std::vector<std::uint8_t> token(32);
    for (std::size_t i = 0; i < token.size(); ++i) {
        token[i] = static_cast<std::uint8_t>(seed + i);
    }
    return token;
}

} //  namespace

TEST_CASE("committing a matching reservation consumes the token",
          "[security][token_store]") {
    auto token = MakeToken(1);
    TokenStore store(token);
    auto reservation = store.TryReserve(token);
    REQUIRE(reservation.has_value());
    reservation->Commit();
    CHECK_FALSE(store.IsAvailable());
}

TEST_CASE(
    "an incorrect token creates no reservation and leaves the store available",
    "[security][token_store]") {
    TokenStore store(MakeToken(1));
    CHECK_FALSE(store.TryReserve(MakeToken(2)).has_value());
    CHECK(store.IsAvailable());
}

TEST_CASE("a token of the wrong length creates no reservation",
          "[security][token_store]") {
    TokenStore store(MakeToken(1));
    std::vector<std::uint8_t> shortToken{1, 2, 3};
    CHECK_FALSE(store.TryReserve(shortToken).has_value());
}

TEST_CASE("a wrong guess does not block a later correct guess",
          "[security][token_store]") {
    auto token = MakeToken(1);
    TokenStore store(token);
    REQUIRE_FALSE(store.TryReserve(MakeToken(2)).has_value());
    CHECK(store.TryReserve(token).has_value());
}

TEST_CASE("an empty configured token fails closed", "[security][token_store]") {
    TokenStore store(std::vector<std::uint8_t>{});
    CHECK_FALSE(store.TryReserve(std::vector<std::uint8_t>{}).has_value());
    CHECK_FALSE(store.IsAvailable());
}

TEST_CASE("a second reservation fails after the first reservation is committed",
          "[security][token_store]") {
    auto token = MakeToken(1);
    TokenStore store(token);
    auto reservation = store.TryReserve(token);
    REQUIRE(reservation.has_value());
    reservation->Commit();
    CHECK_FALSE(store.TryReserve(token).has_value());
}

TEST_CASE("TryReserve fails once the token has expired",
          "[security][token_store]") {
    auto token = MakeToken(1);
    TokenStore store(token, std::chrono::seconds(0));
    CHECK_FALSE(store.TryReserve(token).has_value());
}

TEST_CASE("IsAvailable is false once the token has expired",
          "[security][token_store]") {
    TokenStore store(MakeToken(1), std::chrono::seconds(0));
    CHECK_FALSE(store.IsAvailable());
}

TEST_CASE("IsAvailable is true before consumption or expiry",
          "[security][token_store]") {
    TokenStore store(MakeToken(1));
    CHECK(store.IsAvailable());
}

TEST_CASE(
    "RemainingSeconds reports a positive duration before consumption or expiry",
    "[security][token_store]") {
    TokenStore store(MakeToken(1), std::chrono::minutes(5));
    auto remaining = store.RemainingSeconds();
    REQUIRE(remaining.has_value());
    CHECK(*remaining > std::chrono::seconds(0));
    CHECK(*remaining <= std::chrono::minutes(5));
}

TEST_CASE("RemainingSeconds reports no value once the token has expired",
          "[security][token_store]") {
    TokenStore store(MakeToken(1), std::chrono::seconds(0));
    CHECK_FALSE(store.RemainingSeconds().has_value());
}

TEST_CASE("RemainingSeconds reports no value after a successful consume",
          "[security][token_store]") {
    auto token = MakeToken(1);
    TokenStore store(token);
    auto reservation = store.TryReserve(token);
    REQUIRE(reservation.has_value());
    reservation->Commit();
    CHECK_FALSE(store.RemainingSeconds().has_value());
}

TEST_CASE("IsAvailable is false after a successful consume",
          "[security][token_store]") {
    auto token = MakeToken(1);
    TokenStore store(token);
    auto reservation = store.TryReserve(token);
    REQUIRE(reservation.has_value());
    reservation->Commit();
    CHECK_FALSE(store.IsAvailable());
}

TEST_CASE("destroying an uncommitted reservation preserves the token",
          "[security][token_store]") {
    auto token = MakeToken(1);
    TokenStore store(token);
    {
        auto reservation = store.TryReserve(token);
        REQUIRE(reservation.has_value());
    }
    CHECK(store.IsAvailable());
    CHECK(store.TryReserve(token).has_value());
}

TEST_CASE("moving a reservation transfers commit authority",
          "[security][token_store]") {
    auto token = MakeToken(1);
    TokenStore store(token);
    auto reservation = store.TryReserve(token);
    REQUIRE(reservation.has_value());

    std::optional<TokenStore::Reservation> moved{std::move(*reservation)};
    reservation.reset();
    moved->Commit();
    CHECK_FALSE(store.IsAvailable());
}

TEST_CASE("move-assigning a reservation releases its previous token unchanged",
          "[security][token_store]") {
    auto firstToken = MakeToken(1);
    auto secondToken = MakeToken(2);
    TokenStore firstStore(firstToken);
    TokenStore secondStore(secondToken);
    auto firstReservation = firstStore.TryReserve(firstToken);
    auto secondReservation = secondStore.TryReserve(secondToken);
    REQUIRE(firstReservation.has_value());
    REQUIRE(secondReservation.has_value());

    *firstReservation = std::move(*secondReservation);
    secondReservation.reset();
    firstReservation->Commit();

    CHECK(firstStore.IsAvailable());
    CHECK_FALSE(secondStore.IsAvailable());
}

TEST_CASE("exactly one concurrent TryReserve attempt succeeds",
          "[security][token_store]") {
    //  Uses a spin barrier to maximize actual thread overlap rather than a
    //  timing sleep to approximate concurrency.
    auto token = MakeToken(1);
    TokenStore store(token);

    constexpr int kAttempts = 16;
    std::atomic<int> readyCount{0};
    std::atomic<bool> go{false};
    std::atomic<int> successCount{0};
    std::vector<std::thread> threads;
    threads.reserve(kAttempts);

    for (int i = 0; i < kAttempts; ++i) {
        threads.emplace_back([&]() {
            readyCount.fetch_add(1, std::memory_order_relaxed);
            while (!go.load(std::memory_order_acquire)) {
            }
            auto reservation = store.TryReserve(token);
            if (reservation.has_value()) {
                successCount.fetch_add(1, std::memory_order_relaxed);
                reservation->Commit();
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
    CHECK_FALSE(store.IsAvailable());
}
