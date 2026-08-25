#include "protocol/state_snapshot_payload.hpp"

#include "protocol/fixture_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/object.hpp>
#include <boost/json/parse.hpp>

using dovahlink::protocol::test_support::DecodeFixtureEnvelope;

TEST_CASE("state-snapshot fixture decodes to the expected StateSnapshotPayload",
          "[protocol][state_snapshot_payload]") {
  auto envelope = DecodeFixtureEnvelope("state/state-snapshot.json");
  auto snapshot =
      dovahlink::protocol::DecodeStateSnapshotPayload(envelope.payload);
  REQUIRE(snapshot.has_value());
  CHECK(snapshot->stateArea == "example_area");
  CHECK(snapshot->revision == 1);
  CHECK_FALSE(snapshot->occurredAt.empty());
}

TEST_CASE("state_snapshot is rejected when data is missing",
          "[protocol][state_snapshot_payload]") {
  boost::json::object payload =
      boost::json::parse(
          R"({"stateArea": "example_area", "revision": 1, "occurredAt": "2026-08-11T12:00:00Z"})")
          .get_object();
  auto snapshot = dovahlink::protocol::DecodeStateSnapshotPayload(payload);
  REQUIRE_FALSE(snapshot.has_value());
}

TEST_CASE("EncodeStateSnapshotPayload round-trips the state-snapshot fixture's "
          "payload",
          "[protocol][state_snapshot_payload]") {
  auto envelope = DecodeFixtureEnvelope("state/state-snapshot.json");
  auto original =
      dovahlink::protocol::DecodeStateSnapshotPayload(envelope.payload);
  REQUIRE(original.has_value());

  boost::json::object encoded =
      dovahlink::protocol::EncodeStateSnapshotPayload(*original);
  auto roundTripped = dovahlink::protocol::DecodeStateSnapshotPayload(encoded);

  REQUIRE(roundTripped.has_value());
  CHECK(roundTripped->stateArea == original->stateArea);
  CHECK(roundTripped->revision == original->revision);
  CHECK(roundTripped->occurredAt == original->occurredAt);
  CHECK(roundTripped->data == original->data);
}
