#pragma once

#include "application/play_context.hpp"
#include "application/play_context_lifecycle.hpp"

#include <memory>

namespace dovahlink::application {

///  Read-only capability for game-thread and worker-thread consumers that
///  need the pinned authoritative play context itself -- to capture against,
///  apply a value to, or check for staleness -- rather than only its
///  identifier. `IActivePlayContextReader` remains the narrow view for
///  session-facing consumers that only ever need the identity.
class IActivePlayContextProvider {
  public:
    ///  Allows destruction through the interface.
    virtual ~IActivePlayContextProvider() = default;

    ///  Returns the currently active play context, or `nullptr` when none is
    ///  active (including while loading).
    [[nodiscard]] virtual std::shared_ptr<PlayContext> CurrentPlayContext() const = 0;
};

///  Adapts the lifecycle aggregate to its read-only pinned-context contract.
class ActivePlayContextProvider final : public IActivePlayContextProvider {
  public:
    ///  Binds the provider to the lifecycle-owned context.
    ///  @param playContextLifecycle Lifecycle aggregate used as the read
    ///  source.
    explicit ActivePlayContextProvider(
        const IPlayContextLifecycle& playContextLifecycle);

    ///  @copydoc IActivePlayContextProvider::CurrentPlayContext
    [[nodiscard]] std::shared_ptr<PlayContext> CurrentPlayContext() const override;

  private:
    ///  Lifecycle aggregate whose current context is exposed read-only.
    const IPlayContextLifecycle& playContextLifecycle_;
};

} //  namespace dovahlink::application
