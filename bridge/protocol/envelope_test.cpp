#include "protocol/envelope.hpp"

#include "protocol/bounded_json.hpp"
#include "protocol/fixture_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/value.hpp>

#include <string>
#include <vector>

namespace {

///  Loads and decodes one protocol envelope fixture for envelope assertions.
dovahlink::protocol::Envelope DecodeFixture(const std::string& relativePath) {
    return dovahlink::protocol::test_support::DecodeFixtureEnvelope(relativePath);
}

///  Builds an envelope for EncodeEnvelope's identity-field assertions.
dovahlink::protocol::Envelope
MakeEnvelope(std::optional<std::string> bridgeInstanceId,
             std::optional<std::string> playContextId,
             std::optional<std::string> clientId) {
    return dovahlink::protocol::Envelope{
        .messageType = "ping",
        .messageId = "m-1",
        .sessionId = std::string("s-1"),
        .correlationId = std::nullopt,
        .payload = {},
        .bridgeInstanceId = std::move(bridgeInstanceId),
        .playContextId = std::move(playContextId),
        .clientId = std::move(clientId),
    };
}

} //  namespace

TEST_CASE("hello fixture decodes with a null sessionId, correlationId, and "
          "identity fields, "
          "and its clientId in the payload",
          "[protocol][envelope]") {
    auto envelope = DecodeFixture("connection/hello.json");
    CHECK(envelope.messageType == "hello");
    CHECK(envelope.messageId == "message-hello-1");
    CHECK_FALSE(envelope.sessionId.has_value());
    CHECK_FALSE(envelope.correlationId.has_value());
    CHECK_FALSE(envelope.bridgeInstanceId.has_value());
    CHECK_FALSE(envelope.playContextId.has_value());
    CHECK_FALSE(envelope.clientId.has_value());
    REQUIRE(envelope.payload.if_contains("clientId"));
    CHECK(envelope.payload.at("clientId").as_string() == "client-1");
}

TEST_CASE("hello-ack fixture decodes with the issued sessionId, correlationId, "
          "and bridge identity fields",
          "[protocol][envelope]") {
    auto envelope = DecodeFixture("connection/hello-ack.json");
    CHECK(envelope.messageType == "hello_ack");
    REQUIRE(envelope.sessionId.has_value());
    CHECK(*envelope.sessionId == "session-1");
    REQUIRE(envelope.correlationId.has_value());
    CHECK(*envelope.correlationId == "message-hello-1");
    REQUIRE(envelope.bridgeInstanceId.has_value());
    CHECK(*envelope.bridgeInstanceId == "bridge-1");
    CHECK_FALSE(envelope.playContextId.has_value());
    REQUIRE(envelope.clientId.has_value());
    CHECK(*envelope.clientId == "client-1");
}

TEST_CASE(
    "hello-ack-active-context fixture decodes with a non-null playContextId",
    "[protocol][envelope]") {
    //  Proves the identity fields also carry a real value, not only null --
    //  the shape a reconnect into an already-loaded play context produces.
    auto envelope = DecodeFixture("connection/hello-ack-active-context.json");
    REQUIRE(envelope.playContextId.has_value());
    CHECK(*envelope.playContextId == "context-1");
}

TEST_CASE("EncodeEnvelope round-trips the hello-ack fixture, including its "
          "identity fields",
          "[protocol][envelope]") {
    auto original = DecodeFixture("connection/hello-ack.json");

    std::string encoded = dovahlink::protocol::EncodeEnvelope(original);

    auto reparsed = dovahlink::protocol::ParseBoundedJson(encoded);
    REQUIRE(reparsed.has_value());
    auto roundTripped = dovahlink::protocol::DecodeEnvelope(*reparsed);
    REQUIRE(roundTripped.has_value());

    CHECK(roundTripped->messageType == original.messageType);
    CHECK(roundTripped->messageId == original.messageId);
    CHECK(roundTripped->sessionId == original.sessionId);
    CHECK(roundTripped->correlationId == original.correlationId);
    CHECK(roundTripped->payload == original.payload);
    CHECK(roundTripped->bridgeInstanceId == original.bridgeInstanceId);
    CHECK(roundTripped->playContextId == original.playContextId);
    CHECK(roundTripped->clientId == original.clientId);
}

TEST_CASE("state-snapshot fixture decodes with its payload intact",
          "[protocol][envelope]") {
    auto envelope = DecodeFixture("state/state-snapshot.json");
    CHECK(envelope.messageType == "state_snapshot");
    REQUIRE(envelope.payload.if_contains("stateArea"));
    CHECK(envelope.payload.at("stateArea").as_string() == "example_area");
}

TEST_CASE(
    "error-unauthenticated-invalid-token fixture decodes with a null sessionId",
    "[protocol][envelope]") {
    auto envelope =
        DecodeFixture("errors/error-unauthenticated-invalid-token.json");
    CHECK(envelope.messageType == "error");
    CHECK_FALSE(envelope.sessionId.has_value());
}

TEST_CASE("error-stale-session fixture decodes with a non-null sessionId",
          "[protocol][envelope]") {
    auto envelope = DecodeFixture("errors/error-stale-session.json");
    CHECK(envelope.messageType == "error");
    REQUIRE(envelope.sessionId.has_value());
    CHECK(*envelope.sessionId == "session-2");
}

TEST_CASE("state-snapshot-unknown-field fixture decodes, ignoring the "
          "unrecognized top-level field",
          "[protocol][envelope]") {
    auto envelope = DecodeFixture("state/state-snapshot-unknown-field.json");
    CHECK(envelope.messageType == "state_snapshot");
    REQUIRE(envelope.payload.if_contains("stateArea"));
}

TEST_CASE("every canonical JSON fixture is recursively discovered and decodes "
          "as an envelope",
          "[protocol][envelope][fixtures]") {
    const std::vector<std::string> fixturePaths =
        dovahlink::protocol::test_support::DiscoverFixturePaths();
    REQUIRE_FALSE(fixturePaths.empty());

    for (const std::string& fixturePath : fixturePaths) {
        CAPTURE(fixturePath);
        auto envelope = DecodeFixture(fixturePath);
        CHECK_FALSE(envelope.messageType.empty());
        CHECK_FALSE(envelope.messageId.empty());
    }
}

//  Hand-built malformed cases: these test the codec's rejection behavior rather
//  than model a valid or documented wire scenario, so they are not stored as
//  fixtures. Each literal carries null bridgeInstanceId/playContextId/clientId
//  keys so the case under test is the only isolated violation.

TEST_CASE("a non-object top-level value is rejected", "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson("[1, 2, 3]");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("a message missing a required field is rejected",
          "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": null})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("a message missing bridgeInstanceId is rejected",
          "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": null,
            "payload": {}, "playContextId": null, "clientId": null})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("a message missing playContextId is rejected",
          "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": null,
            "payload": {}, "bridgeInstanceId": null, "clientId": null})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("a message missing clientId is rejected", "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": null,
            "payload": {}, "bridgeInstanceId": null, "playContextId": null})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("a non-string messageType is rejected", "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"messageType": 7, "messageId": "m-1", "sessionId": "s-1", "correlationId": null,
            "payload": {}, "bridgeInstanceId": null, "playContextId": null, "clientId": null})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("an empty messageId is rejected", "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"messageType": "ping", "messageId": "", "sessionId": "s-1", "correlationId": null,
            "payload": {}, "bridgeInstanceId": null, "playContextId": null, "clientId": null})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("hello with a non-null sessionId is rejected",
          "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"messageType": "hello", "messageId": "m-1", "sessionId": "s-1", "correlationId": null,
            "payload": {}, "bridgeInstanceId": null, "playContextId": null, "clientId": null})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("a non-hello, non-error message with a null sessionId is rejected",
          "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"messageType": "ping", "messageId": "m-1", "sessionId": null, "correlationId": null,
            "payload": {}, "bridgeInstanceId": null, "playContextId": null, "clientId": null})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("a non-string, non-null correlationId is rejected",
          "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": 5,
            "payload": {}, "bridgeInstanceId": null, "playContextId": null, "clientId": null})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("an empty correlationId is rejected", "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": "",
            "payload": {}, "bridgeInstanceId": null, "playContextId": null, "clientId": null})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("an empty sessionId for an error message is rejected",
          "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"messageType": "error", "messageId": "m-1", "sessionId": "", "correlationId": null,
            "payload": {}, "bridgeInstanceId": null, "playContextId": null, "clientId": null})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("a non-string messageId is rejected", "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"messageType": "ping", "messageId": 123, "sessionId": "s-1", "correlationId": null,
            "payload": {}, "bridgeInstanceId": null, "playContextId": null, "clientId": null})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE(
    "a non-string, non-null sessionId for an ordinary message is rejected",
    "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"messageType": "ping", "messageId": "m-1", "sessionId": 5, "correlationId": null,
            "payload": {}, "bridgeInstanceId": null, "playContextId": null, "clientId": null})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("a null payload is rejected", "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": null,
            "payload": null, "bridgeInstanceId": null, "playContextId": null, "clientId": null})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("a non-object payload is rejected", "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": null,
            "payload": "nope", "bridgeInstanceId": null, "playContextId": null, "clientId": null})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("an error with a null sessionId is accepted",
          "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"messageType": "error", "messageId": "m-1", "sessionId": null, "correlationId": null,
            "payload": {}, "bridgeInstanceId": null, "playContextId": null, "clientId": null})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE(envelope.has_value());
    CHECK_FALSE(envelope->sessionId.has_value());
}

TEST_CASE("a message with unknown top-level fields still decodes",
          "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": null,
            "payload": {}, "bridgeInstanceId": null, "playContextId": null, "clientId": null, "futureField": true})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE(envelope.has_value());
    CHECK(envelope->messageType == "ping");
}

//  EncodeEnvelope round-trips: encoding a decoded fixture and decoding the
//  result again must reproduce every field DecodeEnvelope itself validates.
//  A bug that omitted or mistyped a field would fail these at the decode
//  step, not just at a hand-rolled field comparison.

TEST_CASE(
    "EncodeEnvelope round-trips a null sessionId as JSON null, not a string",
    "[protocol][envelope]") {
    auto original =
        DecodeFixture("errors/error-unauthenticated-invalid-token.json");
    REQUIRE_FALSE(original.sessionId.has_value());

    std::string encoded = dovahlink::protocol::EncodeEnvelope(original);

    auto reparsed = dovahlink::protocol::ParseBoundedJson(encoded);
    REQUIRE(reparsed.has_value());
    REQUIRE(reparsed->is_object());
    const boost::json::value* sessionIdValue =
        reparsed->get_object().if_contains("sessionId");
    REQUIRE(sessionIdValue != nullptr);
    CHECK(sessionIdValue->is_null());

    auto roundTripped = dovahlink::protocol::DecodeEnvelope(*reparsed);
    REQUIRE(roundTripped.has_value());
    CHECK_FALSE(roundTripped->sessionId.has_value());
}

TEST_CASE(
    "EncodeEnvelope round-trips a non-null sessionId from an error fixture",
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

//  Identity fields: always present as keys (checked above), nullable in value.
//  There is no version gate left to condition their presence on.

TEST_CASE("a message with null identity fields decodes with all three absent",
          "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": null,
            "payload": {}, "bridgeInstanceId": null, "playContextId": null, "clientId": null})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE(envelope.has_value());
    CHECK_FALSE(envelope->bridgeInstanceId.has_value());
    CHECK_FALSE(envelope->playContextId.has_value());
    CHECK_FALSE(envelope->clientId.has_value());
}

TEST_CASE("a message with string identity fields decodes with their values",
          "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": null,
            "payload": {}, "bridgeInstanceId": "bridge-1", "playContextId": "context-1", "clientId": "client-1"})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE(envelope.has_value());
    REQUIRE(envelope->bridgeInstanceId.has_value());
    CHECK(*envelope->bridgeInstanceId == "bridge-1");
    REQUIRE(envelope->playContextId.has_value());
    CHECK(*envelope->playContextId == "context-1");
    REQUIRE(envelope->clientId.has_value());
    CHECK(*envelope->clientId == "client-1");
}

TEST_CASE("a non-string, non-null bridgeInstanceId is rejected",
          "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": null,
            "payload": {}, "bridgeInstanceId": 5, "playContextId": null, "clientId": null})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("an empty bridgeInstanceId is rejected", "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": null,
            "payload": {}, "bridgeInstanceId": "", "playContextId": null, "clientId": null})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("a non-string, non-null playContextId is rejected",
          "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": null,
            "payload": {}, "bridgeInstanceId": null, "playContextId": 5, "clientId": null})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("an empty playContextId is rejected", "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": null,
            "payload": {}, "bridgeInstanceId": null, "playContextId": "", "clientId": null})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("an empty clientId is rejected", "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": null,
            "payload": {}, "bridgeInstanceId": null, "playContextId": null, "clientId": ""})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("a non-string, non-null clientId is rejected",
          "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": null,
            "payload": {}, "bridgeInstanceId": null, "playContextId": null, "clientId": true})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("EncodeEnvelope emits JSON null for empty identity fields",
          "[protocol][envelope]") {
    auto envelope = MakeEnvelope(std::nullopt, std::nullopt, std::nullopt);

    std::string encoded = dovahlink::protocol::EncodeEnvelope(envelope);
    auto reparsed = dovahlink::protocol::ParseBoundedJson(encoded);
    REQUIRE(reparsed.has_value());
    REQUIRE(reparsed->is_object());
    const boost::json::object& obj = reparsed->get_object();
    REQUIRE(obj.if_contains("bridgeInstanceId") != nullptr);
    CHECK(obj.at("bridgeInstanceId").is_null());
    REQUIRE(obj.if_contains("playContextId") != nullptr);
    CHECK(obj.at("playContextId").is_null());
    REQUIRE(obj.if_contains("clientId") != nullptr);
    CHECK(obj.at("clientId").is_null());
}

TEST_CASE("EncodeEnvelope emits values for populated identity fields",
          "[protocol][envelope]") {
    auto envelope = MakeEnvelope("bridge-1", "context-1", "client-1");

    std::string encoded = dovahlink::protocol::EncodeEnvelope(envelope);
    auto reparsed = dovahlink::protocol::ParseBoundedJson(encoded);
    REQUIRE(reparsed.has_value());
    REQUIRE(reparsed->is_object());
    const boost::json::object& obj = reparsed->get_object();
    REQUIRE(obj.if_contains("bridgeInstanceId") != nullptr);
    CHECK(obj.at("bridgeInstanceId").as_string() == "bridge-1");
    REQUIRE(obj.if_contains("playContextId") != nullptr);
    CHECK(obj.at("playContextId").as_string() == "context-1");
    REQUIRE(obj.if_contains("clientId") != nullptr);
    CHECK(obj.at("clientId").as_string() == "client-1");
}

TEST_CASE("EncodeEnvelope round-trips an envelope with mixed set and absent "
          "identity fields",
          "[protocol][envelope]") {
    auto original = MakeEnvelope("bridge-1", std::nullopt, "client-1");

    std::string encoded = dovahlink::protocol::EncodeEnvelope(original);
    auto reparsed = dovahlink::protocol::ParseBoundedJson(encoded);
    REQUIRE(reparsed.has_value());
    auto roundTripped = dovahlink::protocol::DecodeEnvelope(*reparsed);
    REQUIRE(roundTripped.has_value());

    CHECK(roundTripped->bridgeInstanceId == original.bridgeInstanceId);
    CHECK(roundTripped->playContextId == original.playContextId);
    CHECK(roundTripped->clientId == original.clientId);
}
