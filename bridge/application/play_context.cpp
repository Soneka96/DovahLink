#include "application/play_context.hpp"

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
    auto context = std::make_shared<PlayContext>(std::move(id));
    std::lock_guard<std::mutex> lock(mutex_);
    current_ = context;
    return context;
}

ActivePlayContextLevelSink::ActivePlayContextLevelSink(
    ActivePlayContext& activePlayContext)
    : activePlayContext_(activePlayContext) {}

void ActivePlayContextLevelSink::OnLevelCaptured(
    std::optional<std::int64_t> level) {
    auto context = activePlayContext_.AcquireCurrent();
    if (context) {
        context->characterState.OnLevelCaptured(level);
    }
}

void ApplyLifecycleTransition(
    ActivePlayContext& activePlayContext,
    const GameLifecycleTracker::Transition& transition) {
    if (transition.contextInvalidated) {
        activePlayContext.Reset();
    }
    if (transition.newPlayContextId.has_value()) {
        activePlayContext.Begin(*transition.newPlayContextId);
    }
}

} //  namespace dovahlink::application
