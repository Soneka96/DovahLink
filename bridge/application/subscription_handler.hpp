#pragma once

#include "application/character_state.hpp"
#include "application/revision_tracker.hpp"
#include "protocol/envelope.hpp"

#include <chrono>
#include <optional>
#include <string>
#include <vector>

namespace dovahlink::application {

/// Supplies current character state to subscription and snapshot handlers.
class CharacterStateProvider {
public:
    /// Releases the interface without performing work.
    virtual ~CharacterStateProvider() = default;

    /// Captures the current application-owned character state.
    /// @return Current character snapshot, including unavailable values.
    [[nodiscard]] virtual CharacterSnapshot CurrentCharacterSnapshot() const = 0;
};

/// Builds the bridge capabilities envelope for an authenticated session.
/// @param sessionId Server-issued session identifier.
/// @return Capabilities envelope, or no value if an identifier cannot be generated.
[[nodiscard]] std::optional<protocol::Envelope> BuildBridgeCapabilities(const std::string& sessionId);

/// Validates a client capabilities envelope.
/// @param capabilitiesEnvelope Decoded client capabilities message.
/// @param sessionId Authenticated session identifier.
/// @return Error envelope when validation fails; no value when accepted.
[[nodiscard]] std::optional<protocol::Envelope> HandleClientCapabilities(
    const protocol::Envelope& capabilitiesEnvelope, const std::string& sessionId);

/// Contains a subscription acknowledgement and its initial snapshots.
struct SubscribeResult {
    /// Acknowledgement for the subscription request.
    protocol::Envelope subscriptionAck;

    /// Snapshots for accepted state areas, in request order.
    std::vector<protocol::Envelope> snapshots;
};

/// Handles a subscription request and builds initial snapshots before any events.
/// Acknowledgements and initial snapshots correlate to the subscription message ID.
/// @param subscribeEnvelope Decoded client subscription request.
/// @param sessionId Authenticated session identifier.
/// @param stateProvider Source of current character state.
/// @param revisions Session revision tracker.
/// @param now Timestamp assigned to generated snapshots.
/// @return Subscription acknowledgement and any accepted-area snapshots.
[[nodiscard]] SubscribeResult HandleSubscribe(const protocol::Envelope& subscribeEnvelope,
                                               const std::string& sessionId,
                                               const CharacterStateProvider& stateProvider,
                                               RevisionTracker& revisions,
                                               std::chrono::system_clock::time_point now);

/// Handles a request for a fresh state snapshot correlated to the request message ID.
/// @param snapshotRequestEnvelope Decoded client snapshot request.
/// @param sessionId Authenticated session identifier.
/// @param stateProvider Source of current character state.
/// @param revisions Session revision tracker.
/// @param now Timestamp assigned to the generated snapshot.
/// @return Snapshot envelope or a protocol error envelope.
[[nodiscard]] protocol::Envelope HandleSnapshotRequest(const protocol::Envelope& snapshotRequestEnvelope,
                                                        const std::string& sessionId,
                                                        const CharacterStateProvider& stateProvider,
                                                        RevisionTracker& revisions,
                                                        std::chrono::system_clock::time_point now);

}  // namespace dovahlink::application
