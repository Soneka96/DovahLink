#include "game_state/commonlib_level_increase_sink.hpp"

#include <utility>

namespace dovahlink::game_state {

CommonLibLevelIncreaseSink::CommonLibLevelIncreaseSink(
    ILevelIncreaseHandler& handler)
    : handler_(handler) {}

CommonLibLevelIncreaseSink::~CommonLibLevelIncreaseSink() noexcept {
    try {
        Unregister();
    } catch (...) {
        //  Destruction cannot propagate a runtime event-source failure.
    }
}

RE::BSEventNotifyControl CommonLibLevelIncreaseSink::ProcessEvent(
    const RE::LevelIncrease::Event*,
    RE::BSTEventSource<RE::LevelIncrease::Event>*) {
    if (callbackRunner_) {
        (void)callbackRunner_([this] { handler_.HandleLevelIncrease(); });
    }
    return RE::BSEventNotifyControl::kContinue;
}

void CommonLibLevelIncreaseSink::Register(
    application::ContainedWorkRunner callbackRunner) {
    callbackRunner_ = std::move(callbackRunner);
    RE::LevelIncrease::GetEventSource()->AddEventSink(this);
    registered_ = true;
}

void CommonLibLevelIncreaseSink::Unregister() {
    if (!registered_) {
        return;
    }
    RE::LevelIncrease::GetEventSource()->RemoveEventSink(this);
    registered_ = false;
}

} //  namespace dovahlink::game_state
