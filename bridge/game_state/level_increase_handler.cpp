#include "game_state/level_increase_handler.hpp"

namespace dovahlink::game_state {

LevelIncreaseHandler::LevelIncreaseHandler(const IPlayerLevelAccessor& accessor,
                                           application::IActivePlayContextLevelSink& sink)
    : accessor_(accessor), sink_(sink) {}

void LevelIncreaseHandler::HandleLevelIncrease() {
    if (!sink_.IsCaptureActive()) {
        return;
    }
    sink_.OnLevelCaptured(CaptureLevel(accessor_));
}

} //  namespace dovahlink::game_state
