#include "application/active_play_context_provider.hpp"

namespace dovahlink::application {

ActivePlayContextProvider::ActivePlayContextProvider(
    const IPlayContextLifecycle& playContextLifecycle)
    : playContextLifecycle_(playContextLifecycle) {}

std::shared_ptr<PlayContext>
ActivePlayContextProvider::CurrentPlayContext() const {
    return playContextLifecycle_.CurrentPlayContext();
}

} //  namespace dovahlink::application
