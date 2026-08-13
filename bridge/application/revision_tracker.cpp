#include "application/revision_tracker.hpp"

namespace dovahlink::application {

/**
 * @brief Starts a snapshot for a state area and advances its revision.
 *
 * @param stateArea State area whose revision should be initialized or advanced.
 * @return The new revision number.
 */
std::int64_t RevisionTracker::StartSnapshot(const std::string& stateArea) {
    auto it = currentRevision_.find(stateArea);
    std::int64_t next = (it == currentRevision_.end()) ? 1 : it->second + 1;
    currentRevision_[stateArea] = next;
    return next;
}

/**
 * @brief Advances the revision for a tracked state area.
 *
 * @param stateArea State area whose revision should be advanced.
 * @return A pair containing the previous revision and the new revision, or
 *         std::nullopt if the state area has no tracked revision.
 */
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

/**
 * @brief Retrieves the current revision for a state area.
 *
 * @param stateArea State area whose revision is requested.
 * @return The current revision, or `std::nullopt` if the state area is untracked.
 */
std::optional<std::int64_t> RevisionTracker::CurrentRevision(const std::string& stateArea) const {
    auto it = currentRevision_.find(stateArea);
    if (it == currentRevision_.end()) {
        return std::nullopt;
    }
    return it->second;
}

}  // namespace dovahlink::application
