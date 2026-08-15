#include "application/revision_tracker.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cstdint>
#include <future>
#include <new>
#include <optional>
#include <string>
#include <thread>

using dovahlink::application::RevisionTracker;

namespace {
const std::string kCharacter = "character";
const std::string kLocation = "location";
const std::string kFingerprintA = "{\"level\":5}";
const std::string kFingerprintB = "{\"level\":6}";
}  // namespace

TEST_CASE("CurrentRevision is nullopt before any snapshot", "[application][revision_tracker]") {
    RevisionTracker tracker;
    CHECK_FALSE(tracker.CurrentRevision(kCharacter).has_value());
}

TEST_CASE("StartSnapshot establishes revision 1 for a fresh area", "[application][revision_tracker]") {
    RevisionTracker tracker;
    CHECK(tracker.StartSnapshot(kCharacter, kFingerprintA) == 1);
}

TEST_CASE("NextSnapshotRevision reports revision 1 without establishing a baseline",
          "[application][revision_tracker]") {
    RevisionTracker tracker;

    CHECK(tracker.NextSnapshotRevision(kCharacter, kFingerprintA) == 1);
    CHECK_FALSE(tracker.CurrentRevision(kCharacter).has_value());
}

TEST_CASE("CurrentRevision reflects the revision after StartSnapshot", "[application][revision_tracker]") {
    RevisionTracker tracker;
    tracker.StartSnapshot(kCharacter, kFingerprintA);
    REQUIRE(tracker.CurrentRevision(kCharacter).has_value());
    CHECK(*tracker.CurrentRevision(kCharacter) == 1);
}

TEST_CASE("StartSnapshot reuses the existing revision when the fingerprint is unchanged",
          "[application][revision_tracker]") {
    RevisionTracker tracker;
    REQUIRE(tracker.StartSnapshot(kCharacter, kFingerprintA) == 1);

    CHECK(tracker.StartSnapshot(kCharacter, kFingerprintA) == 1);
    CHECK(tracker.StartSnapshot(kCharacter, kFingerprintA) == 1);
}

TEST_CASE("StartSnapshot advances the revision when the fingerprint changes",
          "[application][revision_tracker]") {
    RevisionTracker tracker;
    REQUIRE(tracker.StartSnapshot(kCharacter, kFingerprintA) == 1);

    CHECK(tracker.StartSnapshot(kCharacter, kFingerprintB) == 2);
}

TEST_CASE("NextSnapshotRevision previews reuse without committing when the fingerprint is unchanged",
          "[application][revision_tracker]") {
    RevisionTracker tracker;
    tracker.StartSnapshot(kCharacter, kFingerprintA);

    CHECK(tracker.NextSnapshotRevision(kCharacter, kFingerprintA) == 1);
    CHECK(tracker.CurrentRevision(kCharacter) == 1);
}

TEST_CASE("NextSnapshotRevision previews an advance without committing when the fingerprint changes",
          "[application][revision_tracker]") {
    RevisionTracker tracker;
    tracker.StartSnapshot(kCharacter, kFingerprintA);

    CHECK(tracker.NextSnapshotRevision(kCharacter, kFingerprintB) == 2);
    CHECK(tracker.CurrentRevision(kCharacter) == 1);
}

TEST_CASE("StartSnapshot with no fingerprint always advances, matching v1's unconditional-advance "
          "contract",
          "[application][revision_tracker]") {
    RevisionTracker tracker;
    REQUIRE(tracker.StartSnapshot(kCharacter, std::nullopt) == 1);

    // No fingerprint supplied on either call: v1 has no defined "unchanged
    // state" comparison, so every call advances regardless of content.
    CHECK(tracker.StartSnapshot(kCharacter, std::nullopt) == 2);
    CHECK(tracker.StartSnapshot(kCharacter, std::nullopt) == 3);
}

TEST_CASE("NextSnapshotRevision with no fingerprint always previews an advance",
          "[application][revision_tracker]") {
    RevisionTracker tracker;
    tracker.StartSnapshot(kCharacter, std::nullopt);

    CHECK(tracker.NextSnapshotRevision(kCharacter, std::nullopt) == 2);
    CHECK(tracker.CurrentRevision(kCharacter) == 1);
}

TEST_CASE("CommitSnapshotIfBuilt commits the assigned revision when the builder succeeds",
          "[application][revision_tracker]") {
    RevisionTracker tracker;

    auto result = tracker.CommitSnapshotIfBuilt(kCharacter, kFingerprintA,
                                                 [](std::int64_t revision) -> std::optional<std::int64_t> {
                                                     return revision;
                                                 });

    REQUIRE(result.has_value());
    CHECK(*result == 1);
    REQUIRE(tracker.CurrentRevision(kCharacter).has_value());
    CHECK(*tracker.CurrentRevision(kCharacter) == 1);
}

TEST_CASE("CommitSnapshotIfBuilt leaves the revision unchanged when the builder returns no value",
          "[application][revision_tracker]") {
    RevisionTracker tracker;
    tracker.StartSnapshot(kCharacter, kFingerprintA);

    auto result = tracker.CommitSnapshotIfBuilt(
        kCharacter, kFingerprintB, [](std::int64_t) -> std::optional<std::int64_t> { return std::nullopt; });

    CHECK_FALSE(result.has_value());
    // Neither the revision nor the fingerprint basis advanced: a retry with
    // the original fingerprint still reuses revision 1.
    CHECK(tracker.CurrentRevision(kCharacter) == 1);
    CHECK(tracker.StartSnapshot(kCharacter, kFingerprintA) == 1);
}

TEST_CASE("CommitSnapshotIfBuilt passes the same revision StartSnapshot would have assigned",
          "[application][revision_tracker]") {
    RevisionTracker tracker;
    tracker.StartSnapshot(kCharacter, kFingerprintA);
    tracker.NextEvent(kCharacter);  // current revision now 2

    std::int64_t observedRevision = 0;
    tracker.CommitSnapshotIfBuilt(kCharacter, kFingerprintB, [&](std::int64_t revision) -> std::optional<bool> {
        observedRevision = revision;
        return true;
    });

    CHECK(observedRevision == 3);
    CHECK(tracker.CurrentRevision(kCharacter) == 3);
}

TEST_CASE("CommitSnapshotIfBuilt reuses the existing revision when the fingerprint is unchanged",
          "[application][revision_tracker]") {
    RevisionTracker tracker;
    tracker.StartSnapshot(kCharacter, kFingerprintA);

    std::int64_t observedRevision = 0;
    auto result = tracker.CommitSnapshotIfBuilt(kCharacter, kFingerprintA,
                                                 [&](std::int64_t revision) -> std::optional<std::int64_t> {
                                                     observedRevision = revision;
                                                     return revision;
                                                 });

    REQUIRE(result.has_value());
    CHECK(*result == 1);
    CHECK(observedRevision == 1);
    CHECK(tracker.CurrentRevision(kCharacter) == 1);
}

TEST_CASE("CommitSnapshotIfBuilt with no fingerprint always advances, matching StartSnapshot's "
          "unconditional-advance contract",
          "[application][revision_tracker]") {
    RevisionTracker tracker;
    tracker.CommitSnapshotIfBuilt(kCharacter, std::nullopt,
                                  [](std::int64_t revision) -> std::optional<std::int64_t> { return revision; });

    auto result = tracker.CommitSnapshotIfBuilt(
        kCharacter, std::nullopt, [](std::int64_t revision) -> std::optional<std::int64_t> { return revision; });

    REQUIRE(result.has_value());
    CHECK(*result == 2);
}

TEST_CASE("CommitSnapshotIfBuilt leaves the revision uncommitted when the builder throws",
          "[application][revision_tracker]") {
    RevisionTracker tracker;
    tracker.StartSnapshot(kCharacter, kFingerprintA);

    CHECK_THROWS_AS(tracker.CommitSnapshotIfBuilt(
                        kCharacter, kFingerprintB,
                        [](std::int64_t) -> std::optional<std::int64_t> { throw std::bad_alloc{}; }),
                    std::bad_alloc);

    CHECK(tracker.CurrentRevision(kCharacter) == 1);
    // If the lock did not release cleanly during unwind, this call on the
    // same (non-recursive) mutex would deadlock rather than return.
    CHECK(tracker.StartSnapshot(kCharacter, kFingerprintB) == 2);
}

TEST_CASE("CommitSnapshotIfBuilt holds its lock across the whole builder call, so a concurrent "
          "snapshot for the same area cannot commit until it finishes -- the race a separate "
          "preview-then-StartSnapshot pair leaves open",
          "[application][revision_tracker]") {
    RevisionTracker tracker;
    std::promise<void> builderEntered;
    std::future<void> builderEnteredFuture = builderEntered.get_future();
    std::promise<void> releaseBuilder;
    std::future<void> releaseBuilderFuture = releaseBuilder.get_future();

    std::thread committer([&] {
        tracker.CommitSnapshotIfBuilt(kCharacter, kFingerprintA,
                                      [&](std::int64_t revision) -> std::optional<std::int64_t> {
                                          builderEntered.set_value();
                                          releaseBuilderFuture.wait();
                                          return revision;
                                      });
    });

    builderEnteredFuture.wait();
    // The committing thread is inside the builder right now, still holding
    // the lock. A concurrent call for the same area must block on it until
    // committer finishes -- if it did not, this call could observe the
    // tracker before committer's revision 1 lands and would itself be
    // assigned revision 1 instead of 2.
    std::promise<std::int64_t> concurrentRevision;
    std::thread concurrentCaller(
        [&] { concurrentRevision.set_value(tracker.StartSnapshot(kCharacter, kFingerprintB)); });

    releaseBuilder.set_value();
    committer.join();
    concurrentCaller.join();

    CHECK(concurrentRevision.get_future().get() == 2);
}

TEST_CASE("NextEvent returns nullopt when no baseline has been established",
          "[application][revision_tracker]") {
    RevisionTracker tracker;
    CHECK_FALSE(tracker.NextEvent(kCharacter).has_value());
}

TEST_CASE("NextEvent after the first snapshot matches the established fixture convention "
          "(revision 1 snapshot, then baseRevision 1 / revision 2 event)",
          "[application][revision_tracker]") {
    RevisionTracker tracker;
    REQUIRE(tracker.StartSnapshot(kCharacter, kFingerprintA) == 1);

    auto event = tracker.NextEvent(kCharacter);
    REQUIRE(event.has_value());
    CHECK(event->first == 1);   // baseRevision
    CHECK(event->second == 2);  // revision
}

TEST_CASE("consecutive NextEvent calls produce a strictly increasing, correctly-linked sequence",
          "[application][revision_tracker]") {
    RevisionTracker tracker;
    tracker.StartSnapshot(kCharacter, kFingerprintA);

    auto first = tracker.NextEvent(kCharacter);
    auto second = tracker.NextEvent(kCharacter);
    auto third = tracker.NextEvent(kCharacter);

    REQUIRE(first.has_value());
    REQUIRE(second.has_value());
    REQUIRE(third.has_value());
    CHECK(*first == std::make_pair(std::int64_t{1}, std::int64_t{2}));
    CHECK(*second == std::make_pair(std::int64_t{2}, std::int64_t{3}));
    CHECK(*third == std::make_pair(std::int64_t{3}, std::int64_t{4}));
}

TEST_CASE("a recovery snapshot with changed content continues the sequence rather than restarting it",
          "[application][revision_tracker]") {
    RevisionTracker tracker;
    tracker.StartSnapshot(kCharacter, kFingerprintA);
    tracker.NextEvent(kCharacter);  // (1,2)
    tracker.NextEvent(kCharacter);  // (2,3) -- current revision now 3

    // Queue loss triggers a recovery snapshot with the state as it now
    // stands; a changed fingerprint must not restart the sequence at 1.
    std::int64_t recoveryRevision = tracker.StartSnapshot(kCharacter, kFingerprintB);
    CHECK(recoveryRevision == 4);
}

TEST_CASE("NextSnapshotRevision previews a recovery revision without advancing the sequence",
          "[application][revision_tracker]") {
    RevisionTracker tracker;
    tracker.StartSnapshot(kCharacter, kFingerprintA);
    tracker.NextEvent(kCharacter);
    tracker.NextEvent(kCharacter);

    CHECK(tracker.NextSnapshotRevision(kCharacter, kFingerprintB) == 4);
    CHECK(tracker.CurrentRevision(kCharacter) == 3);
}

TEST_CASE("NextEvent correctly continues from a recovery snapshot's new baseline",
          "[application][revision_tracker]") {
    RevisionTracker tracker;
    tracker.StartSnapshot(kCharacter, kFingerprintA);
    tracker.NextEvent(kCharacter);
    tracker.NextEvent(kCharacter);
    REQUIRE(tracker.StartSnapshot(kCharacter, kFingerprintB) == 4);

    auto event = tracker.NextEvent(kCharacter);
    REQUIRE(event.has_value());
    CHECK(event->first == 4);
    CHECK(event->second == 5);
}

TEST_CASE("different state areas are tracked independently", "[application][revision_tracker]") {
    RevisionTracker tracker;
    REQUIRE(tracker.StartSnapshot(kCharacter, kFingerprintA) == 1);
    tracker.NextEvent(kCharacter);
    tracker.NextEvent(kCharacter);

    // A different area's first snapshot is unaffected by character's revision count.
    CHECK(tracker.StartSnapshot(kLocation, kFingerprintA) == 1);
    CHECK(tracker.CurrentRevision(kCharacter) == 3);
    CHECK(tracker.CurrentRevision(kLocation) == 1);
}

TEST_CASE("NextEvent on one area leaves another area's revision untouched",
          "[application][revision_tracker]") {
    RevisionTracker tracker;
    tracker.StartSnapshot(kCharacter, kFingerprintA);
    tracker.StartSnapshot(kLocation, kFingerprintA);

    tracker.NextEvent(kCharacter);
    tracker.NextEvent(kCharacter);

    CHECK(tracker.CurrentRevision(kCharacter) == 3);
    // location was never touched by character's events; still at its snapshot revision.
    CHECK(tracker.CurrentRevision(kLocation) == 1);
}

TEST_CASE("two recovery snapshots back-to-back with an unchanged fingerprint reuse the same revision",
          "[application][revision_tracker]") {
    RevisionTracker tracker;
    REQUIRE(tracker.StartSnapshot(kCharacter, kFingerprintA) == 1);
    REQUIRE(tracker.StartSnapshot(kCharacter, kFingerprintA) == 1);
    CHECK(tracker.StartSnapshot(kCharacter, kFingerprintA) == 1);
}

TEST_CASE("two recovery snapshots back-to-back with a changed fingerprint each time keep advancing by one",
          "[application][revision_tracker]") {
    RevisionTracker tracker;
    REQUIRE(tracker.StartSnapshot(kCharacter, kFingerprintA) == 1);
    REQUIRE(tracker.StartSnapshot(kCharacter, kFingerprintB) == 2);
    CHECK(tracker.StartSnapshot(kCharacter, kFingerprintA) == 3);
}

TEST_CASE("CurrentRevision tracks the last assigned revision across several events",
          "[application][revision_tracker]") {
    RevisionTracker tracker;
    tracker.StartSnapshot(kCharacter, kFingerprintA);
    tracker.NextEvent(kCharacter);
    tracker.NextEvent(kCharacter);
    tracker.NextEvent(kCharacter);

    REQUIRE(tracker.CurrentRevision(kCharacter).has_value());
    CHECK(*tracker.CurrentRevision(kCharacter) == 4);
}
