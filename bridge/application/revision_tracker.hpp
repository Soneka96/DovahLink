#pragma once

#include "protocol/envelope.hpp"

#include <cstdint>
#include <functional>
#include <mutex>
#include <optional>
#include <string>
#include <unordered_map>
#include <utility>

namespace dovahlink::application {

///  Assigns monotonically increasing revisions within one play context and state
///  area. Snapshots establish baselines, events advance by one, and recovery
///  continues the sequence. A snapshot given a fingerprint advances its revision
///  only when that fingerprint differs from the one last committed for that
///  area, per ARCHITECTURE.md's "advances only when authoritative state changes"
///  -- v2 behavior. A snapshot given no fingerprint always advances
///  unconditionally, preserving v1's original, published behavior; v1 has no
///  defined notion of "unchanged state" and this phase must not silently
///  reinterpret its existing messages. Access is synchronized: an instance may
///  be shared across the play-context lifetime rather than owned by one serial
///  connection, so it may be read and advanced from more than one thread.
///
///  `CommitSnapshotIfBuilt` and `CommitEventIfBuilt` are declared only on the
///  concrete `RevisionTracker` below, not on this interface: they are template
///  methods, and C++ cannot express a template as `virtual`. `CommitSnapshotEnvelopeIfBuilt`
///  and `CommitEventEnvelopeIfBuilt` below give an interface-level consumer the
///  same atomic-commit guarantee without depending on the concrete type, by
///  fixing the builder's return type to `protocol::Envelope` -- the one
///  concrete shape every current and anticipated caller actually builds --
///  instead of leaving it generic.
class IRevisionTracker {
  public:
    ///  Releases the interface without performing work.
    virtual ~IRevisionTracker() = default;

    ///  Starts or advances a snapshot baseline for a state area. With a
    ///  fingerprint, returns the existing revision unchanged when it matches the
    ///  one last committed for this area, and mints a new revision only when it
    ///  differs or none exists yet (v2). Without one, always mints a new revision
    ///  (v1's unconditional-advance contract).
    ///  @param stateArea Canonical state-area identifier.
    ///  @param fingerprint Caller-computed representation of the captured state --
    ///  typically the
    ///      same serialized data that will be sent on the wire -- compared for
    ///      equality against the fingerprint last committed for this area, or no
    ///      value to always advance.
    ///  @return Revision assigned to this snapshot.
    virtual std::int64_t
    StartSnapshot(const std::string& stateArea,
                  const std::optional<std::string>& fingerprint) = 0;

    ///  Calculates the revision a `StartSnapshot` call with this fingerprint would
    ///  receive, without changing tracker state.
    ///  @param stateArea Canonical state-area identifier.
    ///  @param fingerprint Caller-computed representation of the captured state,
    ///  or no value to
    ///      preview v1's unconditional advance.
    ///  @return Revision that a committed snapshot with this fingerprint will
    ///  receive.
    [[nodiscard]] virtual std::int64_t
    NextSnapshotRevision(const std::string& stateArea,
                         const std::optional<std::string>& fingerprint) const = 0;

    ///  Advances a state area for its next event. Unconditional by design -- an
    ///  event represents a change the caller has already confirmed, unlike a
    ///  snapshot pull which may or may not reflect one. Has no production caller
    ///  yet; leaves the area's stored fingerprint untouched, so a future event
    ///  delivery caller must define its interaction with `StartSnapshot`'s
    ///  fingerprint comparison.
    ///  @param stateArea Canonical state-area identifier.
    ///  @return Base and new revision, or no value before a baseline exists.
    virtual std::optional<std::pair<std::int64_t, std::int64_t>>
    NextEvent(const std::string& stateArea) = 0;

    ///  Reads the latest revision for a state area.
    ///  @param stateArea Canonical state-area identifier.
    ///  @return Current revision, or no value when untracked.
    [[nodiscard]] virtual std::optional<std::int64_t>
    CurrentRevision(const std::string& stateArea) const = 0;

    ///  Interface-level equivalent of `RevisionTracker::CommitSnapshotIfBuilt`,
    ///  with the builder's return type fixed to `protocol::Envelope` so this
    ///  method can be virtual. See that method's own documentation for the
    ///  atomicity and locking contract, which this preserves exactly.
    ///  @param stateArea Canonical state-area identifier.
    ///  @param fingerprint Caller-computed representation of the captured
    ///  state, or no value to always advance.
    ///  @param buildSnapshot Builds this call's envelope from the revision it
    ///  assigns. No value leaves the tracker's revision for this area
    ///  unchanged.
    ///  @return Whatever `buildSnapshot` returned.
    [[nodiscard]] virtual std::optional<protocol::Envelope>
    CommitSnapshotEnvelopeIfBuilt(
        const std::string& stateArea,
        const std::optional<std::string>& fingerprint,
        std::function<std::optional<protocol::Envelope>(std::int64_t)>
            buildSnapshot) = 0;

    ///  Interface-level equivalent of `RevisionTracker::CommitEventIfBuilt`,
    ///  with the builder's return type fixed to `protocol::Envelope` so this
    ///  method can be virtual. See that method's own documentation for the
    ///  atomicity and locking contract, which this preserves exactly.
    ///  @param stateArea Canonical state-area identifier.
    ///  @param buildEvent Builds this call's envelope from its base and
    ///  assigned revisions. No value leaves the tracker's revision for this
    ///  area unchanged.
    ///  @return Whatever `buildEvent` returned, or no value when no baseline
    ///  exists.
    [[nodiscard]] virtual std::optional<protocol::Envelope>
    CommitEventEnvelopeIfBuilt(
        const std::string& stateArea,
        std::function<std::optional<protocol::Envelope>(std::int64_t,
                                                        std::int64_t)>
            buildEvent) = 0;
};

///  @copydoc IRevisionTracker
class RevisionTracker final : public IRevisionTracker {
  public:
    ///  Creates an empty revision tracker.
    RevisionTracker() = default;

    ///  @copydoc IRevisionTracker::StartSnapshot
    std::int64_t
    StartSnapshot(const std::string& stateArea,
                  const std::optional<std::string>& fingerprint) override;

    ///  @copydoc IRevisionTracker::NextSnapshotRevision
    [[nodiscard]] std::int64_t
    NextSnapshotRevision(const std::string& stateArea,
                         const std::optional<std::string>& fingerprint) const override;

    ///  Computes the next snapshot revision for a state area and commits it,
    ///  atomically, only if `buildSnapshot` succeeds -- both under the same lock,
    ///  so no concurrently running snapshot for the same state area can observe or
    ///  commit a revision between this call's computation and its conditional
    ///  commit. A separate `NextSnapshotRevision` preview followed later by
    ///  `StartSnapshot` does not close that race: another caller's `StartSnapshot`
    ///  can commit in between, making the preview stale by the time the later call
    ///  runs.
    ///  @param stateArea Canonical state-area identifier.
    ///  @param fingerprint Caller-computed representation of the captured state,
    ///  or no value to always
    ///      advance.
    ///  @param buildSnapshot Builds this call's result from the revision it
    ///  assigns. A result with no
    ///      value leaves the tracker's revision for this area unchanged, matching
    ///      `StartSnapshot`'s all-or-nothing semantics for a caller that must not
    ///      commit on build failure.
    ///  @return Whatever `buildSnapshot` returned.
    template <typename BuildFn>
    auto CommitSnapshotIfBuilt(const std::string& stateArea,
                               const std::optional<std::string>& fingerprint,
                               BuildFn&& buildSnapshot) {
        std::lock_guard<std::mutex> lock(mutex_);
        std::int64_t next = NextRevisionLocked(stateArea, fingerprint);
        auto result = buildSnapshot(next);
        if (result.has_value()) {
            currentRevision_[stateArea] = {next, fingerprint};
        }
        return result;
    }

    ///  Computes the next event revision pair and commits the new revision,
    ///  atomically, only if `buildEvent` succeeds -- both under the same lock,
    ///  so a failed event build cannot consume a revision or allow another
    ///  event for the same state area to commit between calculation and build.
    ///  A state area without a snapshot baseline returns an empty result without
    ///  invoking `buildEvent`.
    ///  @param stateArea Canonical state-area identifier.
    ///  @param buildEvent Builds this call's result from its base and assigned
    ///  revisions. A result with no value leaves the tracker's revision for this
    ///  area unchanged.
    ///  @return Whatever `buildEvent` returned, or an empty result when no
    ///  baseline exists.
    template <typename BuildFn>
    auto CommitEventIfBuilt(const std::string& stateArea,
                            BuildFn&& buildEvent) {
        using Result = decltype(buildEvent(std::int64_t{}, std::int64_t{}));

        std::lock_guard<std::mutex> lock(mutex_);
        auto it = currentRevision_.find(stateArea);
        if (it == currentRevision_.end()) {
            return Result{};
        }

        std::int64_t baseRevision = it->second.first;
        std::int64_t revision = baseRevision + 1;
        auto result = buildEvent(baseRevision, revision);
        if (result.has_value()) {
            it->second.first = revision;
        }
        return result;
    }

    ///  @copydoc IRevisionTracker::NextEvent
    std::optional<std::pair<std::int64_t, std::int64_t>>
    NextEvent(const std::string& stateArea) override;

    ///  @copydoc IRevisionTracker::CurrentRevision
    [[nodiscard]] std::optional<std::int64_t>
    CurrentRevision(const std::string& stateArea) const override;

    ///  @copydoc IRevisionTracker::CommitSnapshotEnvelopeIfBuilt
    [[nodiscard]] std::optional<protocol::Envelope>
    CommitSnapshotEnvelopeIfBuilt(
        const std::string& stateArea,
        const std::optional<std::string>& fingerprint,
        std::function<std::optional<protocol::Envelope>(std::int64_t)>
            buildSnapshot) override;

    ///  @copydoc IRevisionTracker::CommitEventEnvelopeIfBuilt
    [[nodiscard]] std::optional<protocol::Envelope>
    CommitEventEnvelopeIfBuilt(
        const std::string& stateArea,
        std::function<std::optional<protocol::Envelope>(std::int64_t,
                                                        std::int64_t)>
            buildEvent) override;

  private:
    ///  Computes the next revision from the already-locked `currentRevision_`,
    ///  given a fingerprint. Shared by `StartSnapshot`, `NextSnapshotRevision`,
    ///  and `CommitSnapshotIfBuilt`. Caller must hold `mutex_`.
    ///  @param stateArea Canonical state-area identifier.
    ///  @param fingerprint Caller-computed representation of the captured state,
    ///  or no value to
    ///      always advance.
    ///  @return Revision this fingerprint would receive if committed now.
    [[nodiscard]] std::int64_t
    NextRevisionLocked(const std::string& stateArea,
                       const std::optional<std::string>& fingerprint) const;

    ///  Synchronizes access to `currentRevision_`.
    mutable std::mutex mutex_;

    ///  Latest assigned revision and the fingerprint it was assigned for, per
    ///  state area.
    std::unordered_map<std::string,
                       std::pair<std::int64_t, std::optional<std::string>>>
        currentRevision_;
};

} //  namespace dovahlink::application
