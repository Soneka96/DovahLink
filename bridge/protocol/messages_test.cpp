#include "protocol/messages.hpp"

#include "protocol/fixture_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/object.hpp>
#include <boost/json/parse.hpp>
#include <boost/json/value.hpp>

#include <optional>
#include <string>
#include <vector>

using dovahlink::protocol::test_support::DecodeFixtureEnvelope;

TEST_CASE("hello fixture decodes to the expected HelloPayload", "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("connection/hello.json");
    auto hello = dovahlink::protocol::DecodeHelloPayload(envelope.payload);
    REQUIRE(hello.has_value());
    CHECK(hello->endpoint == "client");
    CHECK(hello->authMethod == "one_time_local_token");
    REQUIRE(hello->authToken.has_value());
    CHECK_FALSE(hello->authToken->empty());
    CHECK(hello->clientId == "client-1");
}

TEST_CASE("hello-ack fixture decodes to the expected HelloAckPayload", "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("connection/hello-ack.json");
    auto helloAck = dovahlink::protocol::DecodeHelloAckPayload(envelope.payload);
    REQUIRE(helloAck.has_value());
    CHECK(helloAck->bridgeVersion == "0.2.0");
    CHECK(helloAck->clientIdentityKind == "unpaired");
}

TEST_CASE("hello is rejected when clientId is null", "[protocol][messages]") {
    boost::json::object payload = boost::json::parse(
                                      R"({"endpoint": "client", "clientId": null,
            "auth": {"method": "one_time_local_token", "token": "t"}})")
                                      .get_object();
    auto hello = dovahlink::protocol::DecodeHelloPayload(payload);
    REQUIRE_FALSE(hello.has_value());
}

TEST_CASE("hello is rejected when clientId is missing", "[protocol][messages]") {
    boost::json::object payload = boost::json::parse(
                                      R"({"endpoint": "client",
            "auth": {"method": "one_time_local_token", "token": "t"}})")
                                      .get_object();
    auto hello = dovahlink::protocol::DecodeHelloPayload(payload);
    REQUIRE_FALSE(hello.has_value());
}

TEST_CASE("hello-unpaired fixture decodes with no authToken", "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("connection/hello-unpaired.json");
    auto hello = dovahlink::protocol::DecodeHelloPayload(envelope.payload);
    REQUIRE(hello.has_value());
    CHECK(hello->authMethod == "unpaired");
    CHECK_FALSE(hello->authToken.has_value());
    CHECK(hello->clientId == "client-1");
}

TEST_CASE("hello-trusted-device-credential fixture decodes with an authToken",
          "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("connection/hello-trusted-device-credential.json");
    auto hello = dovahlink::protocol::DecodeHelloPayload(envelope.payload);
    REQUIRE(hello.has_value());
    CHECK(hello->authMethod == "trusted_device_credential");
    REQUIRE(hello->authToken.has_value());
    CHECK_FALSE(hello->authToken->empty());
}

TEST_CASE("hello is rejected when auth.method is unrecognized", "[protocol][messages]") {
    boost::json::object payload = boost::json::parse(
                                      R"({"endpoint": "client", "clientId": "client-1",
            "auth": {"method": "smoke-signal", "token": "t"}})")
                                      .get_object();
    auto hello = dovahlink::protocol::DecodeHelloPayload(payload);
    REQUIRE_FALSE(hello.has_value());
}

TEST_CASE("hello is rejected when auth.method is unpaired but a token is present",
          "[protocol][messages]") {
    boost::json::object payload = boost::json::parse(
                                      R"({"endpoint": "client", "clientId": "client-1",
            "auth": {"method": "unpaired", "token": "t"}})")
                                      .get_object();
    auto hello = dovahlink::protocol::DecodeHelloPayload(payload);
    REQUIRE_FALSE(hello.has_value());
}

TEST_CASE("hello is rejected when auth.method is unpaired but token is present as JSON null",
          "[protocol][messages]") {
    // A present-but-null key is a distinct wire case from an absent key: if_contains finds it
    // either way, so the rejection must not depend on the key being entirely missing.
    boost::json::object payload = boost::json::parse(
                                      R"({"endpoint": "client", "clientId": "client-1",
            "auth": {"method": "unpaired", "token": null}})")
                                      .get_object();
    auto hello = dovahlink::protocol::DecodeHelloPayload(payload);
    REQUIRE_FALSE(hello.has_value());
}

TEST_CASE("hello is rejected when auth.method is trusted_device_credential but no token is present",
          "[protocol][messages]") {
    boost::json::object payload = boost::json::parse(
                                      R"({"endpoint": "client", "clientId": "client-1",
            "auth": {"method": "trusted_device_credential"}})")
                                      .get_object();
    auto hello = dovahlink::protocol::DecodeHelloPayload(payload);
    REQUIRE_FALSE(hello.has_value());
}

TEST_CASE("hello_ack is rejected when clientIdentityKind is null", "[protocol][messages]") {
    boost::json::object payload =
        boost::json::parse(R"({"bridgeVersion": "0.1.0", "clientIdentityKind": null})").get_object();
    auto helloAck = dovahlink::protocol::DecodeHelloAckPayload(payload);
    REQUIRE_FALSE(helloAck.has_value());
}

TEST_CASE("hello_ack is rejected when clientIdentityKind is an empty string", "[protocol][messages]") {
    boost::json::object payload =
        boost::json::parse(R"({"bridgeVersion": "0.1.0", "clientIdentityKind": ""})").get_object();
    auto helloAck = dovahlink::protocol::DecodeHelloAckPayload(payload);
    REQUIRE_FALSE(helloAck.has_value());
}

TEST_CASE("hello_ack is rejected when bridgeVersion is null", "[protocol][messages]") {
    boost::json::object payload =
        boost::json::parse(R"({"bridgeVersion": null, "clientIdentityKind": "unpaired"})").get_object();
    auto helloAck = dovahlink::protocol::DecodeHelloAckPayload(payload);
    REQUIRE_FALSE(helloAck.has_value());
}

TEST_CASE("hello_ack is rejected when bridgeVersion is not a string", "[protocol][messages]") {
    boost::json::object payload =
        boost::json::parse(R"({"bridgeVersion": 1, "clientIdentityKind": "unpaired"})").get_object();
    auto helloAck = dovahlink::protocol::DecodeHelloAckPayload(payload);
    REQUIRE_FALSE(helloAck.has_value());
}

TEST_CASE("capabilities-bridge fixture decodes to the expected CapabilitiesPayload", "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("capabilities/capabilities-bridge.json");
    auto capabilities = dovahlink::protocol::DecodeCapabilitiesPayload(envelope.payload);
    REQUIRE(capabilities.has_value());
    REQUIRE(capabilities->capabilities.size() == 1);
    CHECK(capabilities->capabilities[0].id == "state.character");
    CHECK(capabilities->capabilities[0].version == 1);
}

TEST_CASE("capabilities-client fixture decodes to an empty CapabilitiesPayload", "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("capabilities/capabilities-client.json");
    auto capabilities = dovahlink::protocol::DecodeCapabilitiesPayload(envelope.payload);
    REQUIRE(capabilities.has_value());
    CHECK(capabilities->capabilities.empty());
}

TEST_CASE("subscribe fixture decodes to the expected SubscribePayload", "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("subscriptions/subscribe.json");
    auto subscribe = dovahlink::protocol::DecodeSubscribePayload(envelope.payload);
    REQUIRE(subscribe.has_value());
    CHECK(subscribe->stateAreas == std::vector<std::string>{"character"});
}

TEST_CASE("subscription-ack fixture decodes to the expected SubscriptionAckPayload", "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("subscriptions/subscription-ack.json");
    auto subscriptionAck = dovahlink::protocol::DecodeSubscriptionAckPayload(envelope.payload);
    REQUIRE(subscriptionAck.has_value());
    CHECK(subscriptionAck->acceptedStateAreas == std::vector<std::string>{"character"});
    CHECK(subscriptionAck->rejectedStateAreas.empty());
}

TEST_CASE("snapshot-request fixture decodes to the expected SnapshotRequestPayload", "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("subscriptions/snapshot-request.json");
    auto snapshotRequest = dovahlink::protocol::DecodeSnapshotRequestPayload(envelope.payload);
    REQUIRE(snapshotRequest.has_value());
    CHECK(snapshotRequest->stateArea == "character");
    REQUIRE(snapshotRequest->knownRevision.has_value());
    CHECK(*snapshotRequest->knownRevision == 2);
}

TEST_CASE("character-state-snapshot fixture decodes to the expected "
          "StateSnapshotPayload and CharacterState",
          "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("state/character/character-state-snapshot.json");
    auto snapshot = dovahlink::protocol::DecodeStateSnapshotPayload(envelope.payload);
    REQUIRE(snapshot.has_value());
    CHECK(snapshot->stateArea == "character");
    CHECK(snapshot->revision == 1);
    CHECK_FALSE(snapshot->occurredAt.empty());

    auto character = dovahlink::protocol::DecodeCharacterState(snapshot->data);
    REQUIRE(character.has_value());
    REQUIRE(character->level.has_value());
    CHECK(*character->level == 12);
    REQUIRE(character->health.has_value());
    CHECK(character->health->current == 180.0);
    CHECK(character->health->maximum == 220.0);
    REQUIRE(character->magicka.has_value());
    REQUIRE(character->stamina.has_value());
}

TEST_CASE("character-state-unavailable fixture decodes with every resource "
          "unavailable",
          "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("state/character/character-state-unavailable.json");
    auto snapshot = dovahlink::protocol::DecodeStateSnapshotPayload(envelope.payload);
    REQUIRE(snapshot.has_value());

    auto character = dovahlink::protocol::DecodeCharacterState(snapshot->data);
    REQUIRE(character.has_value());
    CHECK_FALSE(character->level.has_value());
    CHECK_FALSE(character->health.has_value());
    CHECK_FALSE(character->magicka.has_value());
    CHECK_FALSE(character->stamina.has_value());
}

TEST_CASE("character-state-event fixture decodes to the expected StateEventPayload", "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("state/character/character-state-event.json");
    auto event = dovahlink::protocol::DecodeStateEventPayload(envelope.payload);
    REQUIRE(event.has_value());
    CHECK(event->stateArea == "character");
    CHECK(event->baseRevision == 1);
    CHECK(event->revision == 2);

    auto character = dovahlink::protocol::DecodeCharacterState(event->data);
    REQUIRE(character.has_value());
    REQUIRE(character->level.has_value());
    CHECK(*character->level == 12);
}

TEST_CASE("state-event-revision-gap fixture decodes with revision higher than "
          "baseRevision + 1 from the prior event",
          "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("state/character/state-event-revision-gap.json");
    auto event = dovahlink::protocol::DecodeStateEventPayload(envelope.payload);
    REQUIRE(event.has_value());
    CHECK(event->baseRevision == 5);
    CHECK(event->revision == 6);
}

TEST_CASE("state-event-duplicate fixture decodes to the same revision as "
          "character-state-event",
          "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("state/character/state-event-duplicate.json");
    auto event = dovahlink::protocol::DecodeStateEventPayload(envelope.payload);
    REQUIRE(event.has_value());
    CHECK(event->baseRevision == 1);
    CHECK(event->revision == 2);
}

TEST_CASE("state-event-stale fixture decodes to a revision below a later "
          "current revision",
          "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("state/character/state-event-stale.json");
    auto event = dovahlink::protocol::DecodeStateEventPayload(envelope.payload);
    REQUIRE(event.has_value());
    CHECK(event->baseRevision == 0);
    CHECK(event->revision == 1);
}

TEST_CASE("error-rate-limited fixture decodes as retryable", "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("errors/error-rate-limited.json");
    auto error = dovahlink::protocol::DecodeErrorPayload(envelope.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "rate_limited");
    CHECK(error->retryable);
}

TEST_CASE("every error fixture decodes to a valid ErrorPayload", "[protocol][messages]") {
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
    for (const std::string& fixturePath : kErrorFixtures) {
        auto envelope = DecodeFixtureEnvelope(fixturePath);
        auto error = dovahlink::protocol::DecodeErrorPayload(envelope.payload);
        REQUIRE(error.has_value());
        CHECK_FALSE(error->code.empty());
        CHECK_FALSE(error->message.empty());
    }
}

TEST_CASE("EncodeHelloAckPayload round-trips the hello-ack fixture's payload", "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("connection/hello-ack.json");
    auto original = dovahlink::protocol::DecodeHelloAckPayload(envelope.payload);
    REQUIRE(original.has_value());

    boost::json::object encoded = dovahlink::protocol::EncodeHelloAckPayload(*original);
    auto roundTripped = dovahlink::protocol::DecodeHelloAckPayload(encoded);

    REQUIRE(roundTripped.has_value());
    CHECK(roundTripped->bridgeVersion == original->bridgeVersion);
    CHECK(roundTripped->clientIdentityKind == original->clientIdentityKind);
}

TEST_CASE("EncodeErrorPayload round-trips a fixture with details absent", "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("errors/error-malformed-message.json");
    auto original = dovahlink::protocol::DecodeErrorPayload(envelope.payload);
    REQUIRE(original.has_value());
    REQUIRE_FALSE(original->details.has_value());

    boost::json::object encoded = dovahlink::protocol::EncodeErrorPayload(*original);

    // details must still be present in the wire shape, as an explicit null,
    // not omitted -- see the EncodeErrorPayload declaration's comment.
    const boost::json::value* detailsValue = encoded.if_contains("details");
    REQUIRE(detailsValue != nullptr);
    CHECK(detailsValue->is_null());

    auto roundTripped = dovahlink::protocol::DecodeErrorPayload(encoded);
    REQUIRE(roundTripped.has_value());
    CHECK(roundTripped->code == original->code);
    CHECK(roundTripped->message == original->message);
    CHECK(roundTripped->retryable == original->retryable);
    CHECK_FALSE(roundTripped->details.has_value());
}

TEST_CASE("EncodeErrorPayload round-trips retryable and non-retryable values", "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("errors/error-rate-limited.json");
    auto original = dovahlink::protocol::DecodeErrorPayload(envelope.payload);
    REQUIRE(original.has_value());
    REQUIRE(original->retryable);

    boost::json::object encoded = dovahlink::protocol::EncodeErrorPayload(*original);
    auto roundTripped = dovahlink::protocol::DecodeErrorPayload(encoded);

    REQUIRE(roundTripped.has_value());
    CHECK(roundTripped->retryable);
}

TEST_CASE("EncodeErrorPayload round-trips a payload with details actually present", "[protocol][messages]") {
    // No Phase 1 error fixture has non-null details
    // (protocol/fixtures/errors/*.json all use null), so this branch is exercised
    // with a hand-built payload rather than a fixture, matching
    // protocol/fixtures/README.md's fixtures-model-valid- scenarios scope.
    dovahlink::protocol::ErrorPayload original{
        .code = "internal_error",
        .message = "worker exited unexpectedly",
        .retryable = false,
        .details = boost::json::value(boost::json::object{{"component", "worker"}}),
    };

    boost::json::object encoded = dovahlink::protocol::EncodeErrorPayload(original);

    const boost::json::value* detailsValue = encoded.if_contains("details");
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
          "[protocol][messages]") {
    auto originalEnvelope = DecodeFixtureEnvelope("connection/hello.json");

    auto errorEnvelope = dovahlink::protocol::BuildErrorEnvelope(
        originalEnvelope.messageId,
        /*sessionId=*/std::nullopt, "unauthenticated", "Invalid or expired one-time token", /*retryable=*/false);

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

TEST_CASE("BuildErrorEnvelope carries the caller's sessionId through unchanged", "[protocol][messages]") {
    auto originalEnvelope = DecodeFixtureEnvelope("subscriptions/subscribe.json");

    auto errorEnvelope = dovahlink::protocol::BuildErrorEnvelope(
        originalEnvelope.messageId,
        /*sessionId=*/std::string("session-1"), "rate_limited", "Inbound message rate exceeded 100 messages per second",
        /*retryable=*/true);

    REQUIRE(errorEnvelope.sessionId.has_value());
    CHECK(*errorEnvelope.sessionId == "session-1");
}

TEST_CASE("BuildErrorEnvelope accepts a nullopt correlationId for input that "
          "could not be decoded",
          "[protocol][messages]") {
    // Undecodable input (not even valid JSON, or missing/mistyped required
    // envelope fields) has no trustworthy messageId to correlate against --
    // this is why correlationId is a plain parameter rather than requiring
    // a whole original Envelope.
    auto errorEnvelope = dovahlink::protocol::BuildErrorEnvelope(
        /*correlationId=*/std::nullopt,
        /*sessionId=*/std::string("session-1"), "malformed_message", "Malformed message", false);

    CHECK_FALSE(errorEnvelope.correlationId.has_value());
    REQUIRE(errorEnvelope.sessionId.has_value());
    CHECK(*errorEnvelope.sessionId == "session-1");
}

TEST_CASE("EncodeCapabilitiesPayload round-trips the capabilities-bridge "
          "fixture's payload",
          "[protocol][messages]") {
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

TEST_CASE("EncodeCapabilitiesPayload round-trips an empty capabilities list", "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("capabilities/capabilities-client.json");
    auto original = dovahlink::protocol::DecodeCapabilitiesPayload(envelope.payload);
    REQUIRE(original.has_value());
    REQUIRE(original->capabilities.empty());

    boost::json::object encoded = dovahlink::protocol::EncodeCapabilitiesPayload(*original);
    auto roundTripped = dovahlink::protocol::DecodeCapabilitiesPayload(encoded);

    REQUIRE(roundTripped.has_value());
    CHECK(roundTripped->capabilities.empty());
}

TEST_CASE("EncodeSubscriptionAckPayload round-trips the subscription-ack "
          "fixture's payload",
          "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("subscriptions/subscription-ack.json");
    auto original = dovahlink::protocol::DecodeSubscriptionAckPayload(envelope.payload);
    REQUIRE(original.has_value());

    boost::json::object encoded = dovahlink::protocol::EncodeSubscriptionAckPayload(*original);
    auto roundTripped = dovahlink::protocol::DecodeSubscriptionAckPayload(encoded);

    REQUIRE(roundTripped.has_value());
    CHECK(roundTripped->acceptedStateAreas == original->acceptedStateAreas);
    CHECK(roundTripped->rejectedStateAreas == original->rejectedStateAreas);
}

TEST_CASE("EncodeStateSnapshotPayload round-trips the character-state-snapshot "
          "fixture's payload",
          "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("state/character/character-state-snapshot.json");
    auto original = dovahlink::protocol::DecodeStateSnapshotPayload(envelope.payload);
    REQUIRE(original.has_value());

    boost::json::object encoded = dovahlink::protocol::EncodeStateSnapshotPayload(*original);
    auto roundTripped = dovahlink::protocol::DecodeStateSnapshotPayload(encoded);

    REQUIRE(roundTripped.has_value());
    CHECK(roundTripped->stateArea == original->stateArea);
    CHECK(roundTripped->revision == original->revision);
    CHECK(roundTripped->occurredAt == original->occurredAt);
    CHECK(roundTripped->data == original->data);
}

// Hand-built malformed payload cases: these test the codec's rejection behavior
// for rules distinctive to each message type and are not stored as fixtures,
// matching protocol/fixtures/README.md (fixtures model valid or documented wire
// scenarios).

TEST_CASE("hello is rejected when endpoint is not 'client'", "[protocol][messages]") {
    boost::json::object payload = boost::json::parse(
                                      R"({"endpoint": "bridge", "clientId": "client-1",
            "auth": {"method": "one_time_local_token", "token": "t"}})")
                                      .get_object();
    auto hello = dovahlink::protocol::DecodeHelloPayload(payload);
    REQUIRE_FALSE(hello.has_value());
}

TEST_CASE("hello is rejected when auth.method is not 'one_time_local_token'", "[protocol][messages]") {
    boost::json::object payload = boost::json::parse(
                                      R"({"endpoint": "client", "clientId": "client-1",
            "auth": {"method": "password", "token": "t"}})")
                                      .get_object();
    auto hello = dovahlink::protocol::DecodeHelloPayload(payload);
    REQUIRE_FALSE(hello.has_value());
}

TEST_CASE("hello is rejected when auth is missing", "[protocol][messages]") {
    boost::json::object payload =
        boost::json::parse(R"({"endpoint": "client", "clientId": "client-1"})").get_object();
    auto hello = dovahlink::protocol::DecodeHelloPayload(payload);
    REQUIRE_FALSE(hello.has_value());
}

TEST_CASE("hello is rejected when clientId is present but not a string", "[protocol][messages]") {
    boost::json::object payload = boost::json::parse(
                                      R"({"endpoint": "client", "clientId": 5,
            "auth": {"method": "one_time_local_token", "token": "t"}})")
                                      .get_object();
    auto hello = dovahlink::protocol::DecodeHelloPayload(payload);
    REQUIRE_FALSE(hello.has_value());
}

TEST_CASE("hello is rejected when clientId is an empty string", "[protocol][messages]") {
    boost::json::object payload = boost::json::parse(
                                      R"({"endpoint": "client", "clientId": "",
            "auth": {"method": "one_time_local_token", "token": "t"}})")
                                      .get_object();
    auto hello = dovahlink::protocol::DecodeHelloPayload(payload);
    REQUIRE_FALSE(hello.has_value());
}

TEST_CASE("hello-ack is rejected when clientIdentityKind is present but not a string",
          "[protocol][messages]") {
    boost::json::object payload =
        boost::json::parse(R"({"bridgeVersion": "0.1.0", "clientIdentityKind": 5})").get_object();
    auto helloAck = dovahlink::protocol::DecodeHelloAckPayload(payload);
    REQUIRE_FALSE(helloAck.has_value());
}

TEST_CASE("capabilities is rejected when an entry is missing version", "[protocol][messages]") {
    boost::json::object payload = boost::json::parse(R"({"capabilities": [{"id": "state.character"}]})").get_object();
    auto capabilities = dovahlink::protocol::DecodeCapabilitiesPayload(payload);
    REQUIRE_FALSE(capabilities.has_value());
}

TEST_CASE("capabilities is rejected when an entry is not an object", "[protocol][messages]") {
    boost::json::object payload = boost::json::parse(R"({"capabilities": ["state.character"]})").get_object();
    auto capabilities = dovahlink::protocol::DecodeCapabilitiesPayload(payload);
    REQUIRE_FALSE(capabilities.has_value());
}

TEST_CASE("subscribe is rejected when stateAreas contains a non-string item", "[protocol][messages]") {
    boost::json::object payload = boost::json::parse(R"({"stateAreas": [1]})").get_object();
    auto subscribe = dovahlink::protocol::DecodeSubscribePayload(payload);
    REQUIRE_FALSE(subscribe.has_value());
}

TEST_CASE("snapshot-request decodes without knownRevision when it is absent", "[protocol][messages]") {
    boost::json::object payload = boost::json::parse(R"({"stateArea": "character"})").get_object();
    auto snapshotRequest = dovahlink::protocol::DecodeSnapshotRequestPayload(payload);
    REQUIRE(snapshotRequest.has_value());
    CHECK_FALSE(snapshotRequest->knownRevision.has_value());
}

TEST_CASE("state_snapshot is rejected when data is missing", "[protocol][messages]") {
    boost::json::object payload =
        boost::json::parse(R"({"stateArea": "character", "revision": 1, "occurredAt": "2026-08-11T12:00:00Z"})")
            .get_object();
    auto snapshot = dovahlink::protocol::DecodeStateSnapshotPayload(payload);
    REQUIRE_FALSE(snapshot.has_value());
}

TEST_CASE("state_event is rejected when baseRevision is missing", "[protocol][messages]") {
    boost::json::object payload =
        boost::json::parse(
            R"({"stateArea": "character", "revision": 2, "occurredAt": "2026-08-11T12:00:00Z", "data": {}})")
            .get_object();
    auto event = dovahlink::protocol::DecodeStateEventPayload(payload);
    REQUIRE_FALSE(event.has_value());
}

TEST_CASE("character state is rejected when a resource is present but not an object", "[protocol][messages]") {
    boost::json::object data =
        boost::json::parse(R"({"level": 1, "health": "not an object", "magicka": null, "stamina": null})").get_object();
    auto character = dovahlink::protocol::DecodeCharacterState(data);
    REQUIRE_FALSE(character.has_value());
}

TEST_CASE("character state is rejected when a resource is missing its maximum field", "[protocol][messages]") {
    boost::json::object data =
        boost::json::parse(R"({"level": 1, "health": {"current": 100.0}, "magicka": null, "stamina": null})")
            .get_object();
    auto character = dovahlink::protocol::DecodeCharacterState(data);
    REQUIRE_FALSE(character.has_value());
}

TEST_CASE("character state is rejected when level is missing", "[protocol][messages]") {
    boost::json::object data = boost::json::parse(R"({"health": null, "magicka": null, "stamina": null})").get_object();
    auto character = dovahlink::protocol::DecodeCharacterState(data);
    REQUIRE_FALSE(character.has_value());
}

TEST_CASE("error is rejected when retryable is missing", "[protocol][messages]") {
    boost::json::object payload = boost::json::parse(R"({"code": "internal_error", "message": "boom"})").get_object();
    auto error = dovahlink::protocol::DecodeErrorPayload(payload);
    REQUIRE_FALSE(error.has_value());
}

TEST_CASE("hello_ack is rejected when bridgeVersion is missing", "[protocol][messages]") {
    boost::json::object payload = boost::json::parse(R"({"clientIdentityKind": "unpaired"})").get_object();
    auto helloAck = dovahlink::protocol::DecodeHelloAckPayload(payload);
    REQUIRE_FALSE(helloAck.has_value());
}

TEST_CASE("hello_ack is rejected when bridgeVersion is empty", "[protocol][messages]") {
    boost::json::object payload =
        boost::json::parse(R"({"bridgeVersion": "", "clientIdentityKind": "unpaired"})").get_object();
    auto helloAck = dovahlink::protocol::DecodeHelloAckPayload(payload);
    REQUIRE_FALSE(helloAck.has_value());
}

TEST_CASE("hello_ack is rejected when clientIdentityKind is missing", "[protocol][messages]") {
    boost::json::object payload = boost::json::parse(R"({"bridgeVersion": "0.1.0"})").get_object();
    auto helloAck = dovahlink::protocol::DecodeHelloAckPayload(payload);
    REQUIRE_FALSE(helloAck.has_value());
}

TEST_CASE("subscription_ack is rejected when rejectedStateAreas is the wrong type", "[protocol][messages]") {
    boost::json::object payload =
        boost::json::parse(R"({"acceptedStateAreas": ["character"], "rejectedStateAreas": "none"})").get_object();
    auto subscriptionAck = dovahlink::protocol::DecodeSubscriptionAckPayload(payload);
    REQUIRE_FALSE(subscriptionAck.has_value());
}

TEST_CASE("character state is rejected when level is negative", "[protocol][messages]") {
    boost::json::object data =
        boost::json::parse(R"({"level": -1, "health": null, "magicka": null, "stamina": null})").get_object();
    auto character = dovahlink::protocol::DecodeCharacterState(data);
    REQUIRE_FALSE(character.has_value());
}

TEST_CASE("error accepts non-object, non-null details", "[protocol][messages]") {
    boost::json::object payload =
        boost::json::parse(
            R"({"code": "internal_error", "message": "boom", "retryable": false, "details": "a string detail"})")
            .get_object();
    auto error = dovahlink::protocol::DecodeErrorPayload(payload);
    REQUIRE(error.has_value());
    REQUIRE(error->details.has_value());
    CHECK(error->details->is_string());
}

TEST_CASE("error decodes non-null details when present", "[protocol][messages]") {
    boost::json::object payload =
        boost::json::parse(
            R"({"code": "internal_error", "message": "boom", "retryable": true, "details": {"hint": "x"}})")
            .get_object();
    auto error = dovahlink::protocol::DecodeErrorPayload(payload);
    REQUIRE(error.has_value());
    REQUIRE(error->details.has_value());
    CHECK(error->details->is_object());
}

TEST_CASE("pairing-status-available fixture decodes to the expected PairingStatusPayload",
          "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("pairing/pairing-status-available.json");
    auto status = dovahlink::protocol::DecodePairingStatusPayload(envelope.payload);
    REQUIRE(status.has_value());
    CHECK(status->state == "available");
}

TEST_CASE("pairing-status-in-progress fixture decodes to the expected PairingStatusPayload",
          "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("pairing/pairing-status-in-progress.json");
    auto status = dovahlink::protocol::DecodePairingStatusPayload(envelope.payload);
    REQUIRE(status.has_value());
    CHECK(status->state == "in_progress");
}

TEST_CASE("pairing-status-unavailable fixture decodes to the expected PairingStatusPayload",
          "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("pairing/pairing-status-unavailable.json");
    auto status = dovahlink::protocol::DecodePairingStatusPayload(envelope.payload);
    REQUIRE(status.has_value());
    CHECK(status->state == "unavailable");
}

TEST_CASE("PairingStatusPayload round-trips through encode then decode", "[protocol][messages]") {
    dovahlink::protocol::PairingStatusPayload original{.state = "available"};
    auto decoded = dovahlink::protocol::DecodePairingStatusPayload(
        dovahlink::protocol::EncodePairingStatusPayload(original));
    REQUIRE(decoded.has_value());
    CHECK(decoded->state == original.state);
}

TEST_CASE("pairing_status is rejected when state is not a registered value", "[protocol][messages]") {
    boost::json::object payload = boost::json::parse(R"({"state": "sideways"})").get_object();
    auto status = dovahlink::protocol::DecodePairingStatusPayload(payload);
    REQUIRE_FALSE(status.has_value());
}

TEST_CASE("pairing_status is rejected when state has the wrong type", "[protocol][messages]") {
    boost::json::object payload = boost::json::parse(R"({"state": 1})").get_object();
    auto status = dovahlink::protocol::DecodePairingStatusPayload(payload);
    REQUIRE_FALSE(status.has_value());
}

TEST_CASE("EncodePairingStatusPayload writes the state field directly", "[protocol][messages]") {
    auto encoded = dovahlink::protocol::EncodePairingStatusPayload(
        dovahlink::protocol::PairingStatusPayload{.state = "in_progress"});

    const boost::json::value* state = encoded.if_contains("state");
    REQUIRE(state != nullptr);
    REQUIRE(state->is_string());
    CHECK(state->get_string() == "in_progress");
}

TEST_CASE("pairing-confirm fixture decodes to the expected PairingConfirmPayload",
          "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("pairing/pairing-confirm.json");
    auto confirm = dovahlink::protocol::DecodePairingConfirmPayload(envelope.payload);
    REQUIRE(confirm.has_value());
    CHECK(confirm->code == "123456");
    REQUIRE(confirm->displayName.has_value());
    CHECK(*confirm->displayName == "My PC");
}

TEST_CASE("pairing_confirm decodes a null displayName as absent", "[protocol][messages]") {
    boost::json::object payload =
        boost::json::parse(R"({"code": "123456", "displayName": null})").get_object();
    auto confirm = dovahlink::protocol::DecodePairingConfirmPayload(payload);
    REQUIRE(confirm.has_value());
    CHECK_FALSE(confirm->displayName.has_value());
}

TEST_CASE("pairing_confirm decodes a missing displayName as absent", "[protocol][messages]") {
    boost::json::object payload = boost::json::parse(R"({"code": "123456"})").get_object();
    auto confirm = dovahlink::protocol::DecodePairingConfirmPayload(payload);
    REQUIRE(confirm.has_value());
    CHECK_FALSE(confirm->displayName.has_value());
}

TEST_CASE("pairing_confirm is rejected when code is missing", "[protocol][messages]") {
    boost::json::object payload = boost::json::parse(R"({"displayName": "My PC"})").get_object();
    auto confirm = dovahlink::protocol::DecodePairingConfirmPayload(payload);
    REQUIRE_FALSE(confirm.has_value());
}

TEST_CASE("pairing_confirm is rejected when code is an empty string", "[protocol][messages]") {
    boost::json::object payload = boost::json::parse(R"({"code": ""})").get_object();
    auto confirm = dovahlink::protocol::DecodePairingConfirmPayload(payload);
    REQUIRE_FALSE(confirm.has_value());
}

TEST_CASE("pairing_confirm is rejected when code has the wrong type", "[protocol][messages]") {
    boost::json::object payload = boost::json::parse(R"({"code": 123456})").get_object();
    auto confirm = dovahlink::protocol::DecodePairingConfirmPayload(payload);
    REQUIRE_FALSE(confirm.has_value());
}

TEST_CASE("pairing_confirm is rejected when displayName has the wrong type", "[protocol][messages]") {
    boost::json::object payload = boost::json::parse(R"({"code": "123456", "displayName": 42})").get_object();
    auto confirm = dovahlink::protocol::DecodePairingConfirmPayload(payload);
    REQUIRE_FALSE(confirm.has_value());
}

TEST_CASE("pairing-ack fixture decodes to the expected PairingAckPayload", "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("pairing/pairing-ack.json");
    auto ack = dovahlink::protocol::DecodePairingAckPayload(envelope.payload);
    REQUIRE(ack.has_value());
    CHECK(ack->credential == "a1b2c3d4e5f6");
}

TEST_CASE("pairing_ack is rejected when credential is missing", "[protocol][messages]") {
    boost::json::object payload = boost::json::parse(R"({})").get_object();
    auto ack = dovahlink::protocol::DecodePairingAckPayload(payload);
    REQUIRE_FALSE(ack.has_value());
}

TEST_CASE("pairing_ack is rejected when credential is an empty string", "[protocol][messages]") {
    boost::json::object payload = boost::json::parse(R"({"credential": ""})").get_object();
    auto ack = dovahlink::protocol::DecodePairingAckPayload(payload);
    REQUIRE_FALSE(ack.has_value());
}

TEST_CASE("pairing_ack is rejected when credential has the wrong type", "[protocol][messages]") {
    boost::json::object payload = boost::json::parse(R"({"credential": 12345})").get_object();
    auto ack = dovahlink::protocol::DecodePairingAckPayload(payload);
    REQUIRE_FALSE(ack.has_value());
}

TEST_CASE("pairing-outcome-credential-issued fixture decodes with a credential and displayName, "
          "no shortId",
          "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("pairing/pairing-outcome-credential-issued.json");
    auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(envelope.payload);
    REQUIRE(outcome.has_value());
    CHECK(outcome->outcome == "credential_issued");
    REQUIRE(outcome->credential.has_value());
    CHECK(*outcome->credential == "a1b2c3d4e5f6");
    CHECK_FALSE(outcome->shortId.has_value());
    REQUIRE(outcome->displayName.has_value());
    CHECK(*outcome->displayName == "My PC");
}

TEST_CASE("pairing-outcome-trusted fixture decodes with a credential and shortId",
          "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("pairing/pairing-outcome-trusted.json");
    auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(envelope.payload);
    REQUIRE(outcome.has_value());
    CHECK(outcome->outcome == "trusted");
    REQUIRE(outcome->credential.has_value());
    REQUIRE(outcome->shortId.has_value());
    CHECK(*outcome->shortId == "12345");
}

TEST_CASE("pairing-outcome-already-trusted fixture decodes with a credential and shortId",
          "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("pairing/pairing-outcome-already-trusted.json");
    auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(envelope.payload);
    REQUIRE(outcome.has_value());
    CHECK(outcome->outcome == "already_trusted");
    REQUIRE(outcome->credential.has_value());
    REQUIRE(outcome->shortId.has_value());
}

TEST_CASE("pairing-outcome-expired fixture decodes with no credential", "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("pairing/pairing-outcome-expired.json");
    auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(envelope.payload);
    REQUIRE(outcome.has_value());
    CHECK(outcome->outcome == "expired");
    CHECK_FALSE(outcome->credential.has_value());
    CHECK_FALSE(outcome->shortId.has_value());
    CHECK_FALSE(outcome->displayName.has_value());
}

TEST_CASE("pairing-outcome-invalid fixture decodes with no credential", "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("pairing/pairing-outcome-invalid.json");
    auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(envelope.payload);
    REQUIRE(outcome.has_value());
    CHECK(outcome->outcome == "invalid");
}

TEST_CASE("pairing-outcome-rate-limited fixture decodes with no credential", "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("pairing/pairing-outcome-rate-limited.json");
    auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(envelope.payload);
    REQUIRE(outcome.has_value());
    CHECK(outcome->outcome == "rate_limited");
}

TEST_CASE("pairing-outcome-pending-not-found fixture decodes with no credential",
          "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("pairing/pairing-outcome-pending-not-found.json");
    auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(envelope.payload);
    REQUIRE(outcome.has_value());
    CHECK(outcome->outcome == "pending_not_found");
}

TEST_CASE("PairingOutcomePayload round-trips through encode then decode", "[protocol][messages]") {
    dovahlink::protocol::PairingOutcomePayload original{
        .outcome = "trusted",
        .credential = std::string("a1b2c3"),
        .shortId = std::string("12345"),
        .displayName = std::nullopt,
    };
    auto decoded = dovahlink::protocol::DecodePairingOutcomePayload(
        dovahlink::protocol::EncodePairingOutcomePayload(original));
    REQUIRE(decoded.has_value());
    CHECK(decoded->outcome == original.outcome);
    CHECK(decoded->credential == original.credential);
    CHECK(decoded->shortId == original.shortId);
    CHECK(decoded->displayName == original.displayName);
}

TEST_CASE("pairing_outcome is rejected when outcome is not a registered value", "[protocol][messages]") {
    boost::json::object payload =
        boost::json::parse(R"({"outcome": "vibes", "credential": null, "shortId": null, "displayName": null})")
            .get_object();
    auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(payload);
    REQUIRE_FALSE(outcome.has_value());
}

TEST_CASE("pairing_outcome is rejected when credential is present but not a string",
          "[protocol][messages]") {
    boost::json::object payload =
        boost::json::parse(R"({"outcome": "trusted", "credential": 42, "shortId": null, "displayName": null})")
            .get_object();
    auto outcome = dovahlink::protocol::DecodePairingOutcomePayload(payload);
    REQUIRE_FALSE(outcome.has_value());
}
