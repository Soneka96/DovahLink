#pragma once

#include "application/coordinator.hpp"
#include "game_state/level_increase_handler.hpp"

#include "RE/Skyrim.h"

namespace dovahlink::game_state {

///  Receives Skyrim level-increase events and forwards them to a level handler.
class CommonLibLevelIncreaseSink
    : public RE::BSTEventSink<RE::LevelIncrease::Event> {
  public:
    ///  Binds the sink to the handler that captures and publishes current level
    ///  state.
    explicit CommonLibLevelIncreaseSink(ILevelIncreaseHandler& handler);

    ///  Unregisters the sink as a final fallback before its dependent handler is
    ///  destroyed.
    ~CommonLibLevelIncreaseSink() noexcept;

    ///  Captures current state through the handler and continues event-source
    ///  processing.
    RE::BSEventNotifyControl ProcessEvent(
        const RE::LevelIncrease::Event* event,
        RE::BSTEventSource<RE::LevelIncrease::Event>* eventSource) override;

    ///  Registers this sink with the Skyrim level-increase event source.
    ///  @param callbackRunner Guarded containment boundary retained by the sink.
    void Register(application::ContainedWorkRunner callbackRunner);
    ///  Unregisters this sink from the Skyrim level-increase event source once.
    void Unregister();

  private:
    ///  Handler that owns the application-facing level-capture behavior.
    ILevelIncreaseHandler& handler_;

    ///  Coordinator-owned admission and exception boundary for runtime callbacks.
    application::ContainedWorkRunner callbackRunner_;

    ///  Whether this sink is currently registered with Skyrim's event source.
    bool registered_ = false;
};

} //  namespace dovahlink::game_state
