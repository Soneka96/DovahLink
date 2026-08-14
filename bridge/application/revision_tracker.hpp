#pragma once

#include <cstdint>
#include <optional>
#include <string>
#include <unordered_map>
#include <utility>

namespace dovahlink::application {

/// Assigns monotonically increasing revisions within one session and state area.
/// Snapshots establish baselines, events advance by one, and recovery continues the sequence.
/// One instance belongs to one serial connection and is not thread-safe.
class RevisionTracker {
public:
    /// Creates an empty revision tracker.
    RevisionTracker() = default;

    /// Starts or advances a snapshot baseline for a state area.
    /// @param stateArea Canonical state-area identifier.
    /// @return New revision assigned to the snapshot.
    std::int64_t StartSnapshot(const std::string& stateArea);

    /// Calculates the next snapshot revision without changing tracker state.
    /// @param stateArea Canonical state-area identifier.
    /// @return Revision that the next committed snapshot will receive.
    [[nodiscard]] std::int64_t NextSnapshotRevision(const std::string& stateArea) const;

    /// Advances a state area for its next event.
    /// @param stateArea Canonical state-area identifier.
    /// @return Base and new revision, or no value before a baseline exists.
    std::optional<std::pair<std::int64_t, std::int64_t>> NextEvent(const std::string& stateArea);

    /// Reads the latest revision for a state area.
    /// @param stateArea Canonical state-area identifier.
    /// @return Current revision, or no value when untracked.
    [[nodiscard]] std::optional<std::int64_t> CurrentRevision(const std::string& stateArea) const;

private:
    /// Latest assigned revision for each state area.
    std::unordered_map<std::string, std::int64_t> currentRevision_;
};

}  // namespace dovahlink::application
