#pragma once

#include "application/play_context_lifecycle.hpp"

#include <cstdint>
#include <optional>

namespace dovahlink::application {

///  Provides the level-capture capability used by the game-state handler.
class IActivePlayContextLevelSink {
  public:
    ///  Allows destruction through the interface.
    virtual ~IActivePlayContextLevelSink() = default;

    ///  Routes one captured player level, or an unavailable value.
    ///  @param level Captured level, or no value when unavailable.
    virtual void OnLevelCaptured(std::optional<std::int64_t> level) = 0;
};

///  Routes captured level values into the lifecycle aggregate, which drops
///  captures when no authoritative context exists.
class ActivePlayContextLevelSink final : public IActivePlayContextLevelSink {
  public:
    ///  Binds the sink to the lifecycle aggregate's write capability.
    explicit ActivePlayContextLevelSink(
        IPlayContextLifecycle& playContextLifecycle);

    ///  @copydoc IActivePlayContextLevelSink::OnLevelCaptured
    void OnLevelCaptured(std::optional<std::int64_t> level) override;

  private:
    ///  Lifecycle aggregate receiving the captured level.
    IPlayContextLifecycle& playContextLifecycle_;
};

} //  namespace dovahlink::application
