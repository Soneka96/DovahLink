#pragma once

#include <optional>
#include <string>

namespace dovahlink::application {

///  Reports the effect of one processed play-context lifecycle event.
struct PlayContextTransition {
    ///  Whether this event invalidated an active context or terminated a loading
    ///  lifecycle. Preload and failed-load report this only for an active context;
    ///  revert also reports it when ending a loading lifecycle.
    bool contextInvalidated = false;

    ///  The freshly minted play-context identifier, when one was created.
    std::optional<std::string> newPlayContextId;
};

} //  namespace dovahlink::application
