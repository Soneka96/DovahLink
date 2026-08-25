#include "protocol/error_payload.hpp"

#include "protocol/fixture_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/object.hpp>
#include <boost/json/parse.hpp>

#include <string>
#include <vector>

using dovahlink::protocol::test_support::DecodeFixtureEnvelope;

TEST_CASE("error-rate-limited fixture decodes as retryable",
          "[protocol][error_payload]") {
  auto envelope = DecodeFixtureEnvelope("errors/error-rate-limited.json");
  auto error = dovahlink::protocol::DecodeErrorPayload(envelope.payload);
  REQUIRE(error.has_value());
  CHECK(error->code == "rate_limited");
  CHECK(error->retryable);
}

TEST_CASE("every error fixture decodes to a valid ErrorPayload",
          "[protocol][error_payload]") {
  static const std::vector<std::string> kErrorFixtures = {
      "errors/error-unauthenticated-invalid-token.json",
      "errors/error-unauthenticated-expired-token.json",
      "errors/error-unauthenticated-reused-token.json",
      "errors/error-frame-too-large.json",
      "errors/error-stale-session.json",
      "errors/error-replayed-message.json",
      "errors/error-rate-limited.json",
      "errors/error-malformed-message.json",
  };
  for (const std::string &fixturePath : kErrorFixtures) {
    auto envelope = DecodeFixtureEnvelope(fixturePath);
    auto error = dovahlink::protocol::DecodeErrorPayload(envelope.payload);
    REQUIRE(error.has_value());
    CHECK_FALSE(error->code.empty());
    CHECK_FALSE(error->message.empty());
  }
}

TEST_CASE("error is rejected when retryable is missing",
          "[protocol][error_payload]") {
  boost::json::object payload =
      boost::json::parse(R"({"code": "internal_error", "message": "boom"})")
          .get_object();
  auto error = dovahlink::protocol::DecodeErrorPayload(payload);
  REQUIRE_FALSE(error.has_value());
}

TEST_CASE("error accepts non-object, non-null details",
          "[protocol][error_payload]") {
  boost::json::object payload =
      boost::json::parse(
          R"({"code": "internal_error", "message": "boom", "retryable": false, "details": "a string detail"})")
          .get_object();
  auto error = dovahlink::protocol::DecodeErrorPayload(payload);
  REQUIRE(error.has_value());
  REQUIRE(error->details.has_value());
  CHECK(error->details->is_string());
}

TEST_CASE("error decodes non-null details when present",
          "[protocol][error_payload]") {
  boost::json::object payload =
      boost::json::parse(
          R"({"code": "internal_error", "message": "boom", "retryable": true, "details": {"hint": "x"}})")
          .get_object();
  auto error = dovahlink::protocol::DecodeErrorPayload(payload);
  REQUIRE(error.has_value());
  REQUIRE(error->details.has_value());
  CHECK(error->details->is_object());
}

TEST_CASE("EncodeErrorPayload round-trips a fixture with details absent",
          "[protocol][error_payload]") {
  auto envelope = DecodeFixtureEnvelope("errors/error-malformed-message.json");
  auto original = dovahlink::protocol::DecodeErrorPayload(envelope.payload);
  REQUIRE(original.has_value());
  REQUIRE_FALSE(original->details.has_value());

  boost::json::object encoded =
      dovahlink::protocol::EncodeErrorPayload(*original);

  // details must still be present in the wire shape, as an explicit null,
  // not omitted -- see the EncodeErrorPayload declaration's comment.
  const boost::json::value *detailsValue = encoded.if_contains("details");
  REQUIRE(detailsValue != nullptr);
  CHECK(detailsValue->is_null());

  auto roundTripped = dovahlink::protocol::DecodeErrorPayload(encoded);
  REQUIRE(roundTripped.has_value());
  CHECK(roundTripped->code == original->code);
  CHECK(roundTripped->message == original->message);
  CHECK(roundTripped->retryable == original->retryable);
  CHECK_FALSE(roundTripped->details.has_value());
}

TEST_CASE("EncodeErrorPayload round-trips retryable and non-retryable values",
          "[protocol][error_payload]") {
  auto envelope = DecodeFixtureEnvelope("errors/error-rate-limited.json");
  auto original = dovahlink::protocol::DecodeErrorPayload(envelope.payload);
  REQUIRE(original.has_value());
  REQUIRE(original->retryable);

  boost::json::object encoded =
      dovahlink::protocol::EncodeErrorPayload(*original);
  auto roundTripped = dovahlink::protocol::DecodeErrorPayload(encoded);

  REQUIRE(roundTripped.has_value());
  CHECK(roundTripped->retryable);
}

TEST_CASE(
    "EncodeErrorPayload round-trips a payload with details actually present",
    "[protocol][error_payload]") {
  // No Phase 1 error fixture has non-null details
  // (protocol/fixtures/errors/*.json all use null), so this branch is exercised
  // with a hand-built payload rather than a fixture, matching
  // protocol/fixtures/README.md (fixtures model valid or documented wire
  // scenarios).
  dovahlink::protocol::ErrorPayload original{
      .code = "internal_error",
      .message = "worker exited unexpectedly",
      .retryable = false,
      .details =
          boost::json::value(boost::json::object{{"component", "worker"}}),
  };

  boost::json::object encoded =
      dovahlink::protocol::EncodeErrorPayload(original);

  const boost::json::value *detailsValue = encoded.if_contains("details");
  REQUIRE(detailsValue != nullptr);
  REQUIRE(detailsValue->is_object());
  CHECK(detailsValue->get_object().at("component").as_string() == "worker");

  auto roundTripped = dovahlink::protocol::DecodeErrorPayload(encoded);
  REQUIRE(roundTripped.has_value());
  REQUIRE(roundTripped->details.has_value());
  CHECK(*roundTripped->details == *original.details);
}

TEST_CASE("BuildErrorEnvelope answers the original message and encodes a "
          "decodable payload",
          "[protocol][error_payload]") {
  auto originalEnvelope = DecodeFixtureEnvelope("connection/hello.json");

  auto errorEnvelope = dovahlink::protocol::BuildErrorEnvelope(
      originalEnvelope.messageId,
      /*sessionId=*/std::nullopt, "unauthenticated",
      "Invalid or expired one-time token", /*retryable=*/false);

  CHECK(errorEnvelope.messageType == "error");
  CHECK_FALSE(errorEnvelope.messageId.empty());
  CHECK_FALSE(errorEnvelope.sessionId.has_value());
  REQUIRE(errorEnvelope.correlationId.has_value());
  CHECK(*errorEnvelope.correlationId == originalEnvelope.messageId);

  auto error = dovahlink::protocol::DecodeErrorPayload(errorEnvelope.payload);
  REQUIRE(error.has_value());
  CHECK(error->code == "unauthenticated");
  CHECK_FALSE(error->retryable);
}

TEST_CASE("BuildErrorEnvelope carries the caller's sessionId through unchanged",
          "[protocol][error_payload]") {
  auto originalEnvelope = DecodeFixtureEnvelope("subscriptions/subscribe.json");

  auto errorEnvelope = dovahlink::protocol::BuildErrorEnvelope(
      originalEnvelope.messageId,
      /*sessionId=*/std::string("session-1"), "rate_limited",
      "Inbound message rate exceeded 100 messages per second",
      /*retryable=*/true);

  REQUIRE(errorEnvelope.sessionId.has_value());
  CHECK(*errorEnvelope.sessionId == "session-1");
}

TEST_CASE("BuildErrorEnvelope accepts a nullopt correlationId for input that "
          "could not be decoded",
          "[protocol][error_payload]") {
  // Undecodable input (not even valid JSON, or missing/mistyped required
  // envelope fields) has no trustworthy messageId to correlate against --
  // this is why correlationId is a plain parameter rather than requiring
  // a whole original Envelope.
  auto errorEnvelope = dovahlink::protocol::BuildErrorEnvelope(
      /*correlationId=*/std::nullopt,
      /*sessionId=*/std::string("session-1"), "malformed_message",
      "Malformed message", false);

  CHECK_FALSE(errorEnvelope.correlationId.has_value());
  REQUIRE(errorEnvelope.sessionId.has_value());
  CHECK(*errorEnvelope.sessionId == "session-1");
}
