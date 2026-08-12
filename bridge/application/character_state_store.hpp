#pragma once

#include "application/character_state.hpp"
#include "application/level_event_sink.hpp"
#include "application/subscription_handler.hpp"

#include <mutex>
#include <optional>

namespace dovahlink::application {

// Plugin-lifetime store for the latest captured character state. Implements
// both halves of the character state seam: LevelEventSink (the push side
// the game-state adapter's RE::LevelIncrease::Event handling calls into --
// see game_state/level_increase_handler.hpp) and CharacterStateProvider
// (the pull side subscribe/snapshot_request read from -- see
// subscription_handler.hpp).
//
// Level captures happen independent of whether any client is connected:
// CommonLib's RE::LevelIncrease::Event fires whenever it fires, and the
// initial capture happens once at plugin startup, before any socket has
// even been accepted. This store always holds the latest value regardless
// of connection state, and is thread-safe (a plain mutex-guarded read/write)
// since OnLevelCaptured runs on the game thread while CurrentCharacterSnapshot
// may be read from a session's own worker thread.
//
// OnLevelCaptured deliberately does nothing beyond storing the value.
// Turning a change into a published state_event needs a session's own
// RevisionTracker and EventCoalescer (application/revision_tracker.hpp,
// application/event_coalescer.hpp), both explicitly documented as
// single-threaded, one-owner types -- and OnLevelCaptured runs synchronously
// on the game thread (application/level_event_sink.hpp), which must never
// call into worker/session-owned state directly
// (ai/context/skse/architecture.md: "capture the required value, enqueue
// work, and return"; "never perform blocking... or expensive serialization
// work inside a game-thread hook"). The worker thread that will own a
// session's RevisionTracker/EventCoalescer does not exist yet
// (application/coordinator.hpp's WorkerPool is still a seam, not an
// implementation, by that file's own documentation) -- deciding how that
// worker learns "the stored snapshot changed" (poll
// CurrentCharacterSnapshot on wake, a notification seam, etc.) belongs with
// whatever step gives WorkerPool its real implementation, not here.
//
// Concretely, today: this class fully solves the pull side (subscribe and
// snapshot_request already see live level data through
// CharacterStateProvider). It does not touch EventCoalescer at all yet --
// a level change reaching an already-subscribed, connected client as a
// state_event is not implemented. That client only sees the new value on
// its next snapshot_request. This is an intentional, sequenced gap, not a
// finished feature -- do not read "feeds EventCoalescer" as already true.
class CharacterStateStore : public LevelEventSink, public CharacterStateProvider {
public:
    void OnLevelCaptured(std::optional<std::int64_t> level) override;

    [[nodiscard]] CharacterSnapshot CurrentCharacterSnapshot() const override;

private:
    mutable std::mutex mutex_;
    CharacterSnapshot snapshot_;
};

}  // namespace dovahlink::application
