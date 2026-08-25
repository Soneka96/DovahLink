#include "protocol/rename_outcome_payload.hpp"

#include "protocol/fixture_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/object.hpp>
#include <boost/json/parse.hpp>

#include <optional>
#include <string>

using dovahlink::protocol::test_support::DecodeFixtureEnvelope;

TEST_CASE(
    "rename-outcome-renamed fixture decodes with the resulting displayName",
    "[protocol][rename_outcome_payload]") {
  auto envelope = DecodeFixtureEnvelope("rename/rename-outcome-renamed.json");
  auto outcome =
      dovahlink::protocol::DecodeRenameOutcomePayload(envelope.payload);
  REQUIRE(outcome.has_value());
  CHECK(outcome->outcome == "renamed");
  REQUIRE(outcome->displayName.has_value());
  CHECK(*outcome->displayName == "New Name");
}

TEST_CASE(
    "rename-outcome-invalid-display-name fixture decodes with no displayName",
    "[protocol][rename_outcome_payload]") {
  auto envelope =
      DecodeFixtureEnvelope("rename/rename-outcome-invalid-display-name.json");
  auto outcome =
      dovahlink::protocol::DecodeRenameOutcomePayload(envelope.payload);
  REQUIRE(outcome.has_value());
  CHECK(outcome->outcome == "invalid_display_name");
  CHECK_FALSE(outcome->displayName.has_value());
}

TEST_CASE("rename-outcome-not-trusted fixture decodes with no displayName",
          "[protocol][rename_outcome_payload]") {
  auto envelope =
      DecodeFixtureEnvelope("rename/rename-outcome-not-trusted.json");
  auto outcome =
      dovahlink::protocol::DecodeRenameOutcomePayload(envelope.payload);
  REQUIRE(outcome.has_value());
  CHECK(outcome->outcome == "not_trusted");
  CHECK_FALSE(outcome->displayName.has_value());
}

TEST_CASE("rename_outcome decodes a missing displayName as absent",
          "[protocol][rename_outcome_payload]") {
  boost::json::object payload =
      boost::json::parse(R"({"outcome": "invalid_display_name"})").get_object();
  auto outcome = dovahlink::protocol::DecodeRenameOutcomePayload(payload);
  REQUIRE(outcome.has_value());
  CHECK_FALSE(outcome->displayName.has_value());
}

TEST_CASE(
    "rename_outcome is rejected when displayName is a present empty string",
    "[protocol][rename_outcome_payload]") {
  boost::json::object payload =
      boost::json::parse(R"({"outcome": "renamed", "displayName": ""})")
          .get_object();
  auto outcome = dovahlink::protocol::DecodeRenameOutcomePayload(payload);
  REQUIRE_FALSE(outcome.has_value());
}

TEST_CASE("rename_outcome is rejected when outcome has the wrong type",
          "[protocol][rename_outcome_payload]") {
  boost::json::object payload =
      boost::json::parse(R"({"outcome": 42, "displayName": null})")
          .get_object();
  auto outcome = dovahlink::protocol::DecodeRenameOutcomePayload(payload);
  REQUIRE_FALSE(outcome.has_value());
}

TEST_CASE("rename_outcome is rejected when outcome is an empty string",
          "[protocol][rename_outcome_payload]") {
  boost::json::object payload =
      boost::json::parse(R"({"outcome": "", "displayName": null})")
          .get_object();
  auto outcome = dovahlink::protocol::DecodeRenameOutcomePayload(payload);
  REQUIRE_FALSE(outcome.has_value());
}

TEST_CASE("RenameOutcomePayload round-trips a non-renamed outcome through "
          "encode then decode",
          "[protocol][rename_outcome_payload]") {
  dovahlink::protocol::RenameOutcomePayload original{
      .outcome = "not_trusted",
      .displayName = std::nullopt,
  };
  auto decoded = dovahlink::protocol::DecodeRenameOutcomePayload(
      dovahlink::protocol::EncodeRenameOutcomePayload(original));
  REQUIRE(decoded.has_value());
  CHECK(decoded->outcome == original.outcome);
  CHECK_FALSE(decoded->displayName.has_value());
}

TEST_CASE("RenameOutcomePayload round-trips through encode then decode",
          "[protocol][rename_outcome_payload]") {
  dovahlink::protocol::RenameOutcomePayload original{
      .outcome = "renamed",
      .displayName = std::string("New Name"),
  };
  auto decoded = dovahlink::protocol::DecodeRenameOutcomePayload(
      dovahlink::protocol::EncodeRenameOutcomePayload(original));
  REQUIRE(decoded.has_value());
  CHECK(decoded->outcome == original.outcome);
  CHECK(decoded->displayName == original.displayName);
}

TEST_CASE("RenameOutcomePayload round-trips a cleared displayName through "
          "encode then decode",
          "[protocol][rename_outcome_payload]") {
  dovahlink::protocol::RenameOutcomePayload original{
      .outcome = "renamed",
      .displayName = std::nullopt,
  };
  auto decoded = dovahlink::protocol::DecodeRenameOutcomePayload(
      dovahlink::protocol::EncodeRenameOutcomePayload(original));
  REQUIRE(decoded.has_value());
  CHECK_FALSE(decoded->displayName.has_value());
}

TEST_CASE("rename_outcome is rejected when outcome is not a registered value",
          "[protocol][rename_outcome_payload]") {
  boost::json::object payload =
      boost::json::parse(R"({"outcome": "vibes", "displayName": null})")
          .get_object();
  auto outcome = dovahlink::protocol::DecodeRenameOutcomePayload(payload);
  REQUIRE_FALSE(outcome.has_value());
}

TEST_CASE("rename_outcome is rejected when outcome is missing",
          "[protocol][rename_outcome_payload]") {
  boost::json::object payload =
      boost::json::parse(R"({"displayName": null})").get_object();
  auto outcome = dovahlink::protocol::DecodeRenameOutcomePayload(payload);
  REQUIRE_FALSE(outcome.has_value());
}

TEST_CASE(
    "rename_outcome is rejected when displayName is present but not a string",
    "[protocol][rename_outcome_payload]") {
  boost::json::object payload =
      boost::json::parse(R"({"outcome": "renamed", "displayName": 42})")
          .get_object();
  auto outcome = dovahlink::protocol::DecodeRenameOutcomePayload(payload);
  REQUIRE_FALSE(outcome.has_value());
}
