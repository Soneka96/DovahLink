#include "transport/connection_slot.hpp"

#include <catch2/catch_test_macros.hpp>

#include <atomic>
#include <optional>
#include <thread>
#include <type_traits>
#include <utility>
#include <vector>

using dovahlink::transport::ConnectionSlot;

static_assert(!std::is_copy_constructible_v<ConnectionSlot::Lease>);
static_assert(!std::is_copy_assignable_v<ConnectionSlot::Lease>);
static_assert(std::is_nothrow_move_constructible_v<ConnectionSlot::Lease>);
static_assert(std::is_nothrow_move_assignable_v<ConnectionSlot::Lease>);

TEST_CASE("a fresh slot is not occupied", "[transport][connection_slot]") {
  ConnectionSlot slot;
  CHECK_FALSE(slot.IsOccupied());
}

TEST_CASE("TryAcquire succeeds when the slot is free",
          "[transport][connection_slot]") {
  ConnectionSlot slot;
  auto lease = slot.TryAcquire();
  CHECK(lease.has_value());
  CHECK(slot.IsOccupied());
}

TEST_CASE("TryAcquire fails while the slot is already occupied",
          "[transport][connection_slot]") {
  ConnectionSlot slot;
  auto lease = slot.TryAcquire();
  REQUIRE(lease.has_value());
  CHECK_FALSE(slot.TryAcquire().has_value());
}

TEST_CASE("destroying a lease frees the slot for a later TryAcquire",
          "[transport][connection_slot]") {
  ConnectionSlot slot;
  auto lease = slot.TryAcquire();
  REQUIRE(lease.has_value());
  lease.reset();
  CHECK_FALSE(slot.IsOccupied());
  CHECK(slot.TryAcquire().has_value());
}

TEST_CASE("moving a lease transfers ownership without releasing the slot",
          "[transport][connection_slot]") {
  ConnectionSlot slot;
  auto lease = slot.TryAcquire();
  REQUIRE(lease.has_value());

  std::optional<ConnectionSlot::Lease> movedLease{std::move(*lease)};
  lease.reset();

  CHECK(slot.IsOccupied());
  CHECK_FALSE(slot.TryAcquire().has_value());

  movedLease.reset();
  CHECK_FALSE(slot.IsOccupied());
  CHECK(slot.TryAcquire().has_value());
}

TEST_CASE("move-assigning a lease releases its previous slot",
          "[transport][connection_slot]") {
  ConnectionSlot firstSlot;
  ConnectionSlot secondSlot;
  auto firstLease = firstSlot.TryAcquire();
  auto secondLease = secondSlot.TryAcquire();
  REQUIRE(firstLease.has_value());
  REQUIRE(secondLease.has_value());

  *firstLease = std::move(*secondLease);

  CHECK_FALSE(firstSlot.IsOccupied());
  CHECK(secondSlot.IsOccupied());
  secondLease.reset();
  CHECK(secondSlot.IsOccupied());
  firstLease.reset();
  CHECK_FALSE(secondSlot.IsOccupied());
}

TEST_CASE("exactly one concurrent TryAcquire attempt succeeds",
          "[transport][connection_slot]") {
  // Uses a spin barrier to maximize actual thread overlap rather than a
  // timing sleep to approximate concurrency.
  ConnectionSlot slot;

  constexpr int kAttempts = 16;
  std::atomic<int> readyCount{0};
  std::atomic<bool> go{false};
  std::atomic<int> attemptedCount{0};
  std::atomic<int> successCount{0};
  std::vector<std::thread> threads;
  threads.reserve(kAttempts);

  for (int i = 0; i < kAttempts; ++i) {
    threads.emplace_back([&] {
      readyCount.fetch_add(1, std::memory_order_relaxed);
      while (!go.load(std::memory_order_acquire)) {
      }
      auto lease = slot.TryAcquire();
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

  for (std::thread &t : threads) {
    t.join();
  }

  CHECK(successCount.load() == 1);
  CHECK_FALSE(slot.IsOccupied());
}
