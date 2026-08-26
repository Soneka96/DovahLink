#include "security/failed_token_reservation.hpp"

#include "security/failed_token_throttle.hpp"

#include <catch2/catch_test_macros.hpp>

#include <chrono>
#include <cstddef>
#include <new>
#include <optional>
#include <type_traits>
#include <utility>
#include <vector>

using dovahlink::security::FailedTokenReservation;
using dovahlink::security::FailedTokenThrottle;

static_assert(!std::is_copy_constructible_v<FailedTokenReservation>);
static_assert(!std::is_copy_assignable_v<FailedTokenReservation>);
static_assert(std::is_nothrow_move_constructible_v<FailedTokenReservation>);
static_assert(std::is_nothrow_move_assignable_v<FailedTokenReservation>);

namespace {
///  Clock type used for deterministic reservation assertions.
using Clock = std::chrono::steady_clock;

///  Counter double that fails once while committing, then records later.
class RetryableCommitCounter final : public dovahlink::security::IRateWindowCounter {
  public:
    ///  Creates a counter that throws on its first commit.
    RetryableCommitCounter() = default;

    ///  @copydoc dovahlink::security::IRateWindowCounter::RecordEvent
    [[nodiscard]] std::size_t
    RecordEvent(Clock::time_point) override {
        return 0;
    }

    ///  @copydoc dovahlink::security::IRateWindowCounter::ActiveCount
    [[nodiscard]] std::size_t
    ActiveCount(Clock::time_point) override {
        return 0;
    }

    ///  @copydoc dovahlink::security::IRateWindowCounter::TryReserve
    [[nodiscard]] bool TryReserve(Clock::time_point,
                                  std::size_t) override {
        return true;
    }

    ///  @copydoc dovahlink::security::IRateWindowCounter::CommitReservation
    void CommitReservation(Clock::time_point) override {
        ++commitCalls;
        if (throwOnNextCommit) {
            throwOnNextCommit = false;
            throw std::bad_alloc();
        }
    }

    ///  @copydoc dovahlink::security::IRateWindowCounter::ReleaseReservation
    void ReleaseReservation() noexcept override { ++releaseCalls; }

    ///  Whether the next commit should throw.
    bool throwOnNextCommit = true;

    ///  Number of commit attempts observed.
    int commitCalls = 0;

    ///  Number of released reservations observed.
    int releaseCalls = 0;
};
} //  namespace

TEST_CASE("destroying an uncommitted failed-token reservation releases its slot",
          "[security][failed_token_reservation]") {
    FailedTokenThrottle throttle;
    Clock::time_point now = Clock::now();

    {
        auto reservation = throttle.TryReserve(now);
        REQUIRE(reservation.has_value());
    }

    CHECK_FALSE(throttle.IsBlocked(now));
    CHECK(throttle.TryReserve(now).has_value());
}

TEST_CASE("committing a failed-token reservation keeps its failure counted",
          "[security][failed_token_reservation]") {
    FailedTokenThrottle throttle;
    Clock::time_point now = Clock::now();

    auto reservation = throttle.TryReserve(now);
    REQUIRE(reservation.has_value());
    reservation->Commit();

    CHECK(throttle.IsBlocked(now + std::chrono::seconds(5)) == false);
    auto next = throttle.TryReserve(now + std::chrono::seconds(5));
    CHECK(next.has_value());
}

TEST_CASE("a failed reservation commit retains ownership for retry or release",
          "[security][failed_token_reservation]") {
    RetryableCommitCounter counter;
    FailedTokenReservation reservation(counter, Clock::now());

    REQUIRE_THROWS_AS(reservation.Commit(), std::bad_alloc);
    CHECK(counter.commitCalls == 1);
    CHECK(counter.releaseCalls == 0);

    reservation.Commit();
    CHECK(counter.commitCalls == 2);
    CHECK(counter.releaseCalls == 0);
}

TEST_CASE("moving a failed-token reservation transfers release ownership",
          "[security][failed_token_reservation]") {
    FailedTokenThrottle throttle;
    Clock::time_point now = Clock::now();
    std::vector<std::optional<FailedTokenReservation>> held;
    held.reserve(4);
    for (int i = 0; i < 4; ++i) {
        held.push_back(throttle.TryReserve(now));
        REQUIRE(held.back().has_value());
    }

    std::optional<FailedTokenReservation> reservation =
        throttle.TryReserve(now);
    REQUIRE(reservation.has_value());
    std::optional<FailedTokenReservation> moved{std::move(*reservation)};
    reservation.reset();

    CHECK_FALSE(throttle.TryReserve(now).has_value());
    moved.reset();
    CHECK(throttle.TryReserve(now).has_value());
}

TEST_CASE("move-assigning a failed-token reservation releases the target "
          "before taking ownership",
          "[security][failed_token_reservation]") {
    FailedTokenThrottle throttle;
    Clock::time_point now = Clock::now();
    std::vector<std::optional<FailedTokenReservation>> held;
    held.reserve(3);
    for (int i = 0; i < 3; ++i) {
        held.push_back(throttle.TryReserve(now));
        REQUIRE(held.back().has_value());
    }

    auto first = throttle.TryReserve(now);
    auto second = throttle.TryReserve(now);
    REQUIRE(first.has_value());
    REQUIRE(second.has_value());

    *first = std::move(*second);
    second.reset();
    auto extra = throttle.TryReserve(now);
    REQUIRE(extra.has_value());

    first->Commit();
}
