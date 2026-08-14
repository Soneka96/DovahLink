#pragma once

#include "application/character_state_store.hpp"
#include "application/game_lifecycle_tracker.hpp"
#include "application/revision_tracker.hpp"

#include <memory>
#include <mutex>
#include <string>
#include <utility>

namespace dovahlink::application {

/// One authoritative Skyrim play context: the character state and revision counters that belong
/// to the currently loaded game, per `ARCHITECTURE.md`'s identity model. Never moved or copied --
/// `CharacterStateStore`'s internal mutex already forbids it -- and always held behind
/// `std::shared_ptr` so a handler already using this context cannot be left with a dangling
/// reference when a lifecycle callback invalidates or replaces the active context mid-handler.
struct PlayContext {
    /// Creates a play context with the given identifier and empty state.
    /// @param id Opaque play-context identifier assigned by `GameLifecycleTracker`.
    explicit PlayContext(std::string id) : id(std::move(id)) {}

    /// Opaque identifier of this play context.
    const std::string id;

    /// Character state captured while this play context is active.
    CharacterStateStore characterState;

    /// Revision counters for this play context's state areas.
    RevisionTracker revisions;
};

/// Owns the currently active `PlayContext`, if any, and hands out shared ownership so an
/// in-flight handler cannot be left holding a dangling reference when a lifecycle callback
/// invalidates or replaces the active context mid-handler.
class ActivePlayContext {
public:
    /// Returns the currently active play context.
    /// @return The active context, or `nullptr` outside an active context.
    [[nodiscard]] std::shared_ptr<PlayContext> AcquireCurrent() const;

    /// Clears the active play context. Idempotent when already cleared.
    void Reset();

    /// Replaces the active play context with a freshly created one.
    /// @param id Opaque identifier for the new play context.
    /// @return The newly active play context.
    std::shared_ptr<PlayContext> Begin(std::string id);

private:
    /// Synchronizes access to `current_`.
    mutable std::mutex mutex_;

    /// Currently active play context, or `nullptr` outside an active context.
    std::shared_ptr<PlayContext> current_;
};

/// Applies one `GameLifecycleTracker` transition to an `ActivePlayContext`: invalidation resets
/// it, and a freshly minted identifier begins a new one. A transition that both invalidates and
/// mints (for example `kNewGame`, or `kPostLoadGameSuccess` arriving from `kActive`) resets before
/// beginning, matching `GameLifecycleTracker`'s own invalidate-then-activate semantics.
/// @param activePlayContext Context ownership updated by this transition.
/// @param transition Effect produced by `GameLifecycleTracker::HandleEvent`.
void ApplyLifecycleTransition(ActivePlayContext& activePlayContext,
                               const GameLifecycleTracker::Transition& transition);

}  // namespace dovahlink::application
