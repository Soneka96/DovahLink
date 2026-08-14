#include "application/subscription_handler.hpp"

#include "application/handshake_handler.hpp"
#include "application/timestamp.hpp"
#include "protocol/messages.hpp"

#include <algorithm>
#include <cassert>
#include <type_traits>
#include <utility>

namespace dovahlink::application {

namespace {

constexpr std::string_view kCharacterCapabilityId = "state.character";
constexpr std::int64_t kCharacterCapabilityVersion = 1;

static_assert(std::is_nothrow_move_constructible_v<protocol::Envelope>);
static_assert(std::is_nothrow_move_constructible_v<SubscribeResult>);

/// Prepares a character snapshot at the next revision without committing it.
/// @param correlationId Message ID that caused the snapshot, when applicable.
/// @param snapshotBuilder Builds the fallible envelope before revision commit.
std::optional<protocol::Envelope> PrepareCharacterSnapshot(
    const std::string& sessionId, std::optional<std::string> correlationId,
    const CharacterStateProvider& stateProvider, const RevisionTracker& revisions,
    const std::string& stateArea, std::int64_t& revision,
    std::chrono::system_clock::time_point now, SnapshotEnvelopeBuilder& snapshotBuilder) {
    CharacterSnapshot snapshot = stateProvider.CurrentCharacterSnapshot();
    revision = revisions.NextSnapshotRevision(stateArea);
    return snapshotBuilder(sessionId, std::move(correlationId), snapshot, revision, now);
}

/// Commits the previously prepared character snapshot revision.
void CommitCharacterSnapshot(RevisionTracker& revisions, const std::string& stateArea,
                             std::int64_t revision) {
    std::int64_t committedRevision = revisions.StartSnapshot(stateArea);
    assert(committedRevision == revision);
    (void)committedRevision;
}

}

std::optional<protocol::Envelope> BuildCharacterSnapshotEnvelope(
    const std::string& sessionId, std::optional<std::string> correlationId,
    const CharacterSnapshot& snapshot, std::int64_t revision,
    std::chrono::system_clock::time_point now) {
    protocol::StateSnapshotPayload payload =
        BuildCharacterSnapshotPayload(snapshot, revision, FormatTimestamp(now));
    return protocol::BuildEnvelope(kSupportedProtocolVersion, std::string(protocol::message_type::kStateSnapshot),
                                   sessionId, std::move(correlationId),
                                   protocol::EncodeStateSnapshotPayload(payload));
}

std::optional<protocol::Envelope> BuildBridgeCapabilities(const std::string& sessionId) {
    protocol::CapabilitiesPayload payload{
        .capabilities = {protocol::Capability{
            .id = std::string(kCharacterCapabilityId),
            .version = kCharacterCapabilityVersion,
        }},
    };
    return protocol::BuildEnvelope(kSupportedProtocolVersion, std::string(protocol::message_type::kCapabilities),
                                    sessionId, /*correlationId=*/std::nullopt,
                                    protocol::EncodeCapabilitiesPayload(payload));
}

std::optional<protocol::Envelope> HandleClientCapabilities(const protocol::Envelope& capabilitiesEnvelope,
                                                             const std::string& sessionId) {
    auto capabilities = protocol::DecodeCapabilitiesPayload(capabilitiesEnvelope.payload);
    if (!capabilities.has_value()) {
        return protocol::BuildErrorEnvelope(capabilitiesEnvelope.messageId, kSupportedProtocolVersion, sessionId,
                                             "malformed_message", "Malformed capabilities payload", false);
    }
    for (const protocol::Capability& capability : capabilities->capabilities) {
        if (capability.id != kCharacterCapabilityId ||
            capability.version != kCharacterCapabilityVersion) {
            return protocol::BuildErrorEnvelope(capabilitiesEnvelope.messageId, kSupportedProtocolVersion, sessionId,
                                                 "unsupported_capability",
                                                 "Unsupported capability: " + capability.id + " version " +
                                                     std::to_string(capability.version),
                                                 false);
        }
    }
    return std::nullopt;
}

SubscribeResult HandleSubscribe(const protocol::Envelope& subscribeEnvelope, const std::string& sessionId,
                                 const CharacterStateProvider& stateProvider, RevisionTracker& revisions,
                                 std::chrono::system_clock::time_point now,
                                 SnapshotEnvelopeBuilder& snapshotBuilder) {
    auto subscribe = protocol::DecodeSubscribePayload(subscribeEnvelope.payload);
    if (!subscribe.has_value()) {
        return SubscribeResult{
            .subscriptionAck = protocol::BuildErrorEnvelope(subscribeEnvelope.messageId, kSupportedProtocolVersion,
                                                              sessionId, "malformed_message",
                                                              "Malformed subscribe payload", false),
        };
    }

    std::vector<std::string> accepted;
    std::vector<std::string> rejected;
    for (const std::string& area : subscribe->stateAreas) {
        // Deduplicate: a client that lists the same area twice should not
        // see it twice in subscriptionAck, and must still get exactly one
        // snapshot for it (see the !accepted.empty() check below).
        bool alreadySeen = std::find(accepted.begin(), accepted.end(), area) != accepted.end() ||
                            std::find(rejected.begin(), rejected.end(), area) != rejected.end();
        if (alreadySeen) {
            continue;
        }
        if (area == protocol::state_area::kCharacter) {
            accepted.push_back(area);
        } else {
            rejected.push_back(area);
        }
    }

    // v1 has exactly one registered state area, so `accepted` can only ever
    // be empty or contain "character" -- a loop building "one snapshot per
    // accepted area" would have nothing to iterate over beyond this single
    // case.
    const std::string stateArea(protocol::state_area::kCharacter);
    auto ackEnvelope = protocol::BuildEnvelope(
        kSupportedProtocolVersion, std::string(protocol::message_type::kSubscriptionAck), sessionId,
        subscribeEnvelope.messageId,
        protocol::EncodeSubscriptionAckPayload(protocol::SubscriptionAckPayload{
            .acceptedStateAreas = accepted,
            .rejectedStateAreas = rejected,
        }));
    if (!ackEnvelope.has_value()) {
        return SubscribeResult{
            .subscriptionAck = protocol::BuildErrorEnvelope(subscribeEnvelope.messageId, kSupportedProtocolVersion,
                                                              sessionId, "internal_error",
                                                              "Unable to build response", false),
        };
    }

    std::vector<protocol::Envelope> snapshots;
    if (!accepted.empty()) {
        std::int64_t revision = 0;
        auto snapshot = PrepareCharacterSnapshot(sessionId, subscribeEnvelope.messageId, stateProvider, revisions,
                                                 stateArea, revision, now, snapshotBuilder);
        if (!snapshot.has_value()) {
            return SubscribeResult{
                .subscriptionAck = protocol::BuildErrorEnvelope(subscribeEnvelope.messageId, kSupportedProtocolVersion,
                                                                  sessionId, "internal_error",
                                                                  "Unable to build state snapshot", false),
            };
        }
        snapshots.push_back(std::move(*snapshot));
        CommitCharacterSnapshot(revisions, stateArea, revision);
    }

    return SubscribeResult{.subscriptionAck = std::move(*ackEnvelope), .snapshots = std::move(snapshots)};
}

protocol::Envelope HandleSnapshotRequest(const protocol::Envelope& snapshotRequestEnvelope,
                                          const std::string& sessionId,
                                          const CharacterStateProvider& stateProvider,
                                          RevisionTracker& revisions, std::chrono::system_clock::time_point now,
                                          SnapshotEnvelopeBuilder& snapshotBuilder) {
    auto request = protocol::DecodeSnapshotRequestPayload(snapshotRequestEnvelope.payload);
    if (!request.has_value()) {
        return protocol::BuildErrorEnvelope(snapshotRequestEnvelope.messageId, kSupportedProtocolVersion, sessionId,
                                             "malformed_message", "Malformed snapshot_request payload", false);
    }
    if (request->stateArea != protocol::state_area::kCharacter) {
        return protocol::BuildErrorEnvelope(snapshotRequestEnvelope.messageId, kSupportedProtocolVersion, sessionId,
                                             "unsupported_capability",
                                             "Unknown state area: " + request->stateArea, false);
    }

    std::int64_t revision = 0;
    auto snapshot = PrepareCharacterSnapshot(sessionId, snapshotRequestEnvelope.messageId, stateProvider, revisions,
                                             request->stateArea, revision, now, snapshotBuilder);
    if (!snapshot.has_value()) {
        return protocol::BuildErrorEnvelope(snapshotRequestEnvelope.messageId, kSupportedProtocolVersion, sessionId,
                                             "internal_error", "Unable to build state snapshot", false);
    }
    CommitCharacterSnapshot(revisions, request->stateArea, revision);
    return std::move(*snapshot);
}

}  // namespace dovahlink::application
