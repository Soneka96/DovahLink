#include "protocol/state_event_payload.hpp"

#include "protocol/fixture_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/object.hpp>
#include <boost/json/parse.hpp>

using dovahlink::protocol::test_support::DecodeFixtureEnvelope;

TEST_CASE("state-event fixture decodes to the expected StateEventPayload",
          "[protocol][state_event_payload]") {
    auto envelope = DecodeFixtureEnvelope("state/state-event.json");
    auto event = dovahlink::protocol::DecodeStateEventPayload(envelope.payload);
    REQUIRE(event.has_value());
    CHECK(event->stateArea == "example_area");
    CHECK(event->baseRevision == 1);
    CHECK(event->revision == 2);
}

TEST_CASE("state-event-revision-gap fixture decodes with revision higher than "
          "baseRevision + 1 "
          "from the prior event",
          "[protocol][state_event_payload]") {
    auto envelope = DecodeFixtureEnvelope("state/state-event-revision-gap.json");
    auto event = dovahlink::protocol::DecodeStateEventPayload(envelope.payload);
    REQUIRE(event.has_value());
    CHECK(event->baseRevision == 5);
    CHECK(event->revision == 6);
}

TEST_CASE(
    "state-event-duplicate fixture decodes to the same revision as state-event",
    "[protocol][state_event_payload]") {
    auto envelope = DecodeFixtureEnvelope("state/state-event-duplicate.json");
    auto event = dovahlink::protocol::DecodeStateEventPayload(envelope.payload);
    REQUIRE(event.has_value());
    CHECK(event->baseRevision == 1);
    CHECK(event->revision == 2);
}

TEST_CASE("state-event-stale fixture decodes to a revision below a later "
          "current revision",
          "[protocol][state_event_payload]") {
    auto envelope = DecodeFixtureEnvelope("state/state-event-stale.json");
    auto event = dovahlink::protocol::DecodeStateEventPayload(envelope.payload);
    REQUIRE(event.has_value());
    CHECK(event->baseRevision == 0);
    CHECK(event->revision == 1);
}

TEST_CASE("state_event is rejected when baseRevision is missing",
          "[protocol][state_event_payload]") {
    boost::json::object payload =
        boost::json::parse(
            R"({"stateArea": "example_area", "revision": 2, "occurredAt": "2026-08-11T12:00:00Z", "data": {}})")
            .get_object();
    auto event = dovahlink::protocol::DecodeStateEventPayload(payload);
    REQUIRE_FALSE(event.has_value());
}
