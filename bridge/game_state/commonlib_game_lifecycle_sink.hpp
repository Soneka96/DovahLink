#pragma once

#include "application/active_play_context.hpp"
#include "application/coordinator.hpp"
#include "application/game_lifecycle_tracker.hpp"

#include "SKSE/SKSE.h"

#include <atomic>
#include <cstdint>
#include <string_view>

namespace dovahlink::game_state {

///  Translates raw SKSE messaging and serialization-revert callbacks into
///  GameLifecycleTracker events, applies the resulting transition to the
///  active play context, and logs every raw callback and resulting transition
///  with a monotonic sequence number and thread ID for empirical verification
///  of real Skyrim/SKSE callback ordering (see task.md).
///
///  SKSE offers no listener-removal capability for the messaging or
///  serialization interfaces, so registration is a one-time operation for the
///  plugin's lifetime; there is no matching unregister.
class CommonLibGameLifecycleSink {
  public:
    ///  Binds the sink to the tracker it drives and the play context it updates.
    ///  @param tracker Lifecycle state machine driven by decoded SKSE signals.
    ///  @param activePlayContext Play context capability updated by every
    ///  resulting transition.
    CommonLibGameLifecycleSink(application::GameLifecycleTracker& tracker,
                               application::IActivePlayContext& activePlayContext);

    ///  Binds the containment boundary used by every subsequent callback.
    ///  Must be called once, before SKSE can deliver any callback.
    ///  @param callbackRunner Guarded containment boundary retained by the sink.
    void Register(application::ContainedWorkRunner callbackRunner);

    ///  Handles one SKSE::MessagingInterface message. Recognizes
    ///  kPreLoadGame, kPostLoadGame, and kNewGame; every other message type
    ///  is ignored.
    ///  @param message Message delivered by SKSE's messaging interface.
    void OnMessage(const SKSE::MessagingInterface::Message& message);

    ///  Handles SKSE's serialization revert callback.
    void OnRevert();

  private:
    ///  Logs one decoded lifecycle event, forwards it to the tracker, and
    ///  logs the resulting transition, both tagged with the same sequence
    ///  number.
    ///  @param event Decoded lifecycle event.
    ///  @param rawDescription Diagnostic description of the raw SKSE signal.
    void HandleEvent(application::LifecycleEvent event,
                     std::string_view rawDescription);

    ///  Tracker that owns lifecycle state transitions.
    application::GameLifecycleTracker& tracker_;

    ///  Play context ownership updated by every processed transition.
    application::IActivePlayContext& activePlayContext_;

    ///  Coordinator-owned admission and exception boundary for runtime callbacks.
    application::ContainedWorkRunner callbackRunner_;

    ///  Monotonically increasing counter identifying each processed callback
    ///  in the diagnostic log, independent of any other sequence in the bridge.
    std::atomic<std::uint64_t> sequence_{0};
};

} //  namespace dovahlink::game_state
