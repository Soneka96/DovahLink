#pragma once

#include "application/character_state.hpp"
#include "application/revision_tracker.hpp"
#include "protocol/envelope.hpp"

#include <chrono>
#include <cstdint>
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

/// Builds a character snapshot envelope from already-captured state and a provisional revision.
/// @param sessionId Authenticated session identifier.
/// @param protocolVersion Connection's negotiated protocol version.
/// @param correlationId Message ID that caused the snapshot, when applicable.
/// @param snapshot Captured application-owned character state.
/// @param revision Provisional revision to encode without committing it.
/// @param now Timestamp assigned to the snapshot.
/// @return Completed envelope, or no value if secure message-ID generation fails.
/// Formatting, encoding, and allocation exceptions propagate to the caller.
[[nodiscard]] std::optional<protocol::Envelope> BuildCharacterSnapshotEnvelope(
    const std::string& sessionId, std::int64_t protocolVersion, std::optional<std::string> correlationId,
    const CharacterSnapshot& snapshot, std::int64_t revision,
    std::chrono::system_clock::time_point now);

/// Function used to prepare a snapshot envelope before its revision is committed.
using SnapshotEnvelopeBuilder = decltype(BuildCharacterSnapshotEnvelope);

/// Builds the bridge capabilities envelope for an authenticated session.
/// @param sessionId Server-issued session identifier.
/// @param protocolVersion Connection's negotiated protocol version.
/// @return Capabilities envelope, or no value if an identifier cannot be generated.
[[nodiscard]] std::optional<protocol::Envelope> BuildBridgeCapabilities(const std::string& sessionId,
                                                                         std::int64_t protocolVersion);

/// Validates a client capabilities envelope.
/// @param capabilitiesEnvelope Decoded client capabilities message.
/// @param sessionId Authenticated session identifier.
/// @param protocolVersion Connection's negotiated protocol version.
/// @return Error envelope when validation fails; no value when accepted.
[[nodiscard]] std::optional<protocol::Envelope> HandleClientCapabilities(
    const protocol::Envelope& capabilitiesEnvelope, const std::string& sessionId, std::int64_t protocolVersion);

/// Contains a subscription acknowledgement and its initial snapshots.
struct SubscribeResult {
    /// Acknowledgement for the subscription request.
    protocol::Envelope subscriptionAck;

    /// Snapshots for accepted state areas, in request order.
    std::vector<protocol::Envelope> snapshots;

    /// State areas accepted by this request, exposed structurally (beyond
    /// `subscriptionAck`'s encoded payload) for the dispatcher's own
    /// per-connection subscription bookkeeping. Empty when the request
    /// itself failed before areas could be evaluated.
    std::vector<std::string> acceptedStateAreas;
};

/// Handles a subscription request and builds initial snapshots before any events.
/// Acknowledgements and initial snapshots correlate to the subscription message ID.
/// Revision state remains unchanged if any response cannot be prepared or snapshot building throws.
/// @param subscribeEnvelope Decoded client subscription request.
/// @param sessionId Authenticated session identifier.
/// @param protocolVersion Connection's negotiated protocol version.
/// @param stateProvider Source of current character state.
/// @param revisions Session revision tracker.
/// @param now Timestamp assigned to generated snapshots.
/// @param snapshotBuilder Builds a complete snapshot before revision state changes.
/// @return Subscription acknowledgement and any accepted-area snapshots.
[[nodiscard]] SubscribeResult HandleSubscribe(const protocol::Envelope& subscribeEnvelope,
                                               const std::string& sessionId, std::int64_t protocolVersion,
                                               const CharacterStateProvider& stateProvider,
                                               RevisionTracker& revisions,
                                               std::chrono::system_clock::time_point now,
                                               SnapshotEnvelopeBuilder& snapshotBuilder =
                                                   BuildCharacterSnapshotEnvelope);

/// Handles a request for a fresh state snapshot correlated to the request message ID.
/// Revision state remains unchanged if the snapshot cannot be prepared or snapshot building throws.
/// @param snapshotRequestEnvelope Decoded client snapshot request.
/// @param sessionId Authenticated session identifier.
/// @param protocolVersion Connection's negotiated protocol version.
/// @param stateProvider Source of current character state.
/// @param revisions Session revision tracker.
/// @param now Timestamp assigned to the generated snapshot.
/// @param snapshotBuilder Builds a complete snapshot before revision state changes.
/// @return Snapshot envelope or a protocol error envelope.
[[nodiscard]] protocol::Envelope HandleSnapshotRequest(const protocol::Envelope& snapshotRequestEnvelope,
                                                        const std::string& sessionId, std::int64_t protocolVersion,
                                                        const CharacterStateProvider& stateProvider,
                                                        RevisionTracker& revisions,
                                                        std::chrono::system_clock::time_point now,
                                                        SnapshotEnvelopeBuilder& snapshotBuilder =
                                                            BuildCharacterSnapshotEnvelope);

}  // namespace dovahlink::application
