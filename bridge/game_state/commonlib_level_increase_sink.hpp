#pragma once

#include "game_state/level_increase_handler.hpp"

#include "RE/Skyrim.h"

namespace dovahlink::game_state {

/// Receives Skyrim level-increase events and forwards them to a level handler.
class CommonLibLevelIncreaseSink : public RE::BSTEventSink<RE::LevelIncrease::Event> {
public:
    /// Binds the sink to the handler that captures and publishes current level state.
    explicit CommonLibLevelIncreaseSink(LevelIncreaseHandler& handler);

    /// Captures current state through the handler and continues event-source processing.
    RE::BSEventNotifyControl ProcessEvent(const RE::LevelIncrease::Event* event,
                                           RE::BSTEventSource<RE::LevelIncrease::Event>* eventSource) override;

    /// Registers this sink with the Skyrim level-increase event source.
    void Register();
    /// Unregisters this sink from the Skyrim level-increase event source.
    void Unregister();

private:
    /// Handler that owns the application-facing level-capture behavior.
    LevelIncreaseHandler& handler_;
};

}  // namespace dovahlink::game_state
