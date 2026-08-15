#include "application/revision_tracker.hpp"

#include <catch2/catch_test_macros.hpp>

#include <string>

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
