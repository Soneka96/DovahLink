#include "application/subscription_handler.hpp"

#include "protocol/messages.hpp"

#include <algorithm>
#include <type_traits>
#include <utility>

namespace dovahlink::application {

namespace {

static_assert(std::is_nothrow_move_constructible_v<protocol::Envelope>);
static_assert(std::is_nothrow_move_constructible_v<SubscribeResult>);

}

std::optional<protocol::Envelope> BuildBridgeCapabilities(const std::string& sessionId) {
    protocol::CapabilitiesPayload payload{};
    return protocol::BuildEnvelope(std::string(protocol::message_type::kCapabilities), sessionId,
                                    /*correlationId=*/std::nullopt, protocol::EncodeCapabilitiesPayload(payload));
}

std::optional<protocol::Envelope> HandleClientCapabilities(const protocol::Envelope& capabilitiesEnvelope,
                                                             const std::string& sessionId) {
    auto capabilities = protocol::DecodeCapabilitiesPayload(capabilitiesEnvelope.payload);
    if (!capabilities.has_value()) {
        return protocol::BuildErrorEnvelope(capabilitiesEnvelope.messageId, sessionId, "malformed_message",
                                             "Malformed capabilities payload", false);
    }
    if (!capabilities->capabilities.empty()) {
        return protocol::BuildErrorEnvelope(
            capabilitiesEnvelope.messageId, sessionId, "unsupported_capability",
            "Unsupported capability: " + capabilities->capabilities.front().id, false);
    }
    return std::nullopt;
}

SubscribeResult HandleSubscribe(const protocol::Envelope& subscribeEnvelope, const std::string& sessionId) {
    auto subscribe = protocol::DecodeSubscribePayload(subscribeEnvelope.payload);
    if (!subscribe.has_value()) {
        return SubscribeResult{
            .subscriptionAck = protocol::BuildErrorEnvelope(subscribeEnvelope.messageId, sessionId,
                                                              "malformed_message", "Malformed subscribe payload",
                                                              false),
        };
    }

    // No state area is currently registered (protocol/schema/README.md's "Registered state
    // areas"), so every requested area is rejected. Deduplicated the same way an accepted area
    // would be, so a repeated area is not reported twice.
    std::vector<std::string> rejected;
    for (const std::string& area : subscribe->stateAreas) {
        if (std::find(rejected.begin(), rejected.end(), area) == rejected.end()) {
            rejected.push_back(area);
        }
    }

    auto ackEnvelope = protocol::BuildEnvelope(
        std::string(protocol::message_type::kSubscriptionAck), sessionId, subscribeEnvelope.messageId,
        protocol::EncodeSubscriptionAckPayload(protocol::SubscriptionAckPayload{
            .acceptedStateAreas = {},
            .rejectedStateAreas = std::move(rejected),
        }));
    if (!ackEnvelope.has_value()) {
        return SubscribeResult{
            .subscriptionAck = protocol::BuildErrorEnvelope(subscribeEnvelope.messageId, sessionId,
                                                              "internal_error", "Unable to build response", false),
        };
    }

    return SubscribeResult{
        .subscriptionAck = std::move(*ackEnvelope),
    };
}

protocol::Envelope HandleSnapshotRequest(const protocol::Envelope& snapshotRequestEnvelope,
                                          const std::string& sessionId) {
    auto request = protocol::DecodeSnapshotRequestPayload(snapshotRequestEnvelope.payload);
    if (!request.has_value()) {
        return protocol::BuildErrorEnvelope(snapshotRequestEnvelope.messageId, sessionId, "malformed_message",
                                             "Malformed snapshot_request payload", false);
    }
    // No state area is currently registered (protocol/schema/README.md's "Registered state
    // areas"), so every request is rejected.
    return protocol::BuildErrorEnvelope(snapshotRequestEnvelope.messageId, sessionId, "unsupported_capability",
                                         "Unknown state area: " + request->stateArea, false);
}

}  // namespace dovahlink::application
