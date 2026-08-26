#pragma once

#include "application/active_play_context_reader.hpp"

#include <cstdint>
#include <optional>

namespace dovahlink::application {

///  Routes captured levels into the currently active play context.
class IActivePlayContextLevelSink {
  public:
    ///  Allows destruction through the interface.
    virtual ~IActivePlayContextLevelSink() = default;

    ///  Routes one captured player level, or an unavailable value.
    ///  @param level Captured level, or no value when unavailable.
    virtual void OnLevelCaptured(std::optional<std::int64_t> level) = 0;
};

///  Routes captured level values into whichever play context is currently
///  active, dropping captures when no authoritative context exists.
class ActivePlayContextLevelSink final : public IActivePlayContextLevelSink {
  public:
    ///  Binds the sink to the active play-context capability.
    explicit ActivePlayContextLevelSink(
        const IActivePlayContextReader& activePlayContext);

    ///  @copydoc IActivePlayContextLevelSink::OnLevelCaptured
    void OnLevelCaptured(std::optional<std::int64_t> level) override;

  private:
    ///  Source of the currently active play context.
    const IActivePlayContextReader& activePlayContext_;
};

} //  namespace dovahlink::application
