#include "game_state/level_increase_handler.hpp"

namespace dovahlink::game_state {

/**
     * @brief Creates a handler for capturing level increases.
     *
     * @param accessor Provides access to the current level.
     * @param sink Receives captured level events.
     */
    LevelIncreaseHandler::LevelIncreaseHandler(const LevelAccessor& accessor, application::LevelEventSink& sink)
    : accessor_(accessor), sink_(sink) {}

/**
 * @brief Captures the current level and reports it to the level event sink.
 */
void LevelIncreaseHandler::HandleLevelIncrease() { sink_.OnLevelCaptured(CaptureLevel(accessor_)); }

}  // namespace dovahlink::game_state
