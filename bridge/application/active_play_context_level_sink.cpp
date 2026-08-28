#include "application/active_play_context_level_sink.hpp"

#include "shared/enums.hpp"

namespace dovahlink::application {

ActivePlayContextLevelSink::ActivePlayContextLevelSink(
    IPlayContextLifecycle& playContextLifecycle)
    : playContextLifecycle_(playContextLifecycle) {}

void ActivePlayContextLevelSink::OnLevelCaptured(
    std::optional<std::int64_t> level) {
    playContextLifecycle_.CaptureLevel(level);
}

bool ActivePlayContextLevelSink::IsCaptureActive() const {
    return playContextLifecycle_.CurrentState() == LifecycleState::kActive;
}

} //  namespace dovahlink::application
