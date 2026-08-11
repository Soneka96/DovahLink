#pragma once

#include <cstdint>
#include <optional>
#include <string>
#include <unordered_map>
#include <utility>

namespace dovahlink::application {

// Assigns the monotonically increasing revision sequence for each state area
// within one session, per protocol/schema/README.md's state envelope rules:
// revisions are scoped to one state area and session, reset only by
// constructing a fresh tracker for a new session (one instance per session),
// and a snapshot establishes the baseline that events build on with
// `revision == baseRevision + 1`.
//
// This is the bridge's own producer-side sequencing: it is the sole
// authority assigning revisions, so there is nothing external to validate
// against -- StartSnapshot and NextEvent cannot themselves be asked to
// produce a duplicate, stale, or out-of-order revision. NextEvent instead
// guards the one invariant that could otherwise be violated by a coordinator
// bug: an event can never be assigned before its area's first snapshot.
//
// Not thread-safe: one instance per session, intended to be used serially by
// that session's own handling.
class RevisionTracker {
public:
    RevisionTracker() = default;

    // Establishes a fresh baseline revision for `stateArea` -- either the
    // area's first snapshot in this session (revision 1), or a recovery
    // snapshot after queue loss, which continues the same monotonic
    // sequence rather than restarting it. Returns the revision to publish.
    std::int64_t StartSnapshot(const std::string& stateArea);

    // Returns (baseRevision, revision) for the next event on `stateArea`,
    // advancing the tracked revision by exactly 1. Returns nullopt if no
    // baseline has been established yet for this area: an event cannot
    // precede its area's first snapshot.
    std::optional<std::pair<std::int64_t, std::int64_t>> NextEvent(const std::string& stateArea);

    // The most recently assigned revision for `stateArea`, or nullopt if no
    // baseline has been established yet.
    [[nodiscard]] std::optional<std::int64_t> CurrentRevision(const std::string& stateArea) const;

private:
    std::unordered_map<std::string, std::int64_t> currentRevision_;
};

}  // namespace dovahlink::application
