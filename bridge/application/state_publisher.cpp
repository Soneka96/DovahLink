#include "application/state_publisher.hpp"

#include "application/timestamp.hpp"
#include "protocol/constants.hpp"
#include "protocol/state_event_payload.hpp"
#include "protocol/state_snapshot_payload.hpp"

#include <boost/json/serialize.hpp>

#include <optional>
#include <utility>

namespace dovahlink::application {

StatePublisher::StatePublisher(IRevisionTracker& revisionTracker,
                               IOutboundPublicationSink& sink)
    : revisionTracker_(revisionTracker), sink_(sink) {}

std::shared_ptr<std::mutex>
StatePublisher::PublicationMutexFor(const std::string& stateArea) {
    std::lock_guard<std::mutex> lock(publicationMutexesMutex_);
    auto it = publicationMutexes_.find(stateArea);
    if (it != publicationMutexes_.end()) {
        return it->second;
    }
    return publicationMutexes_.emplace(stateArea, std::make_shared<std::mutex>())
        .first->second;
}

bool StatePublisher::PublishSnapshot(
    const std::string& stateArea, boost::json::object data,
    std::chrono::system_clock::time_point occurredAt) {
    std::string fingerprint = boost::json::serialize(data);
    std::string formattedOccurredAt = FormatTimestamp(occurredAt);
    auto publicationMutex = PublicationMutexFor(stateArea);
    std::lock_guard<std::mutex> publicationLock(*publicationMutex);

    std::optional<protocol::Envelope> envelope =
        revisionTracker_.CommitSnapshotEnvelopeIfBuilt(
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
    auto publicationMutex = PublicationMutexFor(stateArea);
    std::lock_guard<std::mutex> publicationLock(*publicationMutex);

    auto envelope = revisionTracker_.CommitEventEnvelopeIfBuilt(
        stateArea,
        [&, data = std::move(data)](
            std::int64_t baseRevision,
            std::int64_t revision) mutable -> std::optional<protocol::Envelope> {
            return protocol::BuildEnvelope(
                std::string(protocol::message_type::kStateEvent),
                /*sessionId=*/std::nullopt, /*correlationId=*/std::nullopt,
                protocol::EncodeStateEventPayload(protocol::StateEventPayload{
                    .stateArea = stateArea,
                    .baseRevision = baseRevision,
                    .revision = revision,
                    .occurredAt = FormatTimestamp(occurredAt),
                    .data = std::move(data),
                }));
        });
    if (!envelope.has_value()) {
        return false;
    }
    sink_.PublishEvent(stateArea, std::move(*envelope));
    return true;
}

} //  namespace dovahlink::application
