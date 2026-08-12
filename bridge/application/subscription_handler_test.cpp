#include "application/subscription_handler.hpp"

#include "application/handshake_handler.hpp"
#include "protocol/messages.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/object.hpp>
#include <boost/json/parse.hpp>

#include <chrono>
#include <optional>
#include <string>

using dovahlink::application::BuildBridgeCapabilities;
using dovahlink::application::CharacterSnapshot;
using dovahlink::application::CharacterStateProvider;
using dovahlink::application::HandleClientCapabilities;
using dovahlink::application::HandleSnapshotRequest;
using dovahlink::application::HandleSubscribe;
using dovahlink::application::kSupportedProtocolVersion;
using dovahlink::application::RevisionTracker;
using dovahlink::protocol::Envelope;

namespace {

constexpr const char* kSessionId = "session-1";

class FakeCharacterStateProvider : public CharacterStateProvider {
public:
    explicit FakeCharacterStateProvider(CharacterSnapshot snapshot) : snapshot_(snapshot) {}
    [[nodiscard]] CharacterSnapshot CurrentCharacterSnapshot() const override { return snapshot_; }

private:
    CharacterSnapshot snapshot_;
};

Envelope BuildEnvelopeWithPayload(std::string messageType, const std::string& jsonPayload,
                                   std::string messageId = "message-1") {
    return Envelope{
        .protocolVersion = kSupportedProtocolVersion,
        .messageType = std::move(messageType),
        .messageId = std::move(messageId),
        .sessionId = std::string(kSessionId),
        .correlationId = std::nullopt,
        .payload = boost::json::parse(jsonPayload).get_object(),
    };
}

}  // namespace

TEST_CASE("BuildBridgeCapabilities advertises state.character version 1", "[application][subscription_handler]") {
    auto envelope = BuildBridgeCapabilities(kSessionId);
    REQUIRE(envelope.has_value());
    CHECK(envelope->messageType == "capabilities");
    REQUIRE(envelope->sessionId.has_value());
    CHECK(*envelope->sessionId == kSessionId);

    auto capabilities = dovahlink::protocol::DecodeCapabilitiesPayload(envelope->payload);
    REQUIRE(capabilities.has_value());
    REQUIRE(capabilities->capabilities.size() == 1);
    CHECK(capabilities->capabilities[0].id == "state.character");
    CHECK(capabilities->capabilities[0].version == 1);
}

TEST_CASE("HandleClientCapabilities accepts an empty capabilities list silently",
          "[application][subscription_handler]") {
    auto envelope = BuildEnvelopeWithPayload("capabilities", R"({"capabilities": []})");
    CHECK_FALSE(HandleClientCapabilities(envelope, kSessionId).has_value());
}

TEST_CASE("HandleClientCapabilities accepts a registered capability silently",
          "[application][subscription_handler]") {
    auto envelope = BuildEnvelopeWithPayload(
        "capabilities", R"({"capabilities": [{"id": "state.character", "version": 1}]})");
    CHECK_FALSE(HandleClientCapabilities(envelope, kSessionId).has_value());
}

TEST_CASE("HandleClientCapabilities rejects an unregistered capability", "[application][subscription_handler]") {
    auto envelope = BuildEnvelopeWithPayload(
        "capabilities", R"({"capabilities": [{"id": "state.inventory", "version": 1}]})");
    auto result = HandleClientCapabilities(envelope, kSessionId);
    REQUIRE(result.has_value());
    auto error = dovahlink::protocol::DecodeErrorPayload(result->payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "unsupported_capability");
}

TEST_CASE("HandleClientCapabilities rejects a malformed payload", "[application][subscription_handler]") {
    auto envelope = BuildEnvelopeWithPayload("capabilities", R"({})");
    auto result = HandleClientCapabilities(envelope, kSessionId);
    REQUIRE(result.has_value());
    auto error = dovahlink::protocol::DecodeErrorPayload(result->payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "malformed_message");
}

TEST_CASE("HandleSubscribe accepts character and returns its snapshot", "[application][subscription_handler]") {
    FakeCharacterStateProvider provider(CharacterSnapshot{.level = 12});
    RevisionTracker revisions;
    auto now = std::chrono::system_clock::now();

    auto envelope = BuildEnvelopeWithPayload("subscribe", R"({"stateAreas": ["character"]})", "message-sub-1");
    auto result = HandleSubscribe(envelope, kSessionId, provider, revisions, now);

    CHECK(result.subscriptionAck.messageType == "subscription_ack");
    REQUIRE(result.subscriptionAck.correlationId.has_value());
    CHECK(*result.subscriptionAck.correlationId == "message-sub-1");

    auto ack = dovahlink::protocol::DecodeSubscriptionAckPayload(result.subscriptionAck.payload);
    REQUIRE(ack.has_value());
    CHECK(ack->acceptedStateAreas == std::vector<std::string>{"character"});
    CHECK(ack->rejectedStateAreas.empty());

    REQUIRE(result.snapshots.size() == 1);
    CHECK(result.snapshots[0].messageType == "state_snapshot");
    REQUIRE(result.snapshots[0].correlationId.has_value());
    CHECK(*result.snapshots[0].correlationId == "message-sub-1");

    auto snapshot = dovahlink::protocol::DecodeStateSnapshotPayload(result.snapshots[0].payload);
    REQUIRE(snapshot.has_value());
    CHECK(snapshot->stateArea == "character");
    CHECK(snapshot->revision == 1);

    auto characterState = dovahlink::protocol::DecodeCharacterState(snapshot->data);
    REQUIRE(characterState.has_value());
    REQUIRE(characterState->level.has_value());
    CHECK(*characterState->level == 12);
    CHECK_FALSE(characterState->health.has_value());
}

TEST_CASE("HandleSubscribe reports an unregistered state area as rejected, not an error",
          "[application][subscription_handler]") {
    FakeCharacterStateProvider provider(CharacterSnapshot{.level = 12});
    RevisionTracker revisions;
    auto now = std::chrono::system_clock::now();

    auto envelope = BuildEnvelopeWithPayload("subscribe", R"({"stateAreas": ["inventory"]})");
    auto result = HandleSubscribe(envelope, kSessionId, provider, revisions, now);

    auto ack = dovahlink::protocol::DecodeSubscriptionAckPayload(result.subscriptionAck.payload);
    REQUIRE(ack.has_value());
    CHECK(ack->acceptedStateAreas.empty());
    CHECK(ack->rejectedStateAreas == std::vector<std::string>{"inventory"});
    CHECK(result.snapshots.empty());
}

TEST_CASE("HandleSubscribe splits a mixed request into accepted and rejected areas",
          "[application][subscription_handler]") {
    FakeCharacterStateProvider provider(CharacterSnapshot{.level = 5});
    RevisionTracker revisions;
    auto now = std::chrono::system_clock::now();

    auto envelope =
        BuildEnvelopeWithPayload("subscribe", R"({"stateAreas": ["character", "inventory"]})");
    auto result = HandleSubscribe(envelope, kSessionId, provider, revisions, now);

    auto ack = dovahlink::protocol::DecodeSubscriptionAckPayload(result.subscriptionAck.payload);
    REQUIRE(ack.has_value());
    CHECK(ack->acceptedStateAreas == std::vector<std::string>{"character"});
    CHECK(ack->rejectedStateAreas == std::vector<std::string>{"inventory"});
    CHECK(result.snapshots.size() == 1);
}

TEST_CASE("HandleSubscribe deduplicates a repeated state area and sends exactly one snapshot for it",
          "[application][subscription_handler]") {
    FakeCharacterStateProvider provider(CharacterSnapshot{.level = 5});
    RevisionTracker revisions;
    auto now = std::chrono::system_clock::now();

    auto envelope =
        BuildEnvelopeWithPayload("subscribe", R"({"stateAreas": ["character", "character"]})");
    auto result = HandleSubscribe(envelope, kSessionId, provider, revisions, now);

    auto ack = dovahlink::protocol::DecodeSubscriptionAckPayload(result.subscriptionAck.payload);
    REQUIRE(ack.has_value());
    CHECK(ack->acceptedStateAreas == std::vector<std::string>{"character"});
    CHECK(result.snapshots.size() == 1);
}

TEST_CASE("HandleSubscribe rejects a malformed payload as an error with no snapshots",
          "[application][subscription_handler]") {
    FakeCharacterStateProvider provider(CharacterSnapshot{.level = 5});
    RevisionTracker revisions;
    auto now = std::chrono::system_clock::now();

    auto envelope = BuildEnvelopeWithPayload("subscribe", R"({})");
    auto result = HandleSubscribe(envelope, kSessionId, provider, revisions, now);

    auto error = dovahlink::protocol::DecodeErrorPayload(result.subscriptionAck.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "malformed_message");
    CHECK(result.snapshots.empty());
}

TEST_CASE("HandleSnapshotRequest returns a fresh snapshot continuing the revision sequence",
          "[application][subscription_handler]") {
    FakeCharacterStateProvider provider(CharacterSnapshot{.level = 20});
    RevisionTracker revisions;
    auto now = std::chrono::system_clock::now();

    auto subscribeEnvelope = BuildEnvelopeWithPayload("subscribe", R"({"stateAreas": ["character"]})");
    auto subscribeResult = HandleSubscribe(subscribeEnvelope, kSessionId, provider, revisions, now);
    REQUIRE(subscribeResult.snapshots.size() == 1);

    auto requestEnvelope = BuildEnvelopeWithPayload("snapshot_request", R"({"stateArea": "character"})",
                                                     "message-snapshot-request-1");
    auto response = HandleSnapshotRequest(requestEnvelope, kSessionId, provider, revisions, now);

    CHECK(response.messageType == "state_snapshot");
    REQUIRE(response.correlationId.has_value());
    CHECK(*response.correlationId == "message-snapshot-request-1");

    auto snapshot = dovahlink::protocol::DecodeStateSnapshotPayload(response.payload);
    REQUIRE(snapshot.has_value());
    // Revision continues the sequence from the earlier subscribe snapshot
    // (which was 1), rather than restarting at 1 again.
    CHECK(snapshot->revision == 2);
}

TEST_CASE("HandleSnapshotRequest rejects an unregistered state area", "[application][subscription_handler]") {
    FakeCharacterStateProvider provider(CharacterSnapshot{.level = 20});
    RevisionTracker revisions;
    auto now = std::chrono::system_clock::now();

    auto envelope = BuildEnvelopeWithPayload("snapshot_request", R"({"stateArea": "inventory"})");
    auto response = HandleSnapshotRequest(envelope, kSessionId, provider, revisions, now);

    auto error = dovahlink::protocol::DecodeErrorPayload(response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "unsupported_capability");
}

TEST_CASE("HandleSnapshotRequest rejects a malformed payload", "[application][subscription_handler]") {
    FakeCharacterStateProvider provider(CharacterSnapshot{.level = 20});
    RevisionTracker revisions;
    auto now = std::chrono::system_clock::now();

    auto envelope = BuildEnvelopeWithPayload("snapshot_request", R"({})");
    auto response = HandleSnapshotRequest(envelope, kSessionId, provider, revisions, now);

    auto error = dovahlink::protocol::DecodeErrorPayload(response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "malformed_message");
}
