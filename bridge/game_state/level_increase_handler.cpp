#include "game_state/level_increase_handler.hpp"

namespace dovahlink::game_state {

LevelIncreaseHandler::LevelIncreaseHandler(const LevelAccessor& accessor, application::LevelEventSink& sink)
    : accessor_(accessor), sink_(sink) {}

void LevelIncreaseHandler::HandleLevelIncrease() { sink_.OnLevelCaptured(CaptureLevel(accessor_)); }

}  // namespace dovahlink::game_state
