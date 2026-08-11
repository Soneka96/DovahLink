#pragma once

#include "application/level_event_sink.hpp"
#include "game_state/level_adapter.hpp"

namespace dovahlink::game_state {

// Skyrim-independent glue between a level-change trigger and the
// application boundary. HandleLevelIncrease captures the current level
// through `accessor` -- never the triggering event's own fields, see
// commonlib_level_increase_sink.hpp -- and pushes the owned result to
// `sink` synchronously, matching LevelEventSink's documented contract that
// OnLevelCaptured is called synchronously from the RE::LevelIncrease::Event
// callback (application/level_event_sink.hpp). The one CommonLib-touching
// caller of this class is CommonLibLevelIncreaseSink.
class LevelIncreaseHandler {
public:
    LevelIncreaseHandler(const LevelAccessor& accessor, application::LevelEventSink& sink);

    void HandleLevelIncrease();

private:
    const LevelAccessor& accessor_;
    application::LevelEventSink& sink_;
};

}  // namespace dovahlink::game_state
