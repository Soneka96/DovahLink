#include "application/revision_tracker.hpp"

namespace dovahlink::application {

std::int64_t RevisionTracker::StartSnapshot(const std::string& stateArea) {
    auto it = currentRevision_.find(stateArea);
    std::int64_t next = (it == currentRevision_.end()) ? 1 : it->second + 1;
    currentRevision_[stateArea] = next;
    return next;
}

std::optional<std::pair<std::int64_t, std::int64_t>> RevisionTracker::NextEvent(
    const std::string& stateArea) {
    auto it = currentRevision_.find(stateArea);
    if (it == currentRevision_.end()) {
        return std::nullopt;
    }
    std::int64_t baseRevision = it->second;
    std::int64_t revision = baseRevision + 1;
    it->second = revision;
    return std::make_pair(baseRevision, revision);
}

std::optional<std::int64_t> RevisionTracker::CurrentRevision(const std::string& stateArea) const {
    auto it = currentRevision_.find(stateArea);
    if (it == currentRevision_.end()) {
        return std::nullopt;
    }
    return it->second;
}

}  // namespace dovahlink::application
