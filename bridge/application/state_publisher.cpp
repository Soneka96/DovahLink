#include "application/state_publisher.hpp"

#include "application/timestamp.hpp"
#include "protocol/constants.hpp"
#include "protocol/state_event_payload.hpp"
#include "protocol/state_snapshot_payload.hpp"

#include <boost/json/serialize.hpp>

#include <optional>
#include <utility>

namespace dovahlink::application {

StatePublisher::StatePublisher(RevisionTracker& revisionTracker,
                               IOutboundPublicationSink& sink)
    : revisionTracker_(revisionTracker), sink_(sink) {}

bool StatePublisher::PublishSnapshot(
    const std::string& stateArea, boost::json::object data,
    std::chrono::system_clock::time_point occurredAt) {
    std::string fingerprint = boost::json::serialize(data);
    std::string formattedOccurredAt = FormatTimestamp(occurredAt);

    std::optional<protocol::Envelope> envelope =
        revisionTracker_.CommitSnapshotIfBuilt(
            stateArea, fingerprint,
            [&, data = std::move(data)](
                std::int64_t revision) mutable -> std::optional<protocol::Envelope> {
                return protocol::BuildEnvelope(
                    std::string(protocol::message_type::kStateSnapshot),
                    /*sessionId=*/std::nullopt,
                    /*correlationId=*/std::nullopt,
                    protocol::EncodeStateSnapshotPayload(
                        protocol::StateSnapshotPayload{
                            .stateArea = stateArea,
                            .revision = revision,
                            .occurredAt = formattedOccurredAt,
                            .data = std::move(data),
                        }));
            });
    if (!envelope.has_value()) {
        return false;
    }
    sink_.PublishSnapshot(stateArea, std::move(*envelope));
    return true;
}

bool StatePublisher::PublishEvent(
    const std::string& stateArea, boost::json::object data,
    std::chrono::system_clock::time_point occurredAt) {
    auto revisions = revisionTracker_.NextEvent(stateArea);
    if (!revisions.has_value()) {
        return false;
    }
    auto [baseRevision, revision] = *revisions;

    auto envelope = protocol::BuildEnvelope(
        std::string(protocol::message_type::kStateEvent),
        /*sessionId=*/std::nullopt, /*correlationId=*/std::nullopt,
        protocol::EncodeStateEventPayload(protocol::StateEventPayload{
            .stateArea = stateArea,
            .baseRevision = baseRevision,
            .revision = revision,
            .occurredAt = FormatTimestamp(occurredAt),
            .data = std::move(data),
        }));
    if (!envelope.has_value()) {
        return false;
    }
    sink_.PublishEvent(stateArea, std::move(*envelope));
    return true;
}

} //  namespace dovahlink::application
