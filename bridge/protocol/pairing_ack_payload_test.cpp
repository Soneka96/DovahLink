#include "protocol/pairing_ack_payload.hpp"

#include "protocol/fixture_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/object.hpp>
#include <boost/json/parse.hpp>

using dovahlink::protocol::test_support::DecodeFixtureEnvelope;

TEST_CASE("pairing-ack fixture decodes to the expected PairingAckPayload",
          "[protocol][pairing_ack_payload]") {
  auto envelope = DecodeFixtureEnvelope("pairing/pairing-ack.json");
  auto ack = dovahlink::protocol::DecodePairingAckPayload(envelope.payload);
  REQUIRE(ack.has_value());
  CHECK(ack->credential == "a1b2c3d4e5f6");
}

TEST_CASE("pairing_ack is rejected when credential is missing",
          "[protocol][pairing_ack_payload]") {
  boost::json::object payload = boost::json::parse(R"({})").get_object();
  auto ack = dovahlink::protocol::DecodePairingAckPayload(payload);
  REQUIRE_FALSE(ack.has_value());
}

TEST_CASE("pairing_ack is rejected when credential is an empty string",
          "[protocol][pairing_ack_payload]") {
  boost::json::object payload =
      boost::json::parse(R"({"credential": ""})").get_object();
  auto ack = dovahlink::protocol::DecodePairingAckPayload(payload);
  REQUIRE_FALSE(ack.has_value());
}

TEST_CASE("pairing_ack is rejected when credential has the wrong type",
          "[protocol][pairing_ack_payload]") {
  boost::json::object payload =
      boost::json::parse(R"({"credential": 12345})").get_object();
  auto ack = dovahlink::protocol::DecodePairingAckPayload(payload);
  REQUIRE_FALSE(ack.has_value());
}
