#pragma once

#include "application/level_event_sink.hpp"
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
    ///  Binds the level source and synchronous application event sink.
    LevelIncreaseHandler(const ILevelAccessor& accessor,
                         application::ILevelEventSink& sink);

    ///  @copydoc ILevelIncreaseHandler::HandleLevelIncrease
    void HandleLevelIncrease() override;

  private:
    ///  Source for the current raw level reading.
    const ILevelAccessor& accessor_;
    ///  Receives the captured level, including unavailable values.
    application::ILevelEventSink& sink_;
};

} //  namespace dovahlink::game_state
