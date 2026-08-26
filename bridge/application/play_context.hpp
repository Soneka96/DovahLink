#pragma once

#include "application/character_state_store.hpp"
#include "application/revision_tracker.hpp"

#include <string>
#include <utility>

namespace dovahlink::application {

///  One authoritative Skyrim play context: the character state captured while
///  the currently loaded game is active, per `ARCHITECTURE.md`'s identity model.
///  Never moved or copied -- `CharacterStateStore`'s internal mutex already
///  forbids it -- and always held behind `std::shared_ptr` so a handler already
///  using this context cannot be left with a dangling reference when a lifecycle
///  callback invalidates or replaces the active context mid-handler.
struct PlayContext {
    ///  Creates a play context with the given identifier and empty state.
    ///  @param id Opaque play-context identifier assigned by
    ///  `PlayContextLifecycle`.
    explicit PlayContext(std::string id) : id(std::move(id)) {}

    ///  Opaque identifier of this play context.
    const std::string id;

    ///  Character state captured while this play context is active.
    CharacterStateStore characterState;

    ///  Revisions for state areas belonging to this play context.
    RevisionTracker revisions;
};

} //  namespace dovahlink::application
