#pragma once

#include "application/character_state.hpp"
#include "application/revision_tracker.hpp"
#include "protocol/envelope.hpp"

#include <chrono>
#include <optional>
#include <string>
#include <vector>

namespace dovahlink::application {

// Supplies the current character state snapshot on demand, for building
// state_snapshot responses (the post-subscribe snapshot and snapshot_request
// recovery). The concrete implementation is kept current by the
// level-increase event publisher (a later step); this seam lets subscribe/
// snapshot handling be tested without depending on that wiring, matching
// game_state::LevelAccessor's role for game_state::CaptureLevel.
class CharacterStateProvider {
public:
    virtual ~CharacterStateProvider() = default;
    [[nodiscard]] virtual CharacterSnapshot CurrentCharacterSnapshot() const = 0;
};

// Builds the bridge's own outbound `capabilities` message, advertising the
// one v1 registered capability (protocol/schema/README.md: state.character
// version 1). Sent once, proactively, after hello_ack -- the caller decides
// when. Returns nullopt only on an unreachable-in-practice CSPRNG failure
// (security::GenerateOpaqueId); the caller should treat that as fatal, the
// same as any other point this codebase cannot produce a compliant message.
[[nodiscard]] std::optional<protocol::Envelope> BuildBridgeCapabilities(const std::string& sessionId);

// Validates the client's `capabilities` message. protocol/schema/README.md:
// "unregistered capability IDs are rejected." Capabilities are not
// individually acknowledged (unlike subscribe): nullopt means "accepted,
// nothing to send back." A non-nullopt return is always an error envelope
// (malformed_message or unsupported_capability) that always succeeds to
// build (see messages.hpp's BuildErrorEnvelope); the caller is expected to
// close the connection on either.
[[nodiscard]] std::optional<protocol::Envelope> HandleClientCapabilities(
    const protocol::Envelope& capabilitiesEnvelope, const std::string& sessionId);

// Result of handling one `subscribe` message: the subscription_ack, plus
// one state_snapshot envelope per accepted state area, in the same order as
// the accepted areas (protocol/schema/README.md: "the bridge sends a
// snapshot before events for each accepted state area"). `snapshots` is
// empty whenever `subscriptionAck` turned out to be an error envelope
// instead of a genuine ack (malformed payload, or an internal_error if a
// response could not be built) -- the caller is expected to close the
// connection in that case.
struct SubscribeResult {
    protocol::Envelope subscriptionAck;
    std::vector<protocol::Envelope> snapshots;
};

// Processes one `subscribe` message. v1 has exactly one registered state
// area, "character" (protocol/schema/README.md); any other requested area
// is reported in subscriptionAck's rejectedStateAreas, not treated as a
// protocol violation -- the schema names a field for exactly this outcome,
// unlike an unregistered capability ID. Each accepted area's snapshot is
// correlated to `subscribeEnvelope`'s own messageId, per the schema's
// "each initial snapshot also uses the subscribe message ID as its
// correlationId."
[[nodiscard]] SubscribeResult HandleSubscribe(const protocol::Envelope& subscribeEnvelope,
                                               const std::string& sessionId,
                                               const CharacterStateProvider& stateProvider,
                                               RevisionTracker& revisions,
                                               std::chrono::system_clock::time_point now);

// Processes one `snapshot_request` message: a fresh state_snapshot for the
// requested area at its current revision, or a mapped error if the area is
// not the one registered v1 state area or the payload is malformed. Unlike
// HandleSubscribe, there is no "partial accept" field for snapshot_request
// to report an unknown area through, so an unknown area maps to the
// unsupported_capability error code -- the closest fit among the canonical
// codes, since state areas are gated by the same capability registration as
// the `capabilities` exchange. This implementation does not require the
// area to have been subscribed to first; the schema does not state that
// requirement.
[[nodiscard]] protocol::Envelope HandleSnapshotRequest(const protocol::Envelope& snapshotRequestEnvelope,
                                                        const std::string& sessionId,
                                                        const CharacterStateProvider& stateProvider,
                                                        RevisionTracker& revisions,
                                                        std::chrono::system_clock::time_point now);

}  // namespace dovahlink::application
