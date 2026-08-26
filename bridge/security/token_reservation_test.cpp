#include "security/token_reservation.hpp"

#include <catch2/catch_test_macros.hpp>

#include <mutex>
#include <optional>
#include <type_traits>
#include <utility>

using dovahlink::security::TokenReservation;

static_assert(!std::is_copy_constructible_v<TokenReservation>);
static_assert(!std::is_copy_assignable_v<TokenReservation>);
static_assert(std::is_nothrow_move_constructible_v<TokenReservation>);
static_assert(std::is_nothrow_move_assignable_v<TokenReservation>);

namespace {

///  Builds a reservation over a caller-owned mutex, already locked, with a
///  spy commit action -- the same shape `TokenStore::TryReserve` builds,
///  without needing a real `TokenStore`.
TokenReservation MakeReservation(std::mutex& mutex, int& commitCalls) {
    return TokenReservation(std::unique_lock<std::mutex>(mutex),
                            [&commitCalls] { ++commitCalls; });
}

} //  namespace

TEST_CASE("Commit runs the action and releases the lock",
          "[security][token_reservation]") {
    std::mutex mutex;
    int commitCalls = 0;
    TokenReservation reservation = MakeReservation(mutex, commitCalls);

    reservation.Commit();

    CHECK(commitCalls == 1);
    //  The lock was released by Commit(): a fresh lock attempt succeeds
    //  immediately rather than blocking.
    CHECK(mutex.try_lock());
    mutex.unlock();
}

TEST_CASE("Commit is harmless to call more than once",
          "[security][token_reservation]") {
    std::mutex mutex;
    int commitCalls = 0;
    TokenReservation reservation = MakeReservation(mutex, commitCalls);

    reservation.Commit();
    reservation.Commit();

    CHECK(commitCalls == 1);
}

TEST_CASE("destroying an uncommitted reservation runs no action",
          "[security][token_reservation]") {
    std::mutex mutex;
    int commitCalls = 0;
    {
        TokenReservation reservation = MakeReservation(mutex, commitCalls);
    }
    CHECK(commitCalls == 0);
    //  The lock was released on destruction too.
    CHECK(mutex.try_lock());
    mutex.unlock();
}

TEST_CASE("moving a reservation transfers commit authority",
          "[security][token_reservation]") {
    std::mutex mutex;
    int commitCalls = 0;
    std::optional<TokenReservation> reservation =
        MakeReservation(mutex, commitCalls);

    std::optional<TokenReservation> moved{std::move(*reservation)};
    reservation.reset();
    CHECK(commitCalls == 0);

    moved->Commit();
    CHECK(commitCalls == 1);
}

TEST_CASE("move-assigning a reservation transfers commit authority to the "
          "target",
          "[security][token_reservation]") {
    std::mutex firstMutex;
    std::mutex secondMutex;
    int firstCommitCalls = 0;
    int secondCommitCalls = 0;
    TokenReservation first = MakeReservation(firstMutex, firstCommitCalls);
    TokenReservation second = MakeReservation(secondMutex, secondCommitCalls);

    first = std::move(second);
    first.Commit();

    CHECK(firstCommitCalls == 0);
    CHECK(secondCommitCalls == 1);
}

TEST_CASE("move-assigning over a reservation releases its previous mutex "
          "without committing it",
          "[security][token_reservation]") {
    std::mutex firstMutex;
    std::mutex secondMutex;
    int firstCommitCalls = 0;
    int secondCommitCalls = 0;
    TokenReservation first = MakeReservation(firstMutex, firstCommitCalls);
    TokenReservation second = MakeReservation(secondMutex, secondCommitCalls);

    first = std::move(second);

    CHECK(firstCommitCalls == 0);
    CHECK(firstMutex.try_lock());
    firstMutex.unlock();
}

TEST_CASE("self-move-assignment is safe and does not run the action",
          "[security][token_reservation]") {
    std::mutex mutex;
    int commitCalls = 0;
    TokenReservation reservation = MakeReservation(mutex, commitCalls);

    TokenReservation& alias = reservation;
    reservation = std::move(alias);

    CHECK(commitCalls == 0);
    reservation.Commit();
    CHECK(commitCalls == 1);
}
