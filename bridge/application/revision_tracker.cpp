#include "application/revision_tracker.hpp"

namespace dovahlink::application {

namespace {

/// Computes the next snapshot revision from an already-locked revision map.
/// @param currentRevision Map guarded by the caller's lock.
/// @param stateArea Canonical state-area identifier.
std::int64_t NextSnapshotRevisionLocked(const std::unordered_map<std::string, std::int64_t>& currentRevision,
                                        const std::string& stateArea) {
    auto it = currentRevision.find(stateArea);
    return (it == currentRevision.end()) ? 1 : it->second + 1;
}

}  // namespace

std::int64_t RevisionTracker::StartSnapshot(const std::string& stateArea) {
    std::lock_guard<std::mutex> lock(mutex_);
    std::int64_t next = NextSnapshotRevisionLocked(currentRevision_, stateArea);
    currentRevision_[stateArea] = next;
    return next;
}

std::int64_t RevisionTracker::NextSnapshotRevision(const std::string& stateArea) const {
    std::lock_guard<std::mutex> lock(mutex_);
    return NextSnapshotRevisionLocked(currentRevision_, stateArea);
}

std::optional<std::pair<std::int64_t, std::int64_t>> RevisionTracker::NextEvent(
    const std::string& stateArea) {
    std::lock_guard<std::mutex> lock(mutex_);
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
    std::lock_guard<std::mutex> lock(mutex_);
    auto it = currentRevision_.find(stateArea);
    if (it == currentRevision_.end()) {
        return std::nullopt;
    }
    return it->second;
}

}  // namespace dovahlink::application
