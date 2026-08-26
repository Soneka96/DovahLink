#include "application/active_play_context_reader.hpp"

namespace dovahlink::application {

ActivePlayContextReader::ActivePlayContextReader(
    const IPlayContextLifecycle& playContextLifecycle)
    : playContextLifecycle_(playContextLifecycle) {}

std::shared_ptr<PlayContext>
ActivePlayContextReader::AcquireCurrent() const {
    return playContextLifecycle_.AcquireCurrent();
}

} //  namespace dovahlink::application
