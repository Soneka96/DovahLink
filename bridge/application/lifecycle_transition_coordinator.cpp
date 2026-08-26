#include "application/lifecycle_transition_coordinator.hpp"

namespace dovahlink::application {

LifecycleTransitionCoordinator::LifecycleTransitionCoordinator(
    GameLifecycleTracker& tracker, IActivePlayContext& activePlayContext)
    : tracker_(tracker), activePlayContext_(activePlayContext) {}

GameLifecycleTracker::Transition LifecycleTransitionCoordinator::HandleEvent(
    LifecycleEvent event) {
    std::lock_guard<std::mutex> lifecycleLock(lifecycleMutex_);
    auto transition = tracker_.HandleEvent(event);
    ApplyLifecycleTransition(activePlayContext_, transition);
    return transition;
}

} //  namespace dovahlink::application
