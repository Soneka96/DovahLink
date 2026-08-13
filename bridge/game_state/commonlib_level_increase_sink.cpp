#include "game_state/commonlib_level_increase_sink.hpp"

#include <utility>

namespace dovahlink::game_state {

CommonLibLevelIncreaseSink::CommonLibLevelIncreaseSink(LevelIncreaseHandler& handler) : handler_(handler) {}

RE::BSEventNotifyControl CommonLibLevelIncreaseSink::ProcessEvent(const RE::LevelIncrease::Event*,
                                                                  RE::BSTEventSource<RE::LevelIncrease::Event>*) {
    if (callbackRunner_) {
        (void)callbackRunner_([this] { handler_.HandleLevelIncrease(); });
    }
    return RE::BSEventNotifyControl::kContinue;
}

void CommonLibLevelIncreaseSink::Register(application::ContainedWorkRunner callbackRunner) {
    callbackRunner_ = std::move(callbackRunner);
    RE::LevelIncrease::GetEventSource()->AddEventSink(this);
}

void CommonLibLevelIncreaseSink::Unregister() { RE::LevelIncrease::GetEventSource()->RemoveEventSink(this); }

}  // namespace dovahlink::game_state
