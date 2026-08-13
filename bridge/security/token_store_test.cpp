#include "security/token_store.hpp"

#include <catch2/catch_test_macros.hpp>

#include <atomic>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <thread>
#include <vector>

using dovahlink::security::TokenStore;

namespace {

/// Builds a deterministic token-sized byte sequence from a seed value.
std::vector<std::uint8_t> MakeToken(std::uint8_t seed) {
    std::vector<std::uint8_t> token(32);
    for (std::size_t i = 0; i < token.size(); ++i) {
        token[i] = static_cast<std::uint8_t>(seed + i);
    }
    return token;
}

}  // namespace

TEST_CASE("TryConsume succeeds for the correct token before expiry", "[security][token_store]") {
    auto token = MakeToken(1);
    TokenStore store(token);
    CHECK(store.TryConsume(token));
}

TEST_CASE("TryConsume fails for an incorrect token and leaves the store available", "[security][token_store]") {
    TokenStore store(MakeToken(1));
    CHECK_FALSE(store.TryConsume(MakeToken(2)));
    CHECK(store.IsAvailable());
}

TEST_CASE("TryConsume fails for a token of the wrong length", "[security][token_store]") {
    TokenStore store(MakeToken(1));
    std::vector<std::uint8_t> shortToken{1, 2, 3};
    CHECK_FALSE(store.TryConsume(shortToken));
}

TEST_CASE("a wrong guess does not block a later correct guess", "[security][token_store]") {
    auto token = MakeToken(1);
    TokenStore store(token);
    REQUIRE_FALSE(store.TryConsume(MakeToken(2)));
    CHECK(store.TryConsume(token));
}

TEST_CASE("an empty presented token trivially matches an empty expected token", "[security][token_store]") {
    // Documents current behavior rather than asserting it is desirable: in
    // practice the token provider always supplies a real 256-bit token, so an
    // empty expected token is a construction bug in the caller,
    // not user-controlled input this store needs to defend against.
    TokenStore store(std::vector<std::uint8_t>{});
    CHECK(store.TryConsume(std::vector<std::uint8_t>{}));
}

TEST_CASE("a second TryConsume with the correct token fails after the first succeeds",
          "[security][token_store]") {
    auto token = MakeToken(1);
    TokenStore store(token);
    REQUIRE(store.TryConsume(token));
    CHECK_FALSE(store.TryConsume(token));
}

TEST_CASE("TryConsume fails once the token has expired", "[security][token_store]") {
    auto token = MakeToken(1);
    TokenStore store(token, std::chrono::seconds(0));
    CHECK_FALSE(store.TryConsume(token));
}

TEST_CASE("IsAvailable is false once the token has expired", "[security][token_store]") {
    TokenStore store(MakeToken(1), std::chrono::seconds(0));
    CHECK_FALSE(store.IsAvailable());
}

TEST_CASE("IsAvailable is true before consumption or expiry", "[security][token_store]") {
    TokenStore store(MakeToken(1));
    CHECK(store.IsAvailable());
}

TEST_CASE("IsAvailable is false after a successful consume", "[security][token_store]") {
    auto token = MakeToken(1);
    TokenStore store(token);
    REQUIRE(store.TryConsume(token));
    CHECK_FALSE(store.IsAvailable());
}

TEST_CASE("exactly one concurrent TryConsume attempt succeeds", "[security][token_store]") {
    // Uses a spin barrier to maximize actual thread overlap rather than a
    // timing sleep to approximate concurrency.
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
            if (store.TryConsume(token)) {
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
