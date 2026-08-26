#pragma once

#include "application/active_play_context.hpp"

#include <mutex>

namespace dovahlink::application {

///  Owns serialized application-side lifecycle transitions.
class ILifecycleTransitionCoordinator {
  public:
    ///  Allows destruction through the interface.
    virtual ~ILifecycleTransitionCoordinator() = default;

    ///  Processes one lifecycle event and applies its resulting transition.
    ///  @param event Lifecycle event to process.
    ///  @return The transition applied to the active play context.
    virtual GameLifecycleTracker::Transition HandleEvent(
        LifecycleEvent event) = 0;
};

///  Serializes lifecycle tracker updates with active-context publication.
class LifecycleTransitionCoordinator final
    : public ILifecycleTransitionCoordinator {
  public:
    ///  Binds the tracker and active-context owner used by transitions.
    ///  @param tracker Lifecycle state machine to update.
    ///  @param activePlayContext Context owner updated by each transition.
    LifecycleTransitionCoordinator(GameLifecycleTracker& tracker,
                                   IActivePlayContext& activePlayContext);

    ///  @copydoc ILifecycleTransitionCoordinator::HandleEvent
    GameLifecycleTracker::Transition HandleEvent(
        LifecycleEvent event) override;

  private:
    ///  Tracker whose state is protected by `lifecycleMutex_`.
    GameLifecycleTracker& tracker_;

    ///  Active context updated while `lifecycleMutex_` is held.
    IActivePlayContext& activePlayContext_;

    ///  Serializes tracker updates with active-context publication.
    std::mutex lifecycleMutex_;
};

} //  namespace dovahlink::application
