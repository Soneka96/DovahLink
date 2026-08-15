#include "protocol/envelope.hpp"

#include "protocol/bounded_json.hpp"
#include "protocol/fixture_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/value.hpp>

#include <string>
#include <vector>

namespace {

/// Loads and decodes one protocol envelope fixture for envelope assertions.
dovahlink::protocol::Envelope DecodeFixture(const std::string& relativePath) {
    return dovahlink::protocol::test_support::DecodeFixtureEnvelope(relativePath);
}

/// Builds a v1 envelope for EncodeEnvelope's version-gating assertions.
dovahlink::protocol::Envelope MakeV1Envelope(std::optional<std::string> bridgeInstanceId,
                                              std::optional<std::string> playContextId,
                                              std::optional<std::string> clientId) {
    return dovahlink::protocol::Envelope{
        .protocolVersion = 1,
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

/// Builds a v2 envelope for EncodeEnvelope's version-gating assertions.
dovahlink::protocol::Envelope MakeV2Envelope(std::optional<std::string> bridgeInstanceId,
                                              std::optional<std::string> playContextId,
                                              std::optional<std::string> clientId) {
    dovahlink::protocol::Envelope envelope = MakeV1Envelope(std::move(bridgeInstanceId), std::move(playContextId),
                                                             std::move(clientId));
    envelope.protocolVersion = 2;
    return envelope;
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

TEST_CASE("v2 hello fixture decodes with a null sessionId and a clientId in its payload",
          "[protocol][envelope]") {
    auto envelope = DecodeFixture("v2/connection/hello.json");
    CHECK(envelope.protocolVersion == 0);
    CHECK(envelope.messageType == "hello");
    CHECK_FALSE(envelope.sessionId.has_value());
    REQUIRE(envelope.payload.if_contains("clientId"));
    CHECK(envelope.payload.at("clientId").as_string() == "client-1");
}

TEST_CASE("v2 hello-ack fixture decodes at protocolVersion 2 with all three envelope identity fields",
          "[protocol][envelope]") {
    auto envelope = DecodeFixture("v2/connection/hello-ack.json");
    CHECK(envelope.protocolVersion == 2);
    CHECK(envelope.messageType == "hello_ack");
    REQUIRE(envelope.bridgeInstanceId.has_value());
    CHECK(*envelope.bridgeInstanceId == "bridge-1");
    CHECK_FALSE(envelope.playContextId.has_value());
    REQUIRE(envelope.clientId.has_value());
    CHECK(*envelope.clientId == "client-1");
}

TEST_CASE("v2 hello-ack-active-context fixture decodes with a non-null playContextId",
          "[protocol][envelope]") {
    // Proves the identity fields also carry a real value, not only null --
    // the shape a reconnect into an already-loaded play context produces.
    auto envelope = DecodeFixture("v2/connection/hello-ack-active-context.json");
    CHECK(envelope.protocolVersion == 2);
    REQUIRE(envelope.playContextId.has_value());
    CHECK(*envelope.playContextId == "context-1");
}

TEST_CASE("EncodeEnvelope round-trips the v2 hello-ack fixture's identity fields", "[protocol][envelope]") {
    auto original = DecodeFixture("v2/connection/hello-ack.json");

    std::string encoded = dovahlink::protocol::EncodeEnvelope(original);

    auto reparsed = dovahlink::protocol::ParseBoundedJson(encoded);
    REQUIRE(reparsed.has_value());
    auto roundTripped = dovahlink::protocol::DecodeEnvelope(*reparsed);
    REQUIRE(roundTripped.has_value());

    CHECK(roundTripped->protocolVersion == original.protocolVersion);
    CHECK(roundTripped->bridgeInstanceId == original.bridgeInstanceId);
    CHECK(roundTripped->playContextId == original.playContextId);
    CHECK(roundTripped->clientId == original.clientId);
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

TEST_CASE("state-snapshot-unknown-field fixture decodes, ignoring the "
          "unrecognized top-level field",
          "[protocol][envelope]") {
    auto envelope = DecodeFixture("state/character/state-snapshot-unknown-field.json");
    CHECK(envelope.messageType == "state_snapshot");
    REQUIRE(envelope.payload.if_contains("stateArea"));
}

TEST_CASE("every canonical JSON fixture is recursively discovered and decodes "
          "as an envelope",
          "[protocol][envelope][fixtures]") {
    const std::vector<std::string> fixturePaths = dovahlink::protocol::test_support::DiscoverFixturePaths();
    REQUIRE_FALSE(fixturePaths.empty());

    for (const std::string& fixturePath : fixturePaths) {
        CAPTURE(fixturePath);
        auto envelope = DecodeFixture(fixturePath);
        CHECK_FALSE(envelope.messageType.empty());
        CHECK_FALSE(envelope.messageId.empty());
    }
}

// Hand-built malformed cases: these test the codec's rejection behavior rather
// than model a valid or documented wire scenario, so they are not stored as
// fixtures.

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

TEST_CASE("EncodeEnvelope round-trips the hello-ack fixture (non-null "
          "sessionId and correlationId)",
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

TEST_CASE("EncodeEnvelope round-trips a null sessionId as JSON null, not a string", "[protocol][envelope]") {
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

TEST_CASE("EncodeEnvelope round-trips a non-null sessionId from an error fixture", "[protocol][envelope]") {
    auto original = DecodeFixture("errors/error-stale-session.json");
    REQUIRE(original.sessionId.has_value());

    std::string encoded = dovahlink::protocol::EncodeEnvelope(original);
    auto reparsed = dovahlink::protocol::ParseBoundedJson(encoded);
    REQUIRE(reparsed.has_value());
    auto roundTripped = dovahlink::protocol::DecodeEnvelope(*reparsed);
    REQUIRE(roundTripped.has_value());
    CHECK(roundTripped->sessionId == original.sessionId);
}

// Protocol v2 identity fields: absent in v1, present (as null or a value)
// once protocolVersion >= 2. Decoding is tolerant of the fields regardless of
// version (protocol/compatibility.md); only EncodeEnvelope enforces the
// version gate on the write side.

TEST_CASE("a v1 message omitting the v2 identity fields decodes with all three absent",
          "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"protocolVersion": 1, "messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": null, "payload": {}})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE(envelope.has_value());
    CHECK_FALSE(envelope->bridgeInstanceId.has_value());
    CHECK_FALSE(envelope->playContextId.has_value());
    CHECK_FALSE(envelope->clientId.has_value());
}

TEST_CASE("a v2 message with null identity fields decodes with all three absent",
          "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"protocolVersion": 2, "messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": null,
            "payload": {}, "bridgeInstanceId": null, "playContextId": null, "clientId": null})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE(envelope.has_value());
    CHECK_FALSE(envelope->bridgeInstanceId.has_value());
    CHECK_FALSE(envelope->playContextId.has_value());
    CHECK_FALSE(envelope->clientId.has_value());
}

TEST_CASE("a v2 message with string identity fields decodes with their values", "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"protocolVersion": 2, "messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": null,
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

TEST_CASE("a non-string, non-null bridgeInstanceId is rejected", "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"protocolVersion": 2, "messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": null,
            "payload": {}, "bridgeInstanceId": 5, "playContextId": null, "clientId": null})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("an empty playContextId is rejected", "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"protocolVersion": 2, "messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": null,
            "payload": {}, "bridgeInstanceId": null, "playContextId": "", "clientId": null})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("a non-string, non-null clientId is rejected", "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"protocolVersion": 2, "messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": null,
            "payload": {}, "bridgeInstanceId": null, "playContextId": null, "clientId": true})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE_FALSE(envelope.has_value());
}

TEST_CASE("a v1 message that includes a v2 identity field anyway still decodes it, tolerantly",
          "[protocol][envelope]") {
    // Decode never rejects on protocolVersion/identity-field mismatch: only
    // EncodeEnvelope enforces which version may write these fields.
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"protocolVersion": 1, "messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": null,
            "payload": {}, "bridgeInstanceId": "bridge-1"})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE(envelope.has_value());
    REQUIRE(envelope->bridgeInstanceId.has_value());
    CHECK(*envelope->bridgeInstanceId == "bridge-1");
}

TEST_CASE("a v2 message that omits the identity keys entirely decodes the same as explicit null",
          "[protocol][envelope]") {
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        R"({"protocolVersion": 2, "messageType": "ping", "messageId": "m-1", "sessionId": "s-1", "correlationId": null, "payload": {}})");
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE(envelope.has_value());
    CHECK_FALSE(envelope->bridgeInstanceId.has_value());
    CHECK_FALSE(envelope->playContextId.has_value());
    CHECK_FALSE(envelope->clientId.has_value());
}

TEST_CASE("EncodeEnvelope also emits the v2 identity fields at protocolVersion 3", "[protocol][envelope]") {
    // Guards against a future edit narrowing the gate from `>= 2` to `== 2`.
    auto envelope = MakeV2Envelope("bridge-1", "context-1", "client-1");
    envelope.protocolVersion = 3;

    std::string encoded = dovahlink::protocol::EncodeEnvelope(envelope);
    auto reparsed = dovahlink::protocol::ParseBoundedJson(encoded);
    REQUIRE(reparsed.has_value());
    REQUIRE(reparsed->is_object());
    const boost::json::object& obj = reparsed->get_object();
    REQUIRE(obj.if_contains("bridgeInstanceId") != nullptr);
    CHECK(obj.at("bridgeInstanceId").as_string() == "bridge-1");
}

TEST_CASE("EncodeEnvelope omits the v2 identity fields for protocolVersion 1, even when set",
          "[protocol][envelope]") {
    // Proves the gate is the version, not whether the optionals happen to be
    // empty (protocol/schema/README.md's v2 section: "explicitly version-
    // gated, not inferred from whether the optional is empty").
    auto envelope = MakeV1Envelope("bridge-1", "context-1", "client-1");

    std::string encoded = dovahlink::protocol::EncodeEnvelope(envelope);
    auto reparsed = dovahlink::protocol::ParseBoundedJson(encoded);
    REQUIRE(reparsed.has_value());
    REQUIRE(reparsed->is_object());
    const boost::json::object& obj = reparsed->get_object();
    CHECK(obj.if_contains("bridgeInstanceId") == nullptr);
    CHECK(obj.if_contains("playContextId") == nullptr);
    CHECK(obj.if_contains("clientId") == nullptr);
}

TEST_CASE("EncodeEnvelope emits JSON null for empty v2 identity fields under protocolVersion 2",
          "[protocol][envelope]") {
    auto envelope = MakeV2Envelope(std::nullopt, std::nullopt, std::nullopt);

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

TEST_CASE("EncodeEnvelope emits values for populated v2 identity fields under protocolVersion 2",
          "[protocol][envelope]") {
    auto envelope = MakeV2Envelope("bridge-1", "context-1", "client-1");

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

TEST_CASE("EncodeEnvelope round-trips a v2 envelope with mixed set and absent identity fields",
          "[protocol][envelope]") {
    auto original = MakeV2Envelope("bridge-1", std::nullopt, "client-1");

    std::string encoded = dovahlink::protocol::EncodeEnvelope(original);
    auto reparsed = dovahlink::protocol::ParseBoundedJson(encoded);
    REQUIRE(reparsed.has_value());
    auto roundTripped = dovahlink::protocol::DecodeEnvelope(*reparsed);
    REQUIRE(roundTripped.has_value());

    CHECK(roundTripped->bridgeInstanceId == original.bridgeInstanceId);
    CHECK(roundTripped->playContextId == original.playContextId);
    CHECK(roundTripped->clientId == original.clientId);
}
