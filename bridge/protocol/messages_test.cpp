#include "protocol/messages.hpp"

#include "protocol/fixture_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/object.hpp>
#include <boost/json/parse.hpp>
#include <boost/json/value.hpp>

#include <cstdint>
#include <string>
#include <vector>

using dovahlink::protocol::test_support::DecodeFixtureEnvelope;

TEST_CASE("hello fixture decodes to the expected HelloPayload", "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("connection/hello.json");
    auto hello = dovahlink::protocol::DecodeHelloPayload(envelope.payload);
    REQUIRE(hello.has_value());
    CHECK(hello->endpoint == "client");
    CHECK(hello->supportedProtocolVersions == std::vector<std::int64_t>{1});
    CHECK(hello->authMethod == "one_time_local_token");
    CHECK_FALSE(hello->authToken.empty());
    CHECK_FALSE(hello->clientId.has_value());
}

TEST_CASE("hello-ack fixture decodes to the expected HelloAckPayload", "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("connection/hello-ack.json");
    auto helloAck = dovahlink::protocol::DecodeHelloAckPayload(envelope.payload);
    REQUIRE(helloAck.has_value());
    CHECK(helloAck->selectedProtocolVersion == 1);
    CHECK_FALSE(helloAck->clientIdentityKind.has_value());
}

TEST_CASE("v2 hello fixture decodes with its clientId", "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("v2/connection/hello.json");
    auto hello = dovahlink::protocol::DecodeHelloPayload(envelope.payload);
    REQUIRE(hello.has_value());
    CHECK(hello->supportedProtocolVersions == (std::vector<std::int64_t>{1, 2}));
    REQUIRE(hello->clientId.has_value());
    CHECK(*hello->clientId == "client-1");
}

TEST_CASE("v2 hello-ack fixture decodes with its clientIdentityKind", "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("v2/connection/hello-ack.json");
    auto helloAck = dovahlink::protocol::DecodeHelloAckPayload(envelope.payload);
    REQUIRE(helloAck.has_value());
    CHECK(helloAck->selectedProtocolVersion == 2);
    REQUIRE(helloAck->clientIdentityKind.has_value());
    CHECK(*helloAck->clientIdentityKind == "unpaired");
}

TEST_CASE("hello decodes an explicit null clientId the same as absent", "[protocol][messages]") {
    boost::json::object payload = boost::json::parse(
                                      R"({"endpoint": "client", "supportedProtocolVersions": [1, 2], "clientId": null,
            "auth": {"method": "one_time_local_token", "token": "t"}})")
                                      .get_object();
    auto hello = dovahlink::protocol::DecodeHelloPayload(payload);
    REQUIRE(hello.has_value());
    CHECK_FALSE(hello->clientId.has_value());
}

TEST_CASE("hello-ack decodes an explicit null clientIdentityKind the same as absent",
          "[protocol][messages]") {
    boost::json::object payload =
        boost::json::parse(R"({"selectedProtocolVersion": 2, "clientIdentityKind": null})").get_object();
    auto helloAck = dovahlink::protocol::DecodeHelloAckPayload(payload);
    REQUIRE(helloAck.has_value());
    CHECK_FALSE(helloAck->clientIdentityKind.has_value());
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

TEST_CASE("error-unsupported-version fixture decodes to the expected ErrorPayload", "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("errors/error-unsupported-version.json");
    CHECK(envelope.protocolVersion == 0);
    CHECK(envelope.messageType == "error");
    CHECK_FALSE(envelope.sessionId.has_value());
    REQUIRE(envelope.correlationId.has_value());
    CHECK(*envelope.correlationId == "message-hello-1");

    auto error = dovahlink::protocol::DecodeErrorPayload(envelope.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "unsupported_version");
    CHECK_FALSE(error->message.empty());
    CHECK_FALSE(error->retryable);
    CHECK_FALSE(error->details.has_value());
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
        "errors/error-unsupported-version.json",
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
    CHECK(roundTripped->selectedProtocolVersion == original->selectedProtocolVersion);
    CHECK(roundTripped->clientIdentityKind == original->clientIdentityKind);
}

TEST_CASE("EncodeHelloAckPayload omits clientIdentityKind for a v1 selection, even when set",
          "[protocol][messages]") {
    // Proves the gate is selectedProtocolVersion, not whether the optional
    // happens to be empty, matching the envelope's own v1-omit/v2-present rule.
    dovahlink::protocol::HelloAckPayload payload{
        .selectedProtocolVersion = 1,
        .clientIdentityKind = std::string("unpaired"),
    };

    boost::json::object encoded = dovahlink::protocol::EncodeHelloAckPayload(payload);

    CHECK(encoded.if_contains("clientIdentityKind") == nullptr);
}

TEST_CASE("EncodeHelloAckPayload round-trips the v2 hello-ack fixture's clientIdentityKind",
          "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("v2/connection/hello-ack.json");
    auto original = dovahlink::protocol::DecodeHelloAckPayload(envelope.payload);
    REQUIRE(original.has_value());

    boost::json::object encoded = dovahlink::protocol::EncodeHelloAckPayload(*original);
    REQUIRE(encoded.if_contains("clientIdentityKind") != nullptr);
    CHECK(encoded.at("clientIdentityKind").as_string() == "unpaired");

    auto roundTripped = dovahlink::protocol::DecodeHelloAckPayload(encoded);
    REQUIRE(roundTripped.has_value());
    CHECK(roundTripped->clientIdentityKind == original->clientIdentityKind);
}

TEST_CASE("EncodeHelloAckPayload emits JSON null for an empty clientIdentityKind at v2",
          "[protocol][messages]") {
    dovahlink::protocol::HelloAckPayload payload{
        .selectedProtocolVersion = 2,
        .clientIdentityKind = std::nullopt,
    };

    boost::json::object encoded = dovahlink::protocol::EncodeHelloAckPayload(payload);

    REQUIRE(encoded.if_contains("clientIdentityKind") != nullptr);
    CHECK(encoded.at("clientIdentityKind").is_null());
}

TEST_CASE("EncodeHelloAckPayload also emits clientIdentityKind at selectedProtocolVersion 3",
          "[protocol][messages]") {
    // Guards the >= 2 gate against narrowing to == 2, matching envelope.cpp's
    // equivalent boundary test.
    dovahlink::protocol::HelloAckPayload payload{
        .selectedProtocolVersion = 3,
        .clientIdentityKind = std::string("unpaired"),
    };

    boost::json::object encoded = dovahlink::protocol::EncodeHelloAckPayload(payload);

    REQUIRE(encoded.if_contains("clientIdentityKind") != nullptr);
    CHECK(encoded.at("clientIdentityKind").as_string() == "unpaired");
}

TEST_CASE("EncodeErrorPayload round-trips a fixture with details absent", "[protocol][messages]") {
    auto envelope = DecodeFixtureEnvelope("errors/error-unsupported-version.json");
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
        originalEnvelope.messageId, /*protocolVersion=*/0,
        /*sessionId=*/std::nullopt, "unauthenticated", "Invalid or expired one-time token", /*retryable=*/false);

    CHECK(errorEnvelope.protocolVersion == 0);
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

TEST_CASE("BuildErrorEnvelope carries the caller's protocolVersion and "
          "sessionId through unchanged",
          "[protocol][messages]") {
    auto originalEnvelope = DecodeFixtureEnvelope("subscriptions/subscribe.json");

    auto errorEnvelope = dovahlink::protocol::BuildErrorEnvelope(
        originalEnvelope.messageId, /*protocolVersion=*/1,
        /*sessionId=*/std::string("session-1"), "rate_limited", "Inbound message rate exceeded 100 messages per second",
        /*retryable=*/true);

    CHECK(errorEnvelope.protocolVersion == 1);
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
        /*correlationId=*/std::nullopt, /*protocolVersion=*/1,
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
                                      R"({"endpoint": "bridge", "supportedProtocolVersions": [1],
            "auth": {"method": "one_time_local_token", "token": "t"}})")
                                      .get_object();
    auto hello = dovahlink::protocol::DecodeHelloPayload(payload);
    REQUIRE_FALSE(hello.has_value());
}

TEST_CASE("hello is rejected when auth.method is not 'one_time_local_token'", "[protocol][messages]") {
    boost::json::object payload = boost::json::parse(
                                      R"({"endpoint": "client", "supportedProtocolVersions": [1],
            "auth": {"method": "password", "token": "t"}})")
                                      .get_object();
    auto hello = dovahlink::protocol::DecodeHelloPayload(payload);
    REQUIRE_FALSE(hello.has_value());
}

TEST_CASE("hello is rejected when auth is missing", "[protocol][messages]") {
    boost::json::object payload =
        boost::json::parse(R"({"endpoint": "client", "supportedProtocolVersions": [1]})").get_object();
    auto hello = dovahlink::protocol::DecodeHelloPayload(payload);
    REQUIRE_FALSE(hello.has_value());
}

TEST_CASE("hello is rejected when clientId is present but not a string", "[protocol][messages]") {
    boost::json::object payload = boost::json::parse(
                                      R"({"endpoint": "client", "supportedProtocolVersions": [1, 2], "clientId": 5,
            "auth": {"method": "one_time_local_token", "token": "t"}})")
                                      .get_object();
    auto hello = dovahlink::protocol::DecodeHelloPayload(payload);
    REQUIRE_FALSE(hello.has_value());
}

TEST_CASE("hello is rejected when clientId is an empty string", "[protocol][messages]") {
    boost::json::object payload = boost::json::parse(
                                      R"({"endpoint": "client", "supportedProtocolVersions": [1, 2], "clientId": "",
            "auth": {"method": "one_time_local_token", "token": "t"}})")
                                      .get_object();
    auto hello = dovahlink::protocol::DecodeHelloPayload(payload);
    REQUIRE_FALSE(hello.has_value());
}

TEST_CASE("hello-ack is rejected when clientIdentityKind is present but not a string",
          "[protocol][messages]") {
    boost::json::object payload =
        boost::json::parse(R"({"selectedProtocolVersion": 2, "clientIdentityKind": 5})").get_object();
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

TEST_CASE("hello_ack is rejected when selectedProtocolVersion is missing", "[protocol][messages]") {
    boost::json::object payload = boost::json::parse(R"({})").get_object();
    auto helloAck = dovahlink::protocol::DecodeHelloAckPayload(payload);
    REQUIRE_FALSE(helloAck.has_value());
}

TEST_CASE("subscription_ack is rejected when rejectedStateAreas is the wrong type", "[protocol][messages]") {
    boost::json::object payload =
        boost::json::parse(R"({"acceptedStateAreas": ["character"], "rejectedStateAreas": "none"})").get_object();
    auto subscriptionAck = dovahlink::protocol::DecodeSubscriptionAckPayload(payload);
    REQUIRE_FALSE(subscriptionAck.has_value());
}

TEST_CASE("hello accepts an empty supportedProtocolVersions array", "[protocol][messages]") {
    // Decoding does not enforce a non-empty intersection with what the bridge
    // supports; an empty list structurally decodes, and the resulting "no
    // mutually supported version" outcome is an application-layer negotiation
    // concern, not a codec one.
    boost::json::object payload = boost::json::parse(
                                      R"({"endpoint": "client", "supportedProtocolVersions": [],
            "auth": {"method": "one_time_local_token", "token": "t"}})")
                                      .get_object();
    auto hello = dovahlink::protocol::DecodeHelloPayload(payload);
    REQUIRE(hello.has_value());
    CHECK(hello->supportedProtocolVersions.empty());
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
