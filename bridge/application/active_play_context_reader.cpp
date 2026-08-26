#include "application/active_play_context_reader.hpp"

namespace dovahlink::application {

ActivePlayContextReader::ActivePlayContextReader(
    const IActivePlayContext& activePlayContext)
    : activePlayContext_(activePlayContext) {}

std::shared_ptr<PlayContext>
ActivePlayContextReader::AcquireCurrent() const {
    return activePlayContext_.AcquireCurrent();
}

} //  namespace dovahlink::application
