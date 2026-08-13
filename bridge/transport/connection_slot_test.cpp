#include "transport/connection_slot.hpp"

#include <catch2/catch_test_macros.hpp>

#include <atomic>
#include <thread>
#include <vector>

using dovahlink::transport::ConnectionSlot;

TEST_CASE("a fresh slot is not occupied", "[transport][connection_slot]") {
    ConnectionSlot slot;
    CHECK_FALSE(slot.IsOccupied());
}

TEST_CASE("TryAcquire succeeds when the slot is free", "[transport][connection_slot]") {
    ConnectionSlot slot;
    CHECK(slot.TryAcquire());
    CHECK(slot.IsOccupied());
}

TEST_CASE("TryAcquire fails while the slot is already occupied", "[transport][connection_slot]") {
    ConnectionSlot slot;
    REQUIRE(slot.TryAcquire());
    CHECK_FALSE(slot.TryAcquire());
}

TEST_CASE("Release frees the slot for a later TryAcquire", "[transport][connection_slot]") {
    ConnectionSlot slot;
    REQUIRE(slot.TryAcquire());
    slot.Release();
    CHECK_FALSE(slot.IsOccupied());
    CHECK(slot.TryAcquire());
}

TEST_CASE("Release is a no-op when the slot was never acquired", "[transport][connection_slot]") {
    ConnectionSlot slot;
    slot.Release();
    CHECK_FALSE(slot.IsOccupied());
    CHECK(slot.TryAcquire());
}

TEST_CASE("exactly one concurrent TryAcquire attempt succeeds", "[transport][connection_slot]") {
    // Uses a spin barrier to maximize actual thread overlap rather than a
    // timing sleep to approximate concurrency.
    ConnectionSlot slot;

    constexpr int kAttempts = 16;
    std::atomic<int> readyCount{0};
    std::atomic<bool> go{false};
    std::atomic<int> successCount{0};
    std::vector<std::thread> threads;
    threads.reserve(kAttempts);

    for (int i = 0; i < kAttempts; ++i) {
        threads.emplace_back([&] {
            readyCount.fetch_add(1, std::memory_order_relaxed);
            while (!go.load(std::memory_order_acquire)) {
            }
            if (slot.TryAcquire()) {
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
