#include "application/state_publisher.hpp"

#include "application/application_test_support.hpp"
#include "application/revision_tracker.hpp"
#include "protocol/state_event_payload.hpp"
#include "protocol/state_snapshot_payload.hpp"

#include <catch2/catch_test_macros.hpp>
#include <gmock/gmock.h>

#include <boost/json/object.hpp>

#include <atomic>
#include <chrono>
#include <functional>
#include <future>
#include <mutex>
#include <string>
#include <thread>
#include <utility>
#include <vector>

using dovahlink::application::CaptureMode;
using dovahlink::application::RevisionTracker;
using dovahlink::application::StatePublisher;
using dovahlink::application::test_support::MockOutboundPublicationSink;
using dovahlink::protocol::DecodeStateEventPayload;
using dovahlink::protocol::DecodeStateSnapshotPayload;
using dovahlink::protocol::Envelope;
using testing::_;
using testing::Invoke;
using testing::StrictMock;
using namespace std::chrono;

namespace {

const system_clock::time_point kOccurredAt = sys_days{2026y / January / 1};
const std::string kPlayContextId = "context-1";

///  Freshness predicate that always reports the capture is still current.
bool AlwaysCurrent() { return true; }

///  Freshness predicate that always reports the capture is no longer
///  current.
bool NeverCurrent() { return false; }

///  Builds a representative single-field data payload.
boost::json::object DataWithLevel(std::int64_t level) {
    boost::json::object data;
    data["level"] = level;
    return data;
}

///  Provides a thread-safe sink that pauses its first handoff until the test
///  releases it, allowing concurrent publication attempts to be observed.
class BlockingPublicationSink final
    : public dovahlink::application::IOutboundPublicationSink {
  public:
    ///  Creates a sink with a release gate for its first publication handoff.
    BlockingPublicationSink()
        : releaseFirstFuture_(releaseFirst_.get_future().share()) {}

    ///  Records one publication, pausing the first handoff until released.
    void PublishSnapshot(std::string, Envelope envelope) override {
        RecordPublication(std::move(envelope));
    }

    ///  Records one publication, pausing the first handoff until released.
    void PublishEvent(std::string, Envelope envelope) override {
        RecordPublication(std::move(envelope));
    }

    ///  Records one publication, pausing the first handoff until released.
    ///  `StatePublisher` never calls this; present only to satisfy
    ///  `IOutboundPublicationSink`.
    void PublishRecoverySnapshot(std::string, Envelope envelope,
                                 std::int64_t) override {
        RecordPublication(std::move(envelope));
    }

    ///  Records one publication, pausing the first handoff until released.
    ///  `StatePublisher` never calls this; present only to satisfy
    ///  `IOutboundPublicationSink`.
    void PublishControl(Envelope envelope) override {
        RecordPublication(std::move(envelope));
    }

    ///  Waits until the first publication handoff has entered the sink.
    void WaitForFirstPublication() { firstEntered_.get_future().wait(); }

    ///  Releases the first publication handoff.
    void ReleaseFirstPublication() { releaseFirst_.set_value(); }

    ///  Returns whether two publication handoffs overlapped.
    [[nodiscard]] bool HandoffsOverlapped() const {
        return overlapped_.load();
    }

    ///  Returns a thread-safe copy of recorded publications.
    [[nodiscard]] std::vector<Envelope> Publications() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return publications_;
    }

  private:
    ///  Records one publication and pauses the first handoff.
    void RecordPublication(Envelope envelope) {
        bool expected = false;
        if (!active_.compare_exchange_strong(expected, true)) {
            overlapped_.store(true);
        }

        if (!firstCall_.exchange(true)) {
            firstEntered_.set_value();
            releaseFirstFuture_.wait();
        }

        {
            std::lock_guard<std::mutex> lock(mutex_);
            publications_.push_back(std::move(envelope));
        }
        active_.store(false);
    }
    ///  Signals entry into the first publication handoff.
    std::promise<void> firstEntered_;

    ///  Releases the first publication handoff.
    std::promise<void> releaseFirst_;

    ///  Waits for release of the first publication handoff.
    std::shared_future<void> releaseFirstFuture_;

    ///  Ensures only the first publication handoff waits on the release gate.
    std::atomic<bool> firstCall_ = false;

    ///  Tracks whether a second handoff overlaps the first.
    std::atomic<bool> active_ = false;

    ///  Records overlapping handoffs.
    std::atomic<bool> overlapped_ = false;

    ///  Synchronizes access to recorded publications.
    mutable std::mutex mutex_;

    ///  Publications received by the sink.
    std::vector<Envelope> publications_;
};

} //  namespace

TEST_CASE("PublishSnapshot assigns revision 1 for a new state area",
          "[application][state_publisher]") {
    RevisionTracker revisionTracker;
    StrictMock<MockOutboundPublicationSink> sink;
    StatePublisher publisher(sink);
    Envelope captured;
    EXPECT_CALL(sink, PublishSnapshot(std::string("character_level"), _))
        .WillOnce(Invoke([&](std::string, Envelope envelope) {
            captured = std::move(envelope);
        }));

    bool published =
        publisher.PublishSnapshot("character_level", kPlayContextId,
                                  revisionTracker, DataWithLevel(5), kOccurredAt,
                                  AlwaysCurrent);

    CHECK(published);
    CHECK(captured.messageType == "state_snapshot");
    CHECK_FALSE(captured.sessionId.has_value());
    CHECK(captured.playContextId == kPlayContextId);
    auto payload = DecodeStateSnapshotPayload(captured.payload);
    REQUIRE(payload.has_value());
    CHECK(payload->stateArea == "character_level");
    CHECK(payload->revision == 1);
    CHECK(payload->occurredAt == "2026-01-01T00:00:00Z");
    CHECK(payload->data.at("level").as_int64() == 5);
}

TEST_CASE("PublishSnapshot publishes an empty data object as-is",
          "[application][state_publisher]") {
    RevisionTracker revisionTracker;
    StrictMock<MockOutboundPublicationSink> sink;
    StatePublisher publisher(sink);
    Envelope captured;
    EXPECT_CALL(sink, PublishSnapshot(std::string("empty_area"), _))
        .WillOnce(Invoke([&](std::string, Envelope envelope) {
            captured = std::move(envelope);
        }));

    bool published = publisher.PublishSnapshot(
        "empty_area", kPlayContextId, revisionTracker, boost::json::object{},
        kOccurredAt, AlwaysCurrent);

    CHECK(published);
    auto payload = DecodeStateSnapshotPayload(captured.payload);
    REQUIRE(payload.has_value());
    CHECK(payload->data.empty());
}

TEST_CASE("PublishSnapshot keeps the same revision for unchanged data but "
          "still publishes",
          "[application][state_publisher]") {
    RevisionTracker revisionTracker;
    StrictMock<MockOutboundPublicationSink> sink;
    StatePublisher publisher(sink);
    Envelope captured;
    EXPECT_CALL(sink, PublishSnapshot(std::string("character_level"), _))
        .Times(2)
        .WillRepeatedly(Invoke([&](std::string, Envelope envelope) {
            captured = std::move(envelope);
        }));

    REQUIRE(publisher.PublishSnapshot("character_level", kPlayContextId,
                                      revisionTracker, DataWithLevel(5),
                                      kOccurredAt, AlwaysCurrent));
    bool published =
        publisher.PublishSnapshot("character_level", kPlayContextId,
                                  revisionTracker, DataWithLevel(5), kOccurredAt,
                                  AlwaysCurrent);

    CHECK(published);
    auto payload = DecodeStateSnapshotPayload(captured.payload);
    REQUIRE(payload.has_value());
    CHECK(payload->revision == 1);
}

TEST_CASE("PublishSnapshot advances the revision when data changes",
          "[application][state_publisher]") {
    RevisionTracker revisionTracker;
    StrictMock<MockOutboundPublicationSink> sink;
    StatePublisher publisher(sink);
    Envelope captured;
    EXPECT_CALL(sink, PublishSnapshot(std::string("character_level"), _))
        .Times(2)
        .WillRepeatedly(Invoke([&](std::string, Envelope envelope) {
            captured = std::move(envelope);
        }));

    REQUIRE(publisher.PublishSnapshot("character_level", kPlayContextId,
                                      revisionTracker, DataWithLevel(5),
                                      kOccurredAt, AlwaysCurrent));
    bool published =
        publisher.PublishSnapshot("character_level", kPlayContextId,
                                  revisionTracker, DataWithLevel(6), kOccurredAt,
                                  AlwaysCurrent);

    CHECK(published);
    auto payload = DecodeStateSnapshotPayload(captured.payload);
    REQUIRE(payload.has_value());
    CHECK(payload->revision == 2);
    CHECK(payload->data.at("level").as_int64() == 6);
}

TEST_CASE("PublishSnapshot discards an already-built envelope when "
          "stillCurrent reports false",
          "[application][state_publisher]") {
    //  StrictMock<MockOutboundPublicationSink> with no expectation fails the
    //  test if the sink is reached at all -- proving the freshness gate
    //  suppresses the handoff, not merely returns false alongside it.
    RevisionTracker revisionTracker;
    StrictMock<MockOutboundPublicationSink> sink;
    StatePublisher publisher(sink);

    bool published =
        publisher.PublishSnapshot("character_level", kPlayContextId,
                                  revisionTracker, DataWithLevel(5), kOccurredAt,
                                  NeverCurrent);

    CHECK_FALSE(published);
    CHECK(revisionTracker.CurrentRevision("character_level") == 1);
}

TEST_CASE("PublishEvent declines when no snapshot baseline exists yet",
          "[application][state_publisher]") {
    RevisionTracker revisionTracker;
    StrictMock<MockOutboundPublicationSink> sink;
    StatePublisher publisher(sink);

    bool published =
        publisher.PublishEvent("character_level", kPlayContextId,
                               revisionTracker, DataWithLevel(6), kOccurredAt,
                               AlwaysCurrent);

    CHECK_FALSE(published);
}

TEST_CASE("PublishEvent publishes ordered events after a snapshot baseline",
          "[application][state_publisher]") {
    RevisionTracker revisionTracker;
    StrictMock<MockOutboundPublicationSink> sink;
    StatePublisher publisher(sink);
    EXPECT_CALL(sink, PublishSnapshot(std::string("character_level"), _));
    REQUIRE(publisher.PublishSnapshot("character_level", kPlayContextId,
                                      revisionTracker, DataWithLevel(5),
                                      kOccurredAt, AlwaysCurrent));
    Envelope firstEvent;
    Envelope secondEvent;
    EXPECT_CALL(sink, PublishEvent(std::string("character_level"), _))
        .WillOnce(Invoke([&](std::string, Envelope envelope) {
            firstEvent = std::move(envelope);
        }))
        .WillOnce(Invoke([&](std::string, Envelope envelope) {
            secondEvent = std::move(envelope);
        }));

    bool publishedFirst =
        publisher.PublishEvent("character_level", kPlayContextId,
                               revisionTracker, DataWithLevel(6), kOccurredAt,
                               AlwaysCurrent);
    bool publishedSecond =
        publisher.PublishEvent("character_level", kPlayContextId,
                               revisionTracker, DataWithLevel(7), kOccurredAt,
                               AlwaysCurrent);

    CHECK(publishedFirst);
    CHECK(publishedSecond);
    CHECK(firstEvent.messageType == "state_event");
    CHECK(firstEvent.playContextId == kPlayContextId);
    auto firstPayload = DecodeStateEventPayload(firstEvent.payload);
    REQUIRE(firstPayload.has_value());
    CHECK(firstPayload->baseRevision == 1);
    CHECK(firstPayload->revision == 2);
    CHECK(firstPayload->occurredAt == "2026-01-01T00:00:00Z");
    auto secondPayload = DecodeStateEventPayload(secondEvent.payload);
    REQUIRE(secondPayload.has_value());
    CHECK(secondPayload->baseRevision == 2);
    CHECK(secondPayload->revision == 3);
}

TEST_CASE("PublishEvent discards an already-built envelope when stillCurrent "
          "reports false",
          "[application][state_publisher]") {
    RevisionTracker revisionTracker;
    revisionTracker.StartSnapshot("character_level", "{\"level\":5}");
    StrictMock<MockOutboundPublicationSink> sink;
    StatePublisher publisher(sink);

    bool published =
        publisher.PublishEvent("character_level", kPlayContextId,
                               revisionTracker, DataWithLevel(6), kOccurredAt,
                               NeverCurrent);

    CHECK_FALSE(published);
    CHECK(revisionTracker.CurrentRevision("character_level") == 2);
}

TEST_CASE("Distinct state areas track independent revisions",
          "[application][state_publisher]") {
    RevisionTracker revisionTracker;
    StrictMock<MockOutboundPublicationSink> sink;
    StatePublisher publisher(sink);
    Envelope firstLevelEnvelope;
    Envelope secondLevelEnvelope;
    Envelope xpEnvelope;
    EXPECT_CALL(sink, PublishSnapshot(std::string("character_level"), _))
        .WillOnce(Invoke([&](std::string, Envelope envelope) {
            firstLevelEnvelope = std::move(envelope);
        }))
        .WillOnce(Invoke([&](std::string, Envelope envelope) {
            secondLevelEnvelope = std::move(envelope);
        }));
    EXPECT_CALL(sink, PublishSnapshot(std::string("character_xp"), _))
        .WillOnce(Invoke([&](std::string, Envelope envelope) {
            xpEnvelope = std::move(envelope);
        }));

    publisher.PublishSnapshot("character_level", kPlayContextId,
                              revisionTracker, DataWithLevel(5), kOccurredAt,
                              AlwaysCurrent);
    publisher.PublishSnapshot("character_xp", kPlayContextId, revisionTracker,
                              DataWithLevel(100), kOccurredAt, AlwaysCurrent);
    publisher.PublishSnapshot("character_level", kPlayContextId,
                              revisionTracker, DataWithLevel(6), kOccurredAt,
                              AlwaysCurrent);

    auto firstLevelPayload = DecodeStateSnapshotPayload(firstLevelEnvelope.payload);
    REQUIRE(firstLevelPayload.has_value());
    CHECK(firstLevelPayload->revision == 1);
    auto secondLevelPayload =
        DecodeStateSnapshotPayload(secondLevelEnvelope.payload);
    REQUIRE(secondLevelPayload.has_value());
    CHECK(secondLevelPayload->revision == 2);
    auto xpPayload = DecodeStateSnapshotPayload(xpEnvelope.payload);
    REQUIRE(xpPayload.has_value());
    CHECK(xpPayload->revision == 1);
}

TEST_CASE("The same state area tracks independent revisions across "
          "different revision trackers",
          "[application][state_publisher]") {
    //  Two RevisionTracker instances stand in for two different play
    //  contexts' own revision authorities: publishing "character_level"
    //  through one must never advance or be constrained by the other's
    //  sequence for the same state-area string.
    RevisionTracker contextATracker;
    RevisionTracker contextBTracker;
    StrictMock<MockOutboundPublicationSink> sink;
    StatePublisher publisher(sink);
    Envelope contextAEnvelope;
    Envelope contextBEnvelope;
    EXPECT_CALL(sink, PublishSnapshot(std::string("character_level"), _))
        .WillOnce(Invoke([&](std::string, Envelope envelope) {
            contextAEnvelope = std::move(envelope);
        }))
        .WillOnce(Invoke([&](std::string, Envelope envelope) {
            contextBEnvelope = std::move(envelope);
        }));

    publisher.PublishSnapshot("character_level", "context-a", contextATracker,
                              DataWithLevel(5), kOccurredAt, AlwaysCurrent);
    publisher.PublishSnapshot("character_level", "context-b", contextBTracker,
                              DataWithLevel(9), kOccurredAt, AlwaysCurrent);

    auto contextAPayload = DecodeStateSnapshotPayload(contextAEnvelope.payload);
    REQUIRE(contextAPayload.has_value());
    CHECK(contextAPayload->revision == 1);
    CHECK(contextAPayload->data.at("level").as_int64() == 5);
    auto contextBPayload = DecodeStateSnapshotPayload(contextBEnvelope.payload);
    REQUIRE(contextBPayload.has_value());
    CHECK(contextBPayload->revision == 1);
    CHECK(contextBPayload->data.at("level").as_int64() == 9);
    CHECK(contextAEnvelope.playContextId == "context-a");
    CHECK(contextBEnvelope.playContextId == "context-b");
}

TEST_CASE("Concurrent snapshots for one state area reach the sink in revision "
          "order",
          "[application][state_publisher]") {
    RevisionTracker revisionTracker;
    BlockingPublicationSink sink;
    StatePublisher publisher(sink);
    std::promise<void> secondStarted;
    std::future<void> secondStartedFuture = secondStarted.get_future();
    std::promise<void> secondFinished;
    std::future<void> secondFinishedFuture = secondFinished.get_future();
    bool firstPublished = false;
    bool secondPublished = false;

    std::thread firstPublisher([&] {
        firstPublished = publisher.PublishSnapshot(
            "character_level", kPlayContextId, revisionTracker,
            DataWithLevel(5), kOccurredAt, AlwaysCurrent);
    });

    sink.WaitForFirstPublication();
    std::thread secondPublisher([&] {
        secondStarted.set_value();
        secondPublished = publisher.PublishSnapshot(
            "character_level", kPlayContextId, revisionTracker,
            DataWithLevel(6), kOccurredAt, AlwaysCurrent);
        secondFinished.set_value();
    });

    secondStartedFuture.wait();
    CHECK(secondFinishedFuture.wait_for(100ms) == std::future_status::timeout);
    sink.ReleaseFirstPublication();
    firstPublisher.join();
    secondPublisher.join();

    CHECK(firstPublished);
    CHECK(secondPublished);
    CHECK_FALSE(sink.HandoffsOverlapped());

    auto publications = sink.Publications();
    REQUIRE(publications.size() == 2);
    auto firstPayload = DecodeStateSnapshotPayload(publications[0].payload);
    auto secondPayload = DecodeStateSnapshotPayload(publications[1].payload);
    REQUIRE(firstPayload.has_value());
    REQUIRE(secondPayload.has_value());
    CHECK(firstPayload->revision == 1);
    CHECK(firstPayload->data.at("level").as_int64() == 5);
    CHECK(secondPayload->revision == 2);
    CHECK(secondPayload->data.at("level").as_int64() == 6);
}

TEST_CASE("Concurrent events for one state area reach the sink in revision "
          "order",
          "[application][state_publisher]") {
    RevisionTracker revisionTracker;
    revisionTracker.StartSnapshot("character_level", "{\"level\":5}");
    BlockingPublicationSink sink;
    StatePublisher publisher(sink);
    std::promise<void> secondStarted;
    std::future<void> secondStartedFuture = secondStarted.get_future();
    std::promise<void> secondFinished;
    std::future<void> secondFinishedFuture = secondFinished.get_future();
    bool firstPublished = false;
    bool secondPublished = false;

    std::thread firstPublisher([&] {
        firstPublished = publisher.PublishEvent(
            "character_level", kPlayContextId, revisionTracker,
            DataWithLevel(6), kOccurredAt, AlwaysCurrent);
    });

    sink.WaitForFirstPublication();
    std::thread secondPublisher([&] {
        secondStarted.set_value();
        secondPublished = publisher.PublishEvent(
            "character_level", kPlayContextId, revisionTracker,
            DataWithLevel(7), kOccurredAt, AlwaysCurrent);
        secondFinished.set_value();
    });

    secondStartedFuture.wait();
    CHECK(secondFinishedFuture.wait_for(100ms) == std::future_status::timeout);
    sink.ReleaseFirstPublication();
    firstPublisher.join();
    secondPublisher.join();

    CHECK(firstPublished);
    CHECK(secondPublished);
    CHECK_FALSE(sink.HandoffsOverlapped());

    auto publications = sink.Publications();
    REQUIRE(publications.size() == 2);
    auto firstPayload = DecodeStateEventPayload(publications[0].payload);
    auto secondPayload = DecodeStateEventPayload(publications[1].payload);
    REQUIRE(firstPayload.has_value());
    REQUIRE(secondPayload.has_value());
    CHECK(firstPayload->baseRevision == 1);
    CHECK(firstPayload->revision == 2);
    CHECK(secondPayload->baseRevision == 2);
    CHECK(secondPayload->revision == 3);
}

TEST_CASE("PublishCapture establishes a Snapshot baseline for a requested "
          "Event with no prior baseline",
          "[application][state_publisher]") {
    RevisionTracker revisionTracker;
    StrictMock<MockOutboundPublicationSink> sink;
    StatePublisher publisher(sink);
    Envelope captured;
    EXPECT_CALL(sink, PublishSnapshot(std::string("character_level"), _))
        .WillOnce(Invoke([&](std::string, Envelope envelope) {
            captured = std::move(envelope);
        }));

    bool published = publisher.PublishCapture(
        "character_level", kPlayContextId, revisionTracker, CaptureMode::kEvent,
        DataWithLevel(5), kOccurredAt, AlwaysCurrent);

    CHECK(published);
    CHECK(captured.messageType == "state_snapshot");
    CHECK(captured.playContextId == kPlayContextId);
    auto payload = DecodeStateSnapshotPayload(captured.payload);
    REQUIRE(payload.has_value());
    CHECK(payload->revision == 1);
    CHECK(payload->data.at("level").as_int64() == 5);
}

TEST_CASE("PublishCapture advances an existing baseline as an ordered Event",
          "[application][state_publisher]") {
    RevisionTracker revisionTracker;
    StrictMock<MockOutboundPublicationSink> sink;
    StatePublisher publisher(sink);
    EXPECT_CALL(sink, PublishSnapshot(std::string("character_level"), _));
    REQUIRE(publisher.PublishCapture("character_level", kPlayContextId,
                                     revisionTracker, CaptureMode::kEvent,
                                     DataWithLevel(5), kOccurredAt,
                                     AlwaysCurrent));
    Envelope captured;
    EXPECT_CALL(sink, PublishEvent(std::string("character_level"), _))
        .WillOnce(Invoke([&](std::string, Envelope envelope) {
            captured = std::move(envelope);
        }));

    bool published = publisher.PublishCapture(
        "character_level", kPlayContextId, revisionTracker, CaptureMode::kEvent,
        DataWithLevel(6), kOccurredAt, AlwaysCurrent);

    CHECK(published);
    CHECK(captured.messageType == "state_event");
    CHECK(captured.playContextId == kPlayContextId);
    auto payload = DecodeStateEventPayload(captured.payload);
    REQUIRE(payload.has_value());
    CHECK(payload->baseRevision == 1);
    CHECK(payload->revision == 2);
}

TEST_CASE("PublishCapture tracks independent revisions across different "
          "revision trackers for the same state area",
          "[application][state_publisher]") {
    RevisionTracker contextATracker;
    RevisionTracker contextBTracker;
    StrictMock<MockOutboundPublicationSink> sink;
    StatePublisher publisher(sink);
    EXPECT_CALL(sink, PublishSnapshot(std::string("character_level"), _))
        .Times(2);

    bool publishedA = publisher.PublishCapture(
        "character_level", "context-a", contextATracker, CaptureMode::kEvent,
        DataWithLevel(5), kOccurredAt, AlwaysCurrent);
    bool publishedB = publisher.PublishCapture(
        "character_level", "context-b", contextBTracker, CaptureMode::kEvent,
        DataWithLevel(9), kOccurredAt, AlwaysCurrent);

    CHECK(publishedA);
    CHECK(publishedB);
    CHECK(contextATracker.CurrentRevision("character_level") == 1);
    CHECK(contextBTracker.CurrentRevision("character_level") == 1);
}

TEST_CASE("PublishCapture honors an explicit Snapshot request even after a "
          "baseline already exists",
          "[application][state_publisher]") {
    RevisionTracker revisionTracker;
    StrictMock<MockOutboundPublicationSink> sink;
    StatePublisher publisher(sink);
    EXPECT_CALL(sink, PublishSnapshot(std::string("character_level"), _))
        .Times(2);
    REQUIRE(publisher.PublishCapture("character_level", kPlayContextId,
                                     revisionTracker, CaptureMode::kEvent,
                                     DataWithLevel(5), kOccurredAt,
                                     AlwaysCurrent));

    bool published = publisher.PublishCapture(
        "character_level", kPlayContextId, revisionTracker,
        CaptureMode::kSnapshot, DataWithLevel(6), kOccurredAt, AlwaysCurrent);

    CHECK(published);
}

TEST_CASE("PublishCapture discards an already-built envelope when "
          "stillCurrent reports false",
          "[application][state_publisher]") {
    RevisionTracker revisionTracker;
    StrictMock<MockOutboundPublicationSink> sink;
    StatePublisher publisher(sink);

    bool published = publisher.PublishCapture(
        "character_level", kPlayContextId, revisionTracker, CaptureMode::kEvent,
        DataWithLevel(5), kOccurredAt, NeverCurrent);

    CHECK_FALSE(published);
    CHECK(revisionTracker.CurrentRevision("character_level") == 1);
}
