#include "protocol/pairing_outcome_payload.hpp"

#include "protocol/fixture_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/object.hpp>
#include <boost/json/parse.hpp>

#include <optional>
#include <string>

using dovahlink::protocol::test_support::DecodeFixtureEnvelope;

TEST_CASE("pairing-outcome-credential-issued fixture decodes with a credential "
          "and displayName, "
          "no shortId",
          "[protocol][pairing_outcome_payload]") {
  auto envelope =
      DecodeFixtureEnvelope("pairing/pairing-outcome-credential-issued.json");
  auto outcome =
      dovahlink::protocol::DecodePairingOutcomePayload(envelope.payload);
  REQUIRE(outcome.has_value());
  CHECK(outcome->outcome == "credential_issued");
  REQUIRE(outcome->credential.has_value());
  CHECK(*outcome->credential == "a1b2c3d4e5f6");
  CHECK_FALSE(outcome->shortId.has_value());
  REQUIRE(outcome->displayName.has_value());
  CHECK(*outcome->displayName == "My PC");
}

TEST_CASE(
    "pairing-outcome-trusted fixture decodes with a credential and shortId",
    "[protocol][pairing_outcome_payload]") {
  auto envelope = DecodeFixtureEnvelope("pairing/pairing-outcome-trusted.json");
  auto outcome =
      dovahlink::protocol::DecodePairingOutcomePayload(envelope.payload);
  REQUIRE(outcome.has_value());
  CHECK(outcome->outcome == "trusted");
  REQUIRE(outcome->credential.has_value());
  REQUIRE(outcome->shortId.has_value());
  CHECK(*outcome->shortId == "12345");
}

TEST_CASE("pairing-outcome-already-trusted fixture decodes with a credential "
          "and shortId",
          "[protocol][pairing_outcome_payload]") {
  auto envelope =
      DecodeFixtureEnvelope("pairing/pairing-outcome-already-trusted.json");
  auto outcome =
      dovahlink::protocol::DecodePairingOutcomePayload(envelope.payload);
  REQUIRE(outcome.has_value());
  CHECK(outcome->outcome == "already_trusted");
  REQUIRE(outcome->credential.has_value());
  REQUIRE(outcome->shortId.has_value());
}

TEST_CASE("pairing-outcome-expired fixture decodes with no credential",
          "[protocol][pairing_outcome_payload]") {
  auto envelope = DecodeFixtureEnvelope("pairing/pairing-outcome-expired.json");
  auto outcome =
      dovahlink::protocol::DecodePairingOutcomePayload(envelope.payload);
  REQUIRE(outcome.has_value());
  CHECK(outcome->outcome == "expired");
  CHECK_FALSE(outcome->credential.has_value());
  CHECK_FALSE(outcome->shortId.has_value());
  CHECK_FALSE(outcome->displayName.has_value());
}

TEST_CASE("pairing-outcome-invalid fixture decodes with no credential",
          "[protocol][pairing_outcome_payload]") {
  auto envelope = DecodeFixtureEnvelope("pairing/pairing-outcome-invalid.json");
  auto outcome =
      dovahlink::protocol::DecodePairingOutcomePayload(envelope.payload);
  REQUIRE(outcome.has_value());
  CHECK(outcome->outcome == "invalid");
}

TEST_CASE("pairing-outcome-pacing-limited fixture decodes with a "
          "retryAfterSeconds and no credential",
          "[protocol][pairing_outcome_payload]") {
  auto envelope =
      DecodeFixtureEnvelope("pairing/pairing-outcome-pacing-limited.json");
  auto outcome =
      dovahlink::protocol::DecodePairingOutcomePayload(envelope.payload);
  REQUIRE(outcome.has_value());
  CHECK(outcome->outcome == "pacing_limited");
  CHECK_FALSE(outcome->credential.has_value());
  REQUIRE(outcome->retryAfterSeconds.has_value());
  CHECK(*outcome->retryAfterSeconds == 1);
}

TEST_CASE("pairing-outcome-hard-limit-reached fixture decodes with no "
          "credential and no "
          "retryAfterSeconds",
          "[protocol][pairing_outcome_payload]") {
  auto envelope =
      DecodeFixtureEnvelope("pairing/pairing-outcome-hard-limit-reached.json");
  auto outcome =
      dovahlink::protocol::DecodePairingOutcomePayload(envelope.payload);
  REQUIRE(outcome.has_value());
  CHECK(outcome->outcome == "hard_limit_reached");
  CHECK_FALSE(outcome->credential.has_value());
  CHECK_FALSE(outcome->retryAfterSeconds.has_value());
}

TEST_CASE("pairing_outcome no longer accepts the retired rate_limited value",
          "[protocol][pairing_outcome_payload]") {
  boost::json::object payload =
      boost::json::parse(
          R"({"outcome": "rate_limited", "credential": null, "shortId": null, )"
          R"("displayName": null})")
          .get_object();
  auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(payload);
  REQUIRE_FALSE(outcome.has_value());
}

TEST_CASE(
    "pairing-outcome-pending-not-found fixture decodes with no credential",
    "[protocol][pairing_outcome_payload]") {
  auto envelope =
      DecodeFixtureEnvelope("pairing/pairing-outcome-pending-not-found.json");
  auto outcome =
      dovahlink::protocol::DecodePairingOutcomePayload(envelope.payload);
  REQUIRE(outcome.has_value());
  CHECK(outcome->outcome == "pending_not_found");
}

TEST_CASE("pairing-outcome-renotified fixture decodes with no credential and "
          "no retryAfterSeconds",
          "[protocol][pairing_outcome_payload]") {
  auto envelope =
      DecodeFixtureEnvelope("pairing/pairing-outcome-renotified.json");
  auto outcome =
      dovahlink::protocol::DecodePairingOutcomePayload(envelope.payload);
  REQUIRE(outcome.has_value());
  CHECK(outcome->outcome == "renotified");
  CHECK_FALSE(outcome->credential.has_value());
  CHECK_FALSE(outcome->retryAfterSeconds.has_value());
}

TEST_CASE("pairing-outcome-renotify-cooldown fixture decodes with a "
          "retryAfterSeconds and no credential",
          "[protocol][pairing_outcome_payload]") {
  auto envelope =
      DecodeFixtureEnvelope("pairing/pairing-outcome-renotify-cooldown.json");
  auto outcome =
      dovahlink::protocol::DecodePairingOutcomePayload(envelope.payload);
  REQUIRE(outcome.has_value());
  CHECK(outcome->outcome == "renotify_cooldown");
  CHECK_FALSE(outcome->credential.has_value());
  REQUIRE(outcome->retryAfterSeconds.has_value());
  CHECK(*outcome->retryAfterSeconds == 3);
}

TEST_CASE("pairing-outcome-already-idle fixture decodes with no credential",
          "[protocol][pairing_outcome_payload]") {
  auto envelope =
      DecodeFixtureEnvelope("pairing/pairing-outcome-already-idle.json");
  auto outcome =
      dovahlink::protocol::DecodePairingOutcomePayload(envelope.payload);
  REQUIRE(outcome.has_value());
  CHECK(outcome->outcome == "already_idle");
  CHECK_FALSE(outcome->credential.has_value());
}

TEST_CASE("pairing-outcome-cancelled fixture decodes with no credential",
          "[protocol][pairing_outcome_payload]") {
  auto envelope =
      DecodeFixtureEnvelope("pairing/pairing-outcome-cancelled.json");
  auto outcome =
      dovahlink::protocol::DecodePairingOutcomePayload(envelope.payload);
  REQUIRE(outcome.has_value());
  CHECK(outcome->outcome == "cancelled");
  CHECK_FALSE(outcome->credential.has_value());
}

TEST_CASE("PairingOutcomePayload round-trips through encode then decode",
          "[protocol][pairing_outcome_payload]") {
  dovahlink::protocol::PairingOutcomePayload original{
      .outcome = "trusted",
      .credential = std::string("a1b2c3"),
      .shortId = std::string("12345"),
      .displayName = std::nullopt,
      .retryAfterSeconds = std::nullopt,
  };
  auto decoded = dovahlink::protocol::DecodePairingOutcomePayload(
      dovahlink::protocol::EncodePairingOutcomePayload(original));
  REQUIRE(decoded.has_value());
  CHECK(decoded->outcome == original.outcome);
  CHECK(decoded->credential == original.credential);
  CHECK(decoded->shortId == original.shortId);
  CHECK(decoded->displayName == original.displayName);
  CHECK(decoded->retryAfterSeconds == original.retryAfterSeconds);
}

TEST_CASE("PairingOutcomePayload round-trips a present retryAfterSeconds "
          "through encode then decode",
          "[protocol][pairing_outcome_payload]") {
  dovahlink::protocol::PairingOutcomePayload original{
      .outcome = "pacing_limited",
      .credential = std::nullopt,
      .shortId = std::nullopt,
      .displayName = std::nullopt,
      .retryAfterSeconds = 1,
  };
  auto decoded = dovahlink::protocol::DecodePairingOutcomePayload(
      dovahlink::protocol::EncodePairingOutcomePayload(original));
  REQUIRE(decoded.has_value());
  REQUIRE(decoded->retryAfterSeconds.has_value());
  CHECK(*decoded->retryAfterSeconds == 1);
}

TEST_CASE("pairing_outcome is rejected when outcome is not a registered value",
          "[protocol][pairing_outcome_payload]") {
  boost::json::object payload =
      boost::json::parse(
          R"({"outcome": "vibes", "credential": null, "shortId": null, "displayName": null})")
          .get_object();
  auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(payload);
  REQUIRE_FALSE(outcome.has_value());
}

TEST_CASE(
    "pairing_outcome is rejected when credential is present but not a string",
    "[protocol][pairing_outcome_payload]") {
  boost::json::object payload =
      boost::json::parse(
          R"({"outcome": "trusted", "credential": 42, "shortId": null, "displayName": null})")
          .get_object();
  auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(payload);
  REQUIRE_FALSE(outcome.has_value());
}
