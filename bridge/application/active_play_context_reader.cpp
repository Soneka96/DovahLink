#include "application/active_play_context_reader.hpp"

namespace dovahlink::application {

ActivePlayContextReader::ActivePlayContextReader(
    const IPlayContextLifecycle& playContextLifecycle)
    : playContextLifecycle_(playContextLifecycle) {}

std::optional<std::string>
ActivePlayContextReader::CurrentPlayContextId() const {
    return playContextLifecycle_.CurrentPlayContextId();
}

} //  namespace dovahlink::application
