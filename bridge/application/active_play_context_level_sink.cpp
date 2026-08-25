#include "application/active_play_context_level_sink.hpp"

namespace dovahlink::application {

ActivePlayContextLevelSink::ActivePlayContextLevelSink(
    const IActivePlayContext& activePlayContext)
    : activePlayContext_(activePlayContext) {}

void ActivePlayContextLevelSink::OnLevelCaptured(
    std::optional<std::int64_t> level) {
    auto context = activePlayContext_.AcquireCurrent();
    if (context) {
        context->characterState.OnLevelCaptured(level);
    }
}

} //  namespace dovahlink::application
