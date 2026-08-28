#include "game_state/level_increase_handler.hpp"

namespace dovahlink::game_state {

LevelIncreaseHandler::LevelIncreaseHandler(const IPlayerLevelAccessor& accessor,
                                           application::IActivePlayContextLevelSink& sink)
    : accessor_(accessor), sink_(sink) {}

void LevelIncreaseHandler::HandleLevelIncrease() {
    auto capture = sink_.BeginCapture();
    if (!capture) {
        return;
    }
    sink_.OnLevelCaptured(capture, CaptureLevel(accessor_));
}

} //  namespace dovahlink::game_state
