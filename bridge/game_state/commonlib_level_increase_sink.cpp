#include "game_state/commonlib_level_increase_sink.hpp"

namespace dovahlink::game_state {

CommonLibLevelIncreaseSink::CommonLibLevelIncreaseSink(LevelIncreaseHandler& handler) : handler_(handler) {}

RE::BSEventNotifyControl CommonLibLevelIncreaseSink::ProcessEvent(
    const RE::LevelIncrease::Event*, RE::BSTEventSource<RE::LevelIncrease::Event>*) {
    handler_.HandleLevelIncrease();
    return RE::BSEventNotifyControl::kContinue;
}

void CommonLibLevelIncreaseSink::Register() { RE::LevelIncrease::GetEventSource()->AddEventSink(this); }

void CommonLibLevelIncreaseSink::Unregister() { RE::LevelIncrease::GetEventSource()->RemoveEventSink(this); }

}  // namespace dovahlink::game_state
