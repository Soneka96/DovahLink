#include "application/subscription_handler.hpp"

#include "application/application_test_support.hpp"
#include "protocol/messages.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/object.hpp>
#include <boost/json/parse.hpp>

#include <optional>
#include <string>
#include <utility>

using dovahlink::application::BuildBridgeCapabilities;
using dovahlink::application::HandleClientCapabilities;
using dovahlink::application::HandleSnapshotRequest;
using dovahlink::application::HandleSubscribe;
using dovahlink::application::test_support::BuildEnvelope;
using dovahlink::protocol::Envelope;

namespace {

constexpr const char* kSessionId = "session-1";

///  Builds a session-bound envelope from a JSON object payload.
Envelope BuildEnvelopeWithPayload(std::string messageType,
                                  const std::string& jsonPayload,
                                  std::string messageId = "message-1") {
    return BuildEnvelope(std::move(messageType), std::move(messageId),
                         std::string(kSessionId), std::nullopt,
                         boost::json::parse(jsonPayload).get_object());
}

} //  namespace

TEST_CASE("BuildBridgeCapabilities advertises an empty capability list",
          "[application][subscription_handler]") {
    auto envelope = BuildBridgeCapabilities(kSessionId);
    REQUIRE(envelope.has_value());
    CHECK(envelope->messageType == "capabilities");
    REQUIRE(envelope->sessionId.has_value());
    CHECK(*envelope->sessionId == kSessionId);

    auto capabilities =
        dovahlink::protocol::DecodeCapabilitiesPayload(envelope->payload);
    REQUIRE(capabilities.has_value());
    CHECK(capabilities->capabilities.empty());
}

TEST_CASE(
    "HandleClientCapabilities accepts an empty capabilities list silently",
    "[application][subscription_handler]") {
    auto envelope =
        BuildEnvelopeWithPayload("capabilities", R"({"capabilities": []})");
    CHECK_FALSE(HandleClientCapabilities(envelope, kSessionId).has_value());
}

TEST_CASE("HandleClientCapabilities rejects any non-empty capability list",
          "[application][subscription_handler]") {
    auto envelope = BuildEnvelopeWithPayload(
        "capabilities",
        R"({"capabilities": [{"id": "state.inventory", "version": 1}]})");
    auto result = HandleClientCapabilities(envelope, kSessionId);
    REQUIRE(result.has_value());
    auto error = dovahlink::protocol::DecodeErrorPayload(result->payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "unsupported_capability");
}

TEST_CASE("HandleClientCapabilities reports only the first entry when multiple "
          "are present",
          "[application][subscription_handler]") {
    auto envelope = BuildEnvelopeWithPayload(
        "capabilities",
        R"({"capabilities": [{"id": "state.inventory", "version": 1}, {"id": "state.quests", "version": 1}]})");
    auto result = HandleClientCapabilities(envelope, kSessionId);
    REQUIRE(result.has_value());
    auto error = dovahlink::protocol::DecodeErrorPayload(result->payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "unsupported_capability");
    CHECK(error->message.find("state.inventory") != std::string::npos);
    CHECK(error->message.find("state.quests") == std::string::npos);
}

TEST_CASE("HandleClientCapabilities rejects a malformed payload",
          "[application][subscription_handler]") {
    auto envelope = BuildEnvelopeWithPayload("capabilities", R"({})");
    auto result = HandleClientCapabilities(envelope, kSessionId);
    REQUIRE(result.has_value());
    auto error = dovahlink::protocol::DecodeErrorPayload(result->payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "malformed_message");
}

TEST_CASE("HandleSubscribe rejects every requested state area, producing no "
          "snapshots",
          "[application][subscription_handler]") {
    auto envelope = BuildEnvelopeWithPayload(
        "subscribe", R"({"stateAreas": ["example_area"]})", "message-sub-1");
    auto result = HandleSubscribe(envelope, kSessionId);

    CHECK(result.subscriptionAck.messageType == "subscription_ack");
    REQUIRE(result.subscriptionAck.correlationId.has_value());
    CHECK(*result.subscriptionAck.correlationId == "message-sub-1");

    auto ack = dovahlink::protocol::DecodeSubscriptionAckPayload(
        result.subscriptionAck.payload);
    REQUIRE(ack.has_value());
    CHECK(ack->acceptedStateAreas.empty());
    CHECK(ack->rejectedStateAreas == std::vector<std::string>{"example_area"});

    CHECK(result.snapshots.empty());
    CHECK(result.acceptedStateAreas.empty());
}

TEST_CASE(
    "HandleSubscribe rejects multiple requested state areas, each exactly once",
    "[application][subscription_handler]") {
    auto envelope = BuildEnvelopeWithPayload(
        "subscribe", R"({"stateAreas": ["example_area", "other_area"]})");
    auto result = HandleSubscribe(envelope, kSessionId);

    auto ack = dovahlink::protocol::DecodeSubscriptionAckPayload(
        result.subscriptionAck.payload);
    REQUIRE(ack.has_value());
    CHECK(ack->acceptedStateAreas.empty());
    CHECK(ack->rejectedStateAreas ==
          std::vector<std::string>{"example_area", "other_area"});
}

TEST_CASE(
    "HandleSubscribe deduplicates a repeated state area in the rejection list",
    "[application][subscription_handler]") {
    auto envelope = BuildEnvelopeWithPayload(
        "subscribe", R"({"stateAreas": ["example_area", "example_area"]})");
    auto result = HandleSubscribe(envelope, kSessionId);

    auto ack = dovahlink::protocol::DecodeSubscriptionAckPayload(
        result.subscriptionAck.payload);
    REQUIRE(ack.has_value());
    CHECK(ack->rejectedStateAreas == std::vector<std::string>{"example_area"});
}

TEST_CASE("HandleSubscribe deduplicates a non-adjacent repeat, preserving "
          "first-occurrence order",
          "[application][subscription_handler]") {
    auto envelope = BuildEnvelopeWithPayload(
        "subscribe", R"({"stateAreas": ["area_a", "area_b", "area_a"]})");
    auto result = HandleSubscribe(envelope, kSessionId);

    auto ack = dovahlink::protocol::DecodeSubscriptionAckPayload(
        result.subscriptionAck.payload);
    REQUIRE(ack.has_value());
    CHECK(ack->rejectedStateAreas ==
          std::vector<std::string>{"area_a", "area_b"});
}

TEST_CASE(
    "HandleSubscribe accepts an empty stateAreas list as a no-op unsubscribe",
    "[application][subscription_handler]") {
    auto envelope =
        BuildEnvelopeWithPayload("subscribe", R"({"stateAreas": []})");
    auto result = HandleSubscribe(envelope, kSessionId);

    auto ack = dovahlink::protocol::DecodeSubscriptionAckPayload(
        result.subscriptionAck.payload);
    REQUIRE(ack.has_value());
    CHECK(ack->acceptedStateAreas.empty());
    CHECK(ack->rejectedStateAreas.empty());
    CHECK(result.snapshots.empty());
}

TEST_CASE(
    "HandleSubscribe rejects a malformed payload as an error with no snapshots",
    "[application][subscription_handler]") {
    auto envelope = BuildEnvelopeWithPayload("subscribe", R"({})");
    auto result = HandleSubscribe(envelope, kSessionId);

    auto error =
        dovahlink::protocol::DecodeErrorPayload(result.subscriptionAck.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "malformed_message");
    CHECK(result.snapshots.empty());
}

TEST_CASE(
    "HandleSnapshotRequest rejects every state area as unsupported_capability",
    "[application][subscription_handler]") {
    auto envelope = BuildEnvelopeWithPayload("snapshot_request",
                                             R"({"stateArea": "example_area"})");
    auto response = HandleSnapshotRequest(envelope, kSessionId);

    auto error = dovahlink::protocol::DecodeErrorPayload(response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "unsupported_capability");
}

TEST_CASE("HandleSnapshotRequest rejects a malformed payload",
          "[application][subscription_handler]") {
    auto envelope = BuildEnvelopeWithPayload("snapshot_request", R"({})");
    auto response = HandleSnapshotRequest(envelope, kSessionId);

    auto error = dovahlink::protocol::DecodeErrorPayload(response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "malformed_message");
}
