#include "application/revision_tracker.hpp"

namespace dovahlink::application {

namespace {

/// Computes the next snapshot revision from an already-locked revision map, given the captured
/// state's fingerprint. With a fingerprint, returns the existing revision unchanged when it matches
/// what is stored for `stateArea`; otherwise -- including when no fingerprint is supplied, or the
/// area is not yet tracked -- returns the next sequential revision.
/// @param currentRevision Map guarded by the caller's lock.
/// @param stateArea Canonical state-area identifier.
/// @param fingerprint Caller-computed representation of the captured state, or no value to always
///     advance (v1's unconditional-advance contract).
std::int64_t NextSnapshotRevisionLocked(
    const std::unordered_map<std::string, std::pair<std::int64_t, std::optional<std::string>>>& currentRevision,
    const std::string& stateArea, const std::optional<std::string>& fingerprint) {
    auto it = currentRevision.find(stateArea);
    if (it == currentRevision.end()) {
        return 1;
    }
    if (fingerprint.has_value() && it->second.second.has_value() && *fingerprint == *it->second.second) {
        return it->second.first;
    }
    return it->second.first + 1;
}

}  // namespace

std::int64_t RevisionTracker::StartSnapshot(const std::string& stateArea,
                                            const std::optional<std::string>& fingerprint) {
    std::lock_guard<std::mutex> lock(mutex_);
    std::int64_t next = NextSnapshotRevisionLocked(currentRevision_, stateArea, fingerprint);
    currentRevision_[stateArea] = {next, fingerprint};
    return next;
}

std::int64_t RevisionTracker::NextSnapshotRevision(const std::string& stateArea,
                                                    const std::optional<std::string>& fingerprint) const {
    std::lock_guard<std::mutex> lock(mutex_);
    return NextSnapshotRevisionLocked(currentRevision_, stateArea, fingerprint);
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
