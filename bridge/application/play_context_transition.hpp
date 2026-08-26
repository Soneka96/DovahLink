#pragma once

#include <optional>
#include <string>

namespace dovahlink::application {

///  Reports the effect of one processed play-context lifecycle event.
struct PlayContextTransition {
    ///  Whether a previously active or loading play context was invalidated.
    bool contextInvalidated = false;

    ///  The freshly minted play-context identifier, when one was created.
    std::optional<std::string> newPlayContextId;
};

} //  namespace dovahlink::application
