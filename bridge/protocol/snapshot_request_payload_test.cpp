#include "protocol/snapshot_request_payload.hpp"

#include "protocol/fixture_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/object.hpp>
#include <boost/json/parse.hpp>

using dovahlink::protocol::test_support::DecodeFixtureEnvelope;

TEST_CASE("snapshot-request fixture decodes to the expected SnapshotRequestPayload",
          "[protocol][snapshot_request_payload]") {
    auto envelope = DecodeFixtureEnvelope("subscriptions/snapshot-request.json");
    auto snapshotRequest = dovahlink::protocol::DecodeSnapshotRequestPayload(envelope.payload);
    REQUIRE(snapshotRequest.has_value());
    CHECK(snapshotRequest->stateArea == "example_area");
    REQUIRE(snapshotRequest->knownRevision.has_value());
    CHECK(*snapshotRequest->knownRevision == 2);
}

TEST_CASE("snapshot-request decodes without knownRevision when it is absent",
          "[protocol][snapshot_request_payload]") {
    boost::json::object payload = boost::json::parse(R"({"stateArea": "example_area"})").get_object();
    auto snapshotRequest = dovahlink::protocol::DecodeSnapshotRequestPayload(payload);
    REQUIRE(snapshotRequest.has_value());
    CHECK_FALSE(snapshotRequest->knownRevision.has_value());
}
