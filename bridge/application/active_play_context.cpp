#include "application/active_play_context.hpp"

#include "application/game_lifecycle_tracker.hpp"

#include <utility>

namespace dovahlink::application {

std::shared_ptr<PlayContext> ActivePlayContext::AcquireCurrent() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return current_;
}

void ActivePlayContext::Reset() {
    std::lock_guard<std::mutex> lock(mutex_);
    current_.reset();
}

std::shared_ptr<PlayContext> ActivePlayContext::Begin(std::string id) {
    return Replace(std::move(id));
}

std::shared_ptr<PlayContext> ActivePlayContext::Replace(std::string id) {
    auto context = std::make_shared<PlayContext>(std::move(id));
    std::lock_guard<std::mutex> lock(mutex_);
    current_ = context;
    return context;
}

void ApplyLifecycleTransition(
    IActivePlayContext& activePlayContext,
    const GameLifecycleTracker::Transition& transition) {
    if (transition.newPlayContextId.has_value()) {
        activePlayContext.Replace(*transition.newPlayContextId);
    } else if (transition.contextInvalidated) {
        activePlayContext.Reset();
    }
}

} //  namespace dovahlink::application
