#include "protocol/envelope.hpp"

#include "protocol/bounded_json.hpp"
#include "protocol/fixture_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/value.hpp>

#include <string>

namespace {

dovahlink::protocol::Envelope DecodeFixture(const std::string& relativePath) {
    return dovahlink::protocol::test_support::DecodeFixtureEnvelope(relativePath);
}

}  // namespace

TEST_CASE("hello fixture decodes with a null sessionId and correlationId", "[protocol][envelope]") {
    auto envelope = DecodeFixture("connection/hello.json");
    CHECK(envelope.protocolVersion == 0);
    CHECK(envelope.messageType == "hello");
    CHECK(envelope.messageId == "message-hello-1");
    CHECK_FALSE(envelope.sessionId.has_value());
    CHECK_FALSE(envelope.correlationId.has_value());
}

TEST_CASE("hello-ack fixture decodes with the issued sessionId and correlationId", "[protocol][envelope]") {
    auto envelope = DecodeFixture("connection/hello-ack.json");
    CHECK(envelope.messageType == "hello_ack");
    REQUIRE(envelope.sessionId.has_value());
    CHECK(*envelope.sessionId == "session-1");
    REQUIRE(envelope.correlationId.has_value());
    CHECK(*envelope.correlationId == "message-hello-1");
}

TEST_CASE("character-state-snapshot fixture decodes with its payload intact", "[protocol][envelope]") {
    auto envelope = DecodeFixture("state/character/character-state-snapshot.json");
    CHECK(envelope.protocolVersion == 1);
    CHECK(envelope.messageType == "state_snapshot");
    REQUIRE(envelope.payload.if_contains("stateArea"));
    CHECK(envelope.payload.at("stateArea").as_string() == "character");
}

TEST_CASE("error-unauthenticated-invalid-token fixture decodes with a null sessionId", "[protocol][envelope]") {
    auto envelope = DecodeFixture("errors/error-unauthenticated-invalid-token.json");
    CHECK(envelope.messageType == "error");
    CHECK_FALSE(envelope.sessionId.has_value());
}

TEST_CASE("error-stale-session fixture decodes with a non-null sessionId", "[protocol][envelope]") {
    auto envelope = DecodeFixture("errors/error-stale-session.json");
    CHECK(envelope.messageType == "error");
    REQUIRE(envelope.sessionId.has_value());
    CHECK(*envelope.sessionId == "session-2");
}

TEST_CASE("state-snapshot-unknown-field fixture decodes, ignoring the unrecognized top-level field",
          "[protocol][envelope]") {
    auto envelope = DecodeFixture("state/character/state-snapshot-unknown-field.json");
    CHECK(envelope.messageType == "state_snapshot");
    REQUIRE(envelope.payload.if_contains("stateArea"));
}

// Hand-built malformed cases: these test the codec's rejection behavior rather than
// model a valid or documented wire scenario, so they are not stored as fixtures.

TEST_CASE("a non-object top-level value is rejected", "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson("[1, 2, 3]");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("a message missing a required field is rejected", "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"protocolVersion": 1, "messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": null})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("a negative protocolVersion is rejected", "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"protocolVersion": -1, "messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": null, "payload": {}})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("a non-integer protocolVersion is rejected", "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"protocolVersion": "1", "messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": null, "payload": {}})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("a non-string messageType is rejected", "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"protocolVersion": 1, "messageType": 7, "messageId": "m-1", "sessionId": "s-1", "correlationId": null, "payload": {}})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("an empty messageId is rejected", "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"protocolVersion": 1, "messageType": "ping", "messageId": "", "sessionId": "s-1", "correlationId": null, "payload": {}})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("hello with a non-null sessionId is rejected", "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"protocolVersion": 0, "messageType": "hello", "messageId": "m-1", "sessionId": "s-1", "correlationId": null, "payload": {}})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("a non-hello, non-error message with a null sessionId is rejected", "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"protocolVersion": 1, "messageType": "ping", "messageId": "m-1", "sessionId": null, "correlationId": null, "payload": {}})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("a non-string, non-null correlationId is rejected", "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"protocolVersion": 1, "messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": 5, "payload": {}})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("an empty correlationId is rejected", "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"protocolVersion": 1, "messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": "", "payload": {}})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("an empty sessionId for an error message is rejected", "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"protocolVersion": 0, "messageType": "error", "messageId": "m-1", "sessionId": "", "correlationId": null, "payload": {}})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("a protocolVersion too large to fit in int64 is rejected", "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"protocolVersion": 18446744073709551615, "messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": null, "payload": {}})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("a non-string messageId is rejected", "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"protocolVersion": 1, "messageType": "ping", "messageId": 123, "sessionId": "s-1", "correlationId": null, "payload": {}})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("a non-string, non-null sessionId for an ordinary message is rejected", "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"protocolVersion": 1, "messageType": "ping", "messageId": "m-1", "sessionId": 5, "correlationId": null, "payload": {}})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("a null payload is rejected", "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"protocolVersion": 1, "messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": null, "payload": null})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("a non-object payload is rejected", "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"protocolVersion": 1, "messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": null, "payload": "nope"})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("an error with a null sessionId is accepted", "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"protocolVersion": 0, "messageType": "error", "messageId": "m-1", "sessionId": null, "correlationId": null, "payload": {}})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE(envelope.has_value());
    CHECK_FALSE(envelope->sessionId.has_value());
}

TEST_CASE("a message with unknown top-level fields still decodes", "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"protocolVersion": 1, "messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": null, "payload": {}, "futureField": true})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE(envelope.has_value());
    CHECK(envelope->messageType == "ping");
}

// EncodeEnvelope round-trips: encoding a decoded fixture and decoding the
// result again must reproduce every field DecodeEnvelope itself validates.
// A bug that omitted or mistyped a field would fail these at the decode
// step, not just at a hand-rolled field comparison.

TEST_CASE("EncodeEnvelope round-trips the hello-ack fixture (non-null sessionId and correlationId)",
          "[protocol][envelope]") {
    auto original = DecodeFixture("connection/hello-ack.json");

    std::string encoded = dovahlink::protocol::EncodeEnvelope(original);

    auto reparsed = dovahlink::protocol::ParseBoundedJson(encoded);
    REQUIRE(reparsed.has_value());
    auto roundTripped = dovahlink::protocol::DecodeEnvelope(*reparsed);
    REQUIRE(roundTripped.has_value());

    CHECK(roundTripped->protocolVersion == original.protocolVersion);
    CHECK(roundTripped->messageType == original.messageType);
    CHECK(roundTripped->messageId == original.messageId);
    CHECK(roundTripped->sessionId == original.sessionId);
    CHECK(roundTripped->correlationId == original.correlationId);
    CHECK(roundTripped->payload == original.payload);
}

TEST_CASE("EncodeEnvelope round-trips a null sessionId as JSON null, not a string",
          "[protocol][envelope]") {
    auto original = DecodeFixture("errors/error-unauthenticated-invalid-token.json");
    REQUIRE_FALSE(original.sessionId.has_value());

    std::string encoded = dovahlink::protocol::EncodeEnvelope(original);

    auto reparsed = dovahlink::protocol::ParseBoundedJson(encoded);
    REQUIRE(reparsed.has_value());
    REQUIRE(reparsed->is_object());
    const boost::json::value* sessionIdValue = reparsed->get_object().if_contains("sessionId");
    REQUIRE(sessionIdValue != nullptr);
    CHECK(sessionIdValue->is_null());

    auto roundTripped = dovahlink::protocol::DecodeEnvelope(*reparsed);
    REQUIRE(roundTripped.has_value());
    CHECK_FALSE(roundTripped->sessionId.has_value());
}

TEST_CASE("EncodeEnvelope round-trips a non-null sessionId from an error fixture",
          "[protocol][envelope]") {
    auto original = DecodeFixture("errors/error-stale-session.json");
    REQUIRE(original.sessionId.has_value());

    std::string encoded = dovahlink::protocol::EncodeEnvelope(original);
    auto reparsed = dovahlink::protocol::ParseBoundedJson(encoded);
    REQUIRE(reparsed.has_value());
    auto roundTripped = dovahlink::protocol::DecodeEnvelope(*reparsed);
    REQUIRE(roundTripped.has_value());
    CHECK(roundTripped->sessionId == original.sessionId);
}
