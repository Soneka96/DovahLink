#include "application/state_publisher.hpp"

#include "application/application_test_support.hpp"
#include "application/revision_tracker.hpp"
#include "protocol/state_event_payload.hpp"
#include "protocol/state_snapshot_payload.hpp"

#include <catch2/catch_test_macros.hpp>
#include <gmock/gmock.h>

#include <boost/json/object.hpp>

#include <chrono>
#include <string>
#include <utility>

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

///  Builds a representative single-field data payload.
boost::json::object DataWithLevel(std::int64_t level) {
    boost::json::object data;
    data["level"] = level;
    return data;
}

} //  namespace

TEST_CASE("PublishSnapshot assigns revision 1 for a new state area",
          "[application][state_publisher]") {
    RevisionTracker revisionTracker;
    StrictMock<MockOutboundPublicationSink> sink;
    StatePublisher publisher(revisionTracker, sink);
    Envelope captured;
    EXPECT_CALL(sink, PublishSnapshot(std::string("character_level"), _))
        .WillOnce(Invoke([&](std::string, Envelope envelope) {
            captured = std::move(envelope);
        }));

    bool published =
        publisher.PublishSnapshot("character_level", DataWithLevel(5), kOccurredAt);

    CHECK(published);
    CHECK(captured.messageType == "state_snapshot");
    CHECK_FALSE(captured.sessionId.has_value());
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
    StatePublisher publisher(revisionTracker, sink);
    Envelope captured;
    EXPECT_CALL(sink, PublishSnapshot(std::string("empty_area"), _))
        .WillOnce(Invoke([&](std::string, Envelope envelope) {
            captured = std::move(envelope);
        }));

    bool published =
        publisher.PublishSnapshot("empty_area", boost::json::object{}, kOccurredAt);

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
    StatePublisher publisher(revisionTracker, sink);
    Envelope captured;
    EXPECT_CALL(sink, PublishSnapshot(std::string("character_level"), _))
        .Times(2)
        .WillRepeatedly(Invoke([&](std::string, Envelope envelope) {
            captured = std::move(envelope);
        }));

    REQUIRE(
        publisher.PublishSnapshot("character_level", DataWithLevel(5), kOccurredAt));
    bool published =
        publisher.PublishSnapshot("character_level", DataWithLevel(5), kOccurredAt);

    CHECK(published);
    auto payload = DecodeStateSnapshotPayload(captured.payload);
    REQUIRE(payload.has_value());
    CHECK(payload->revision == 1);
}

TEST_CASE("PublishSnapshot advances the revision when data changes",
          "[application][state_publisher]") {
    RevisionTracker revisionTracker;
    StrictMock<MockOutboundPublicationSink> sink;
    StatePublisher publisher(revisionTracker, sink);
    Envelope captured;
    EXPECT_CALL(sink, PublishSnapshot(std::string("character_level"), _))
        .Times(2)
        .WillRepeatedly(Invoke([&](std::string, Envelope envelope) {
            captured = std::move(envelope);
        }));

    REQUIRE(
        publisher.PublishSnapshot("character_level", DataWithLevel(5), kOccurredAt));
    bool published =
        publisher.PublishSnapshot("character_level", DataWithLevel(6), kOccurredAt);

    CHECK(published);
    auto payload = DecodeStateSnapshotPayload(captured.payload);
    REQUIRE(payload.has_value());
    CHECK(payload->revision == 2);
    CHECK(payload->data.at("level").as_int64() == 6);
}

TEST_CASE("PublishEvent declines when no snapshot baseline exists yet",
          "[application][state_publisher]") {
    RevisionTracker revisionTracker;
    StrictMock<MockOutboundPublicationSink> sink;
    StatePublisher publisher(revisionTracker, sink);

    bool published =
        publisher.PublishEvent("character_level", DataWithLevel(6), kOccurredAt);

    CHECK_FALSE(published);
}

TEST_CASE("PublishEvent publishes ordered events after a snapshot baseline",
          "[application][state_publisher]") {
    RevisionTracker revisionTracker;
    StrictMock<MockOutboundPublicationSink> sink;
    StatePublisher publisher(revisionTracker, sink);
    EXPECT_CALL(sink, PublishSnapshot(std::string("character_level"), _));
    REQUIRE(
        publisher.PublishSnapshot("character_level", DataWithLevel(5), kOccurredAt));
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
        publisher.PublishEvent("character_level", DataWithLevel(6), kOccurredAt);
    bool publishedSecond =
        publisher.PublishEvent("character_level", DataWithLevel(7), kOccurredAt);

    CHECK(publishedFirst);
    CHECK(publishedSecond);
    CHECK(firstEvent.messageType == "state_event");
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

TEST_CASE("Distinct state areas track independent revisions",
          "[application][state_publisher]") {
    RevisionTracker revisionTracker;
    StrictMock<MockOutboundPublicationSink> sink;
    StatePublisher publisher(revisionTracker, sink);
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

    publisher.PublishSnapshot("character_level", DataWithLevel(5), kOccurredAt);
    publisher.PublishSnapshot("character_xp", DataWithLevel(100), kOccurredAt);
    publisher.PublishSnapshot("character_level", DataWithLevel(6), kOccurredAt);

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
