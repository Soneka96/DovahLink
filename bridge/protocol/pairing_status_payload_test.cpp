#include "protocol/pairing_status_payload.hpp"

#include "protocol/fixture_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/object.hpp>
#include <boost/json/parse.hpp>

using dovahlink::protocol::test_support::DecodeFixtureEnvelope;

TEST_CASE("pairing-status-available fixture decodes to the expected "
          "PairingStatusPayload",
          "[protocol][pairing_status_payload]") {
  auto envelope =
      DecodeFixtureEnvelope("pairing/pairing-status-available.json");
  auto status =
      dovahlink::protocol::DecodePairingStatusPayload(envelope.payload);
  REQUIRE(status.has_value());
  CHECK(status->state == "available");
  REQUIRE(status->expiresInSeconds.has_value());
  CHECK(*status->expiresInSeconds == 300);
}

TEST_CASE("pairing-status-in-progress fixture decodes to the expected "
          "PairingStatusPayload",
          "[protocol][pairing_status_payload]") {
  auto envelope =
      DecodeFixtureEnvelope("pairing/pairing-status-in-progress.json");
  auto status =
      dovahlink::protocol::DecodePairingStatusPayload(envelope.payload);
  REQUIRE(status.has_value());
  CHECK(status->state == "in_progress");
  REQUIRE(status->expiresInSeconds.has_value());
  CHECK(*status->expiresInSeconds == 187);
}

TEST_CASE("pairing-status-unavailable fixture decodes to the expected "
          "PairingStatusPayload",
          "[protocol][pairing_status_payload]") {
  auto envelope =
      DecodeFixtureEnvelope("pairing/pairing-status-unavailable.json");
  auto status =
      dovahlink::protocol::DecodePairingStatusPayload(envelope.payload);
  REQUIRE(status.has_value());
  CHECK(status->state == "unavailable");
  CHECK_FALSE(status->expiresInSeconds.has_value());
}

TEST_CASE("pairing_status decodes a missing expiresInSeconds as absent",
          "[protocol][pairing_status_payload]") {
  boost::json::object payload =
      boost::json::parse(R"({"state": "unavailable"})").get_object();
  auto status = dovahlink::protocol::DecodePairingStatusPayload(payload);
  REQUIRE(status.has_value());
  CHECK_FALSE(status->expiresInSeconds.has_value());
}

TEST_CASE("pairing_status accepts the other_device_pairing state",
          "[protocol][pairing_status_payload]") {
  boost::json::object payload =
      boost::json::parse(R"({"state": "other_device_pairing"})").get_object();
  auto status = dovahlink::protocol::DecodePairingStatusPayload(payload);
  REQUIRE(status.has_value());
  CHECK(status->state == "other_device_pairing");
}

TEST_CASE("pairing_status is rejected when expiresInSeconds is negative",
          "[protocol][pairing_status_payload]") {
  boost::json::object payload =
      boost::json::parse(R"({"state": "available", "expiresInSeconds": -1})")
          .get_object();
  auto status = dovahlink::protocol::DecodePairingStatusPayload(payload);
  REQUIRE_FALSE(status.has_value());
}

TEST_CASE("PairingStatusPayload round-trips through encode then decode",
          "[protocol][pairing_status_payload]") {
  dovahlink::protocol::PairingStatusPayload original{.state = "available",
                                                     .expiresInSeconds = 42};
  auto decoded = dovahlink::protocol::DecodePairingStatusPayload(
      dovahlink::protocol::EncodePairingStatusPayload(original));
  REQUIRE(decoded.has_value());
  CHECK(decoded->state == original.state);
  CHECK(decoded->expiresInSeconds == original.expiresInSeconds);
}

TEST_CASE("pairing_status is rejected when state is not a registered value",
          "[protocol][pairing_status_payload]") {
  boost::json::object payload =
      boost::json::parse(R"({"state": "sideways"})").get_object();
  auto status = dovahlink::protocol::DecodePairingStatusPayload(payload);
  REQUIRE_FALSE(status.has_value());
}

TEST_CASE("pairing_status is rejected when state has the wrong type",
          "[protocol][pairing_status_payload]") {
  boost::json::object payload =
      boost::json::parse(R"({"state": 1})").get_object();
  auto status = dovahlink::protocol::DecodePairingStatusPayload(payload);
  REQUIRE_FALSE(status.has_value());
}

TEST_CASE("EncodePairingStatusPayload omits expiresInSeconds entirely for "
          "other_device_pairing, "
          "not just as null",
          "[protocol][pairing_status_payload]") {
  auto encoded = dovahlink::protocol::EncodePairingStatusPayload(
      dovahlink::protocol::PairingStatusPayload{.state =
                                                    "other_device_pairing"});

  CHECK(encoded.if_contains("expiresInSeconds") == nullptr);
}

TEST_CASE("EncodePairingStatusPayload writes the state field directly",
          "[protocol][pairing_status_payload]") {
  auto encoded = dovahlink::protocol::EncodePairingStatusPayload(
      dovahlink::protocol::PairingStatusPayload{.state = "in_progress"});

  const boost::json::value *state = encoded.if_contains("state");
  REQUIRE(state != nullptr);
  REQUIRE(state->is_string());
  CHECK(state->get_string() == "in_progress");
  const boost::json::value *expiresInSeconds =
      encoded.if_contains("expiresInSeconds");
  REQUIRE(expiresInSeconds != nullptr);
  CHECK(expiresInSeconds->is_null());
}
