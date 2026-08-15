#pragma once

#include <cstdint>
#include <mutex>
#include <optional>
#include <string>
#include <unordered_map>
#include <utility>

namespace dovahlink::application {

/// Assigns monotonically increasing revisions within one session and state area.
/// Snapshots establish baselines, events advance by one, and recovery continues the sequence.
/// A snapshot given a fingerprint advances its revision only when that fingerprint differs from the
/// one last committed for that area, per ARCHITECTURE.md's "advances only when authoritative state
/// changes" -- v2 behavior. A snapshot given no fingerprint always advances unconditionally,
/// preserving v1's original, published behavior; v1 has no defined notion of "unchanged state" and
/// this phase must not silently reinterpret its existing messages.
/// Access is synchronized: an instance may be shared across the play-context lifetime rather than
/// owned by one serial connection, so it may be read and advanced from more than one thread.
class RevisionTracker {
public:
    /// Creates an empty revision tracker.
    RevisionTracker() = default;

    /// Starts or advances a snapshot baseline for a state area. With a fingerprint, returns the
    /// existing revision unchanged when it matches the one last committed for this area, and mints a
    /// new revision only when it differs or none exists yet (v2). Without one, always mints a new
    /// revision (v1's unconditional-advance contract).
    /// @param stateArea Canonical state-area identifier.
    /// @param fingerprint Caller-computed representation of the captured state -- typically the
    ///     same serialized data that will be sent on the wire -- compared for equality against the
    ///     fingerprint last committed for this area, or no value to always advance.
    /// @return Revision assigned to this snapshot.
    std::int64_t StartSnapshot(const std::string& stateArea, const std::optional<std::string>& fingerprint);

    /// Calculates the revision a `StartSnapshot` call with this fingerprint would receive, without
    /// changing tracker state.
    /// @param stateArea Canonical state-area identifier.
    /// @param fingerprint Caller-computed representation of the captured state, or no value to
    ///     preview v1's unconditional advance.
    /// @return Revision that a committed snapshot with this fingerprint will receive.
    [[nodiscard]] std::int64_t NextSnapshotRevision(const std::string& stateArea,
                                                     const std::optional<std::string>& fingerprint) const;

    /// Advances a state area for its next event. Unconditional by design -- an event represents a
    /// change the caller has already confirmed, unlike a snapshot pull which may or may not reflect
    /// one. Has no production caller yet (event delivery is Phase 4 scope; see ROADMAP.md); leaves
    /// the area's stored fingerprint untouched, so how it should interact with `StartSnapshot`'s
    /// fingerprint comparison is left for whichever phase wires event delivery to decide.
    /// @param stateArea Canonical state-area identifier.
    /// @return Base and new revision, or no value before a baseline exists.
    std::optional<std::pair<std::int64_t, std::int64_t>> NextEvent(const std::string& stateArea);

    /// Reads the latest revision for a state area.
    /// @param stateArea Canonical state-area identifier.
    /// @return Current revision, or no value when untracked.
    [[nodiscard]] std::optional<std::int64_t> CurrentRevision(const std::string& stateArea) const;

private:
    /// Synchronizes access to `currentRevision_`.
    mutable std::mutex mutex_;

    /// Latest assigned revision and the fingerprint it was assigned for, per state area.
    std::unordered_map<std::string, std::pair<std::int64_t, std::optional<std::string>>> currentRevision_;
};

}  // namespace dovahlink::application
