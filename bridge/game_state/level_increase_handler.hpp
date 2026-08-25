#pragma once

#include "application/level_event_sink.hpp"
#include "game_state/level_adapter.hpp"

namespace dovahlink::game_state {

/// Converts a level-change notification into a synchronous application event.
class LevelIncreaseHandler {
public:
  /// Binds the level source and synchronous application event sink.
  LevelIncreaseHandler(const LevelAccessor &accessor,
                       application::LevelEventSink &sink);

  /// Captures the current level and publishes it to the event sink.
  void HandleLevelIncrease();

private:
  /// Source for the current raw level reading.
  const LevelAccessor &accessor_;
  /// Receives the captured level, including unavailable values.
  application::LevelEventSink &sink_;
};

} // namespace dovahlink::game_state
