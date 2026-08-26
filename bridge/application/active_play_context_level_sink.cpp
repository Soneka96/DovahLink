#include "application/active_play_context_level_sink.hpp"

namespace dovahlink::application {

ActivePlayContextLevelSink::ActivePlayContextLevelSink(
    IPlayContextLifecycle& playContextLifecycle)
    : playContextLifecycle_(playContextLifecycle) {}

void ActivePlayContextLevelSink::OnLevelCaptured(
    std::optional<std::int64_t> level) {
    playContextLifecycle_.CaptureLevel(level);
}

} //  namespace dovahlink::application
