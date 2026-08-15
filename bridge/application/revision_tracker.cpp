#include "application/revision_tracker.hpp"

namespace dovahlink::application {

std::int64_t RevisionTracker::NextRevisionLocked(const std::string& stateArea,
                                                  const std::optional<std::string>& fingerprint) const {
    auto it = currentRevision_.find(stateArea);
    if (it == currentRevision_.end()) {
        return 1;
    }
    if (fingerprint.has_value() && it->second.second.has_value() && *fingerprint == *it->second.second) {
        return it->second.first;
    }
    return it->second.first + 1;
}

std::int64_t RevisionTracker::StartSnapshot(const std::string& stateArea,
                                            const std::optional<std::string>& fingerprint) {
    std::lock_guard<std::mutex> lock(mutex_);
    std::int64_t next = NextRevisionLocked(stateArea, fingerprint);
    currentRevision_[stateArea] = {next, fingerprint};
    return next;
}

std::int64_t RevisionTracker::NextSnapshotRevision(const std::string& stateArea,
                                                    const std::optional<std::string>& fingerprint) const {
    std::lock_guard<std::mutex> lock(mutex_);
    return NextRevisionLocked(stateArea, fingerprint);
}

std::optional<std::pair<std::int64_t, std::int64_t>> RevisionTracker::NextEvent(
    const std::string& stateArea) {
    std::lock_guard<std::mutex> lock(mutex_);
    auto it = currentRevision_.find(stateArea);
    if (it == currentRevision_.end()) {
        return std::nullopt;
    }
    std::int64_t baseRevision = it->second.first;
    std::int64_t revision = baseRevision + 1;
    it->second.first = revision;
    return std::make_pair(baseRevision, revision);
}

std::optional<std::int64_t> RevisionTracker::CurrentRevision(const std::string& stateArea) const {
    std::lock_guard<std::mutex> lock(mutex_);
    auto it = currentRevision_.find(stateArea);
    if (it == currentRevision_.end()) {
        return std::nullopt;
    }
    return it->second.first;
}

}  // namespace dovahlink::application
