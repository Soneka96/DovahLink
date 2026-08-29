#include "application/state_publisher.hpp"

#include "application/timestamp.hpp"
#include "protocol/constants.hpp"
#include "protocol/state_event_payload.hpp"
#include "protocol/state_snapshot_payload.hpp"

#include <boost/json/serialize.hpp>

#include <optional>
#include <utility>

namespace dovahlink::application {

StatePublisher::StatePublisher(IOutboundPublicationSink& sink) : sink_(sink) {}

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

std::optional<protocol::Envelope> StatePublisher::BuildSnapshotEnvelopeLocked(
    const std::string& stateArea, IRevisionTracker& revisionTracker,
    boost::json::object data,
    std::chrono::system_clock::time_point occurredAt) {
    std::string fingerprint = boost::json::serialize(data);
    std::string formattedOccurredAt = FormatTimestamp(occurredAt);
    return revisionTracker.CommitSnapshotEnvelopeIfBuilt(
        stateArea, fingerprint,
        [&, data = std::move(data)](
            std::int64_t revision) mutable -> std::optional<protocol::Envelope> {
            return protocol::BuildEnvelope(
                std::string(protocol::message_type::kStateSnapshot),
                /*sessionId=*/std::nullopt,
                /*correlationId=*/std::nullopt,
                protocol::EncodeStateSnapshotPayload(protocol::StateSnapshotPayload{
                    .stateArea = stateArea,
                    .revision = revision,
                    .occurredAt = formattedOccurredAt,
                    .data = std::move(data),
                }));
        });
}

std::optional<protocol::Envelope> StatePublisher::BuildEventEnvelopeLocked(
    const std::string& stateArea, IRevisionTracker& revisionTracker,
    boost::json::object data,
    std::chrono::system_clock::time_point occurredAt) {
    return revisionTracker.CommitEventEnvelopeIfBuilt(
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
}

bool StatePublisher::PublishSnapshot(
    const std::string& stateArea, const std::string& playContextId,
    IRevisionTracker& revisionTracker, boost::json::object data,
    std::chrono::system_clock::time_point occurredAt,
    const std::function<bool()>& stillCurrent) {
    auto publicationMutex = PublicationMutexFor(stateArea);
    std::lock_guard<std::mutex> publicationLock(*publicationMutex);

    auto envelope = BuildSnapshotEnvelopeLocked(stateArea, revisionTracker,
                                                std::move(data), occurredAt);
    if (!envelope.has_value()) {
        return false;
    }
    envelope->playContextId = playContextId;
    if (!stillCurrent()) {
        return false;
    }
    sink_.PublishSnapshot(stateArea, std::move(*envelope));
    return true;
}

bool StatePublisher::PublishEvent(
    const std::string& stateArea, const std::string& playContextId,
    IRevisionTracker& revisionTracker, boost::json::object data,
    std::chrono::system_clock::time_point occurredAt,
    const std::function<bool()>& stillCurrent) {
    auto publicationMutex = PublicationMutexFor(stateArea);
    std::lock_guard<std::mutex> publicationLock(*publicationMutex);

    auto envelope = BuildEventEnvelopeLocked(stateArea, revisionTracker,
                                             std::move(data), occurredAt);
    if (!envelope.has_value()) {
        return false;
    }
    envelope->playContextId = playContextId;
    if (!stillCurrent()) {
        return false;
    }
    sink_.PublishEvent(stateArea, std::move(*envelope));
    return true;
}

bool StatePublisher::PublishCapture(
    const std::string& stateArea, const std::string& playContextId,
    IRevisionTracker& revisionTracker, CaptureMode mode,
    boost::json::object data, std::chrono::system_clock::time_point occurredAt,
    const std::function<bool()>& stillCurrent) {
    auto publicationMutex = PublicationMutexFor(stateArea);
    std::lock_guard<std::mutex> publicationLock(*publicationMutex);

    bool hasBaseline = revisionTracker.CurrentRevision(stateArea).has_value();
    bool asSnapshot = mode == CaptureMode::kSnapshot || !hasBaseline;

    auto envelope = asSnapshot
                        ? BuildSnapshotEnvelopeLocked(stateArea, revisionTracker,
                                                      std::move(data), occurredAt)
                        : BuildEventEnvelopeLocked(stateArea, revisionTracker, std::move(data),
                                                   occurredAt);
    if (!envelope.has_value()) {
        return false;
    }
    envelope->playContextId = playContextId;
    if (!stillCurrent()) {
        return false;
    }
    if (asSnapshot) {
        sink_.PublishSnapshot(stateArea, std::move(*envelope));
    } else {
        sink_.PublishEvent(stateArea, std::move(*envelope));
    }
    return true;
}

} //  namespace dovahlink::application
