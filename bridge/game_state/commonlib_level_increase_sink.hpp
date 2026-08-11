#pragma once

#include "game_state/level_increase_handler.hpp"

#include "RE/Skyrim.h"

namespace dovahlink::game_state {

// The one CommonLib-touching file for the level-increase boundary (see
// ai/context/skse/architecture.md: "Game-state adapters are the only bridge
// components that may depend directly on CommonLib or Skyrim runtime
// types"). Deliberately ignores RE::LevelIncrease::Event's own fields
// (event->player, event->newLevel, confirmed at
// RE/L/LevelIncrease.h in the pinned CommonLibSSE-NG source): per
// level_adapter.hpp's design, the trustworthy level always comes from
// re-reading current state through the adapter, never from a value or
// borrowed pointer handed in by the event. No RE:: pointer from the event
// is ever captured or retained past ProcessEvent's return.
class CommonLibLevelIncreaseSink : public RE::BSTEventSink<RE::LevelIncrease::Event> {
public:
    explicit CommonLibLevelIncreaseSink(LevelIncreaseHandler& handler);

    // Per ai/context/skse/cpp-style.md, a game-thread callback must not let
    // an exception escape it; Coordinator::RunContained
    // (application/coordinator.hpp) exists to wrap exactly this kind of
    // callback body. This class does not hold a Coordinator reference --
    // that wiring belongs to whichever later step connects this sink to a
    // live coordinator (the SKSE plugin entry point) -- so containment is a
    // known, intentional gap here, not an oversight, until that wiring
    // exists.
    RE::BSEventNotifyControl ProcessEvent(const RE::LevelIncrease::Event* event,
                                           RE::BSTEventSource<RE::LevelIncrease::Event>* eventSource) override;

    // Wraps RE::LevelIncrease::GetEventSource()->AddEventSink/RemoveEventSink.
    // Registration and unregistration under the real SKSE lifecycle
    // (kDataLoaded, plugin unload) cannot be exercised without Skyrim -- see
    // ai/context/skse/testing.md's "Reserve manual in-game checks for
    // runtime hooks, event timing... that cannot be represented in unit
    // tests" -- and belong in the manual verification record instead.
    void Register();
    void Unregister();

private:
    LevelIncreaseHandler& handler_;
};

}  // namespace dovahlink::game_state
