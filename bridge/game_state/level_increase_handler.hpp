#pragma once

#include "application/active_play_context_level_sink.hpp"
#include "game_state/level_adapter.hpp"

namespace dovahlink::game_state {

///  Receives a level-change notification and publishes the captured level.
class ILevelIncreaseHandler {
  public:
    ///  Allows destruction through the interface.
    virtual ~ILevelIncreaseHandler() = default;

    ///  Captures the current level and publishes it to the event sink.
    virtual void HandleLevelIncrease() = 0;
};

///  Converts a level-change notification into a synchronous application event.
class LevelIncreaseHandler : public ILevelIncreaseHandler {
  public:
    ///  Binds the level source and active-context level sink.
    LevelIncreaseHandler(const IPlayerLevelAccessor& accessor,
                         application::IActivePlayContextLevelSink& sink);

    ///  @copydoc ILevelIncreaseHandler::HandleLevelIncrease
    void HandleLevelIncrease() override;

  private:
    ///  Source for the current raw level reading.
    const IPlayerLevelAccessor& accessor_;
    ///  Receives the captured level, including unavailable values.
    application::IActivePlayContextLevelSink& sink_;
};

} //  namespace dovahlink::game_state
