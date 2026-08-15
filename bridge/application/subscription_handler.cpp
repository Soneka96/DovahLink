#include "application/subscription_handler.hpp"

#include "application/handshake_handler.hpp"
#include "application/timestamp.hpp"
#include "protocol/messages.hpp"

#include <boost/json/serialize.hpp>

#include <algorithm>
#include <type_traits>
#include <utility>

namespace dovahlink::application {

namespace {

constexpr std::string_view kCharacterCapabilityId = "state.character";
constexpr std::int64_t kCharacterCapabilityVersion = 1;

static_assert(std::is_nothrow_move_constructible_v<protocol::Envelope>);
static_assert(std::is_nothrow_move_constructible_v<SubscribeResult>);

/// Captures character state and atomically assigns, commits, and builds its snapshot envelope, via
/// `RevisionTracker::CommitSnapshotIfBuilt` -- so a concurrent snapshot for the same state area
/// (the tracker may be shared across a play context's connections) cannot observe or commit a
/// revision between this call's assignment and its conditional commit.
/// @param correlationId Message ID that caused the snapshot, when applicable.
/// @param snapshotBuilder Builds the fallible envelope from the revision this call assigns.
std::optional<protocol::Envelope> BuildCommittedCharacterSnapshot(
    const std::string& sessionId, std::optional<std::string> correlationId,
    const CharacterStateProvider& stateProvider, RevisionTracker& revisions, const std::string& stateArea,
    std::chrono::system_clock::time_point now, SnapshotEnvelopeBuilder& snapshotBuilder) {
    CharacterSnapshot snapshot = stateProvider.CurrentCharacterSnapshot();
    std::optional<std::string> fingerprint =
        std::make_optional(boost::json::serialize(BuildCharacterStateData(snapshot)));
    return revisions.CommitSnapshotIfBuilt(stateArea, fingerprint, [&](std::int64_t revision) {
        return snapshotBuilder(sessionId, std::move(correlationId), snapshot, revision, now);
    });
}

}

std::optional<protocol::Envelope> BuildCharacterSnapshotEnvelope(
    const std::string& sessionId, std::optional<std::string> correlationId, const CharacterSnapshot& snapshot,
    std::int64_t revision, std::chrono::system_clock::time_point now) {
    protocol::StateSnapshotPayload payload =
        BuildCharacterSnapshotPayload(snapshot, revision, FormatTimestamp(now));
    return protocol::BuildEnvelope(std::string(protocol::message_type::kStateSnapshot), sessionId,
                                   std::move(correlationId), protocol::EncodeStateSnapshotPayload(payload));
}

std::optional<protocol::Envelope> BuildBridgeCapabilities(const std::string& sessionId) {
    protocol::CapabilitiesPayload payload{
        .capabilities = {protocol::Capability{
            .id = std::string(kCharacterCapabilityId),
            .version = kCharacterCapabilityVersion,
        }},
    };
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
    for (const protocol::Capability& capability : capabilities->capabilities) {
        if (capability.id != kCharacterCapabilityId ||
            capability.version != kCharacterCapabilityVersion) {
            return protocol::BuildErrorEnvelope(capabilitiesEnvelope.messageId, sessionId,
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
            .subscriptionAck = protocol::BuildErrorEnvelope(subscribeEnvelope.messageId, sessionId,
                                                              "malformed_message", "Malformed subscribe payload",
                                                              false),
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

    // The current contract has exactly one registered state area, so
    // `accepted` can only ever be empty or contain "character" -- a loop
    // building "one snapshot per accepted area" would have nothing to
    // iterate over beyond this single case.
    const std::string stateArea(protocol::state_area::kCharacter);
    auto ackEnvelope = protocol::BuildEnvelope(
        std::string(protocol::message_type::kSubscriptionAck), sessionId, subscribeEnvelope.messageId,
        protocol::EncodeSubscriptionAckPayload(protocol::SubscriptionAckPayload{
            .acceptedStateAreas = accepted,
            .rejectedStateAreas = rejected,
        }));
    if (!ackEnvelope.has_value()) {
        return SubscribeResult{
            .subscriptionAck = protocol::BuildErrorEnvelope(subscribeEnvelope.messageId, sessionId,
                                                              "internal_error", "Unable to build response", false),
        };
    }

    std::vector<protocol::Envelope> snapshots;
    if (!accepted.empty()) {
        auto snapshot = BuildCommittedCharacterSnapshot(sessionId, subscribeEnvelope.messageId, stateProvider,
                                                         revisions, stateArea, now, snapshotBuilder);
        if (!snapshot.has_value()) {
            return SubscribeResult{
                .subscriptionAck = protocol::BuildErrorEnvelope(subscribeEnvelope.messageId, sessionId,
                                                                  "internal_error",
                                                                  "Unable to build state snapshot", false),
            };
        }
        snapshots.push_back(std::move(*snapshot));
    }

    return SubscribeResult{
        .subscriptionAck = std::move(*ackEnvelope),
        .snapshots = std::move(snapshots),
        .acceptedStateAreas = std::move(accepted),
    };
}

protocol::Envelope HandleSnapshotRequest(const protocol::Envelope& snapshotRequestEnvelope,
                                          const std::string& sessionId, const CharacterStateProvider& stateProvider,
                                          RevisionTracker& revisions, std::chrono::system_clock::time_point now,
                                          SnapshotEnvelopeBuilder& snapshotBuilder) {
    auto request = protocol::DecodeSnapshotRequestPayload(snapshotRequestEnvelope.payload);
    if (!request.has_value()) {
        return protocol::BuildErrorEnvelope(snapshotRequestEnvelope.messageId, sessionId, "malformed_message",
                                             "Malformed snapshot_request payload", false);
    }
    if (request->stateArea != protocol::state_area::kCharacter) {
        return protocol::BuildErrorEnvelope(snapshotRequestEnvelope.messageId, sessionId, "unsupported_capability",
                                             "Unknown state area: " + request->stateArea, false);
    }

    auto snapshot = BuildCommittedCharacterSnapshot(sessionId, snapshotRequestEnvelope.messageId, stateProvider,
                                                     revisions, request->stateArea, now, snapshotBuilder);
    if (!snapshot.has_value()) {
        return protocol::BuildErrorEnvelope(snapshotRequestEnvelope.messageId, sessionId, "internal_error",
                                             "Unable to build state snapshot", false);
    }
    return std::move(*snapshot);
}

}  // namespace dovahlink::application
