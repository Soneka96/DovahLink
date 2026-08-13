#include "game_state/commonlib_level_increase_sink.hpp"

namespace dovahlink::game_state {

/**
 * @brief Creates a level-increase event sink for the specified handler.
 *
 * @param handler Handler that processes received level-increase events.
 */
CommonLibLevelIncreaseSink::CommonLibLevelIncreaseSink(LevelIncreaseHandler& handler) : handler_(handler) {}

/**
 * @brief Handles a level-increase event.
 *
 * @return RE::BSEventNotifyControl::kContinue to continue event processing.
 */
RE::BSEventNotifyControl CommonLibLevelIncreaseSink::ProcessEvent(
    const RE::LevelIncrease::Event*, RE::BSTEventSource<RE::LevelIncrease::Event>*) {
    handler_.HandleLevelIncrease();
    return RE::BSEventNotifyControl::kContinue;
}

/**
 * @brief Registers this sink with the level-increase event source.
 */
void CommonLibLevelIncreaseSink::Register() { RE::LevelIncrease::GetEventSource()->AddEventSink(this); }

/**
 * @brief Removes this sink from the level-increase event source.
 */
void CommonLibLevelIncreaseSink::Unregister() { RE::LevelIncrease::GetEventSource()->RemoveEventSink(this); }

}  // namespace dovahlink::game_state
