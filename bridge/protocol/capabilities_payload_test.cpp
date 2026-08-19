#include "protocol/capabilities_payload.hpp"

#include "protocol/fixture_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/object.hpp>
#include <boost/json/parse.hpp>

using dovahlink::protocol::test_support::DecodeFixtureEnvelope;

TEST_CASE("capabilities-bridge fixture decodes to the expected CapabilitiesPayload",
          "[protocol][capabilities_payload]") {
    auto envelope = DecodeFixtureEnvelope("capabilities/capabilities-bridge.json");
    auto capabilities = dovahlink::protocol::DecodeCapabilitiesPayload(envelope.payload);
    REQUIRE(capabilities.has_value());
    REQUIRE(capabilities->capabilities.size() == 1);
    CHECK(capabilities->capabilities[0].id == "state.character");
    CHECK(capabilities->capabilities[0].version == 1);
}

TEST_CASE("capabilities-client fixture decodes to an empty CapabilitiesPayload",
          "[protocol][capabilities_payload]") {
    auto envelope = DecodeFixtureEnvelope("capabilities/capabilities-client.json");
    auto capabilities = dovahlink::protocol::DecodeCapabilitiesPayload(envelope.payload);
    REQUIRE(capabilities.has_value());
    CHECK(capabilities->capabilities.empty());
}

TEST_CASE("capabilities is rejected when an entry is missing version", "[protocol][capabilities_payload]") {
    boost::json::object payload = boost::json::parse(R"({"capabilities": [{"id": "state.character"}]})").get_object();
    auto capabilities = dovahlink::protocol::DecodeCapabilitiesPayload(payload);
    REQUIRE_FALSE(capabilities.has_value());
}

TEST_CASE("capabilities is rejected when an entry is not an object", "[protocol][capabilities_payload]") {
    boost::json::object payload = boost::json::parse(R"({"capabilities": ["state.character"]})").get_object();
    auto capabilities = dovahlink::protocol::DecodeCapabilitiesPayload(payload);
    REQUIRE_FALSE(capabilities.has_value());
}

TEST_CASE("EncodeCapabilitiesPayload round-trips the capabilities-bridge fixture's payload",
          "[protocol][capabilities_payload]") {
    auto envelope = DecodeFixtureEnvelope("capabilities/capabilities-bridge.json");
    auto original = dovahlink::protocol::DecodeCapabilitiesPayload(envelope.payload);
    REQUIRE(original.has_value());

    boost::json::object encoded = dovahlink::protocol::EncodeCapabilitiesPayload(*original);
    auto roundTripped = dovahlink::protocol::DecodeCapabilitiesPayload(encoded);

    REQUIRE(roundTripped.has_value());
    REQUIRE(roundTripped->capabilities.size() == 1);
    CHECK(roundTripped->capabilities[0].id == "state.character");
    CHECK(roundTripped->capabilities[0].version == 1);
}

TEST_CASE("EncodeCapabilitiesPayload round-trips an empty capabilities list", "[protocol][capabilities_payload]") {
    auto envelope = DecodeFixtureEnvelope("capabilities/capabilities-client.json");
    auto original = dovahlink::protocol::DecodeCapabilitiesPayload(envelope.payload);
    REQUIRE(original.has_value());
    REQUIRE(original->capabilities.empty());

    boost::json::object encoded = dovahlink::protocol::EncodeCapabilitiesPayload(*original);
    auto roundTripped = dovahlink::protocol::DecodeCapabilitiesPayload(encoded);

    REQUIRE(roundTripped.has_value());
    CHECK(roundTripped->capabilities.empty());
}
