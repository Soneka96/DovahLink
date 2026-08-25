#pragma once

#include "application/active_play_context.hpp"
#include "application/level_event_sink.hpp"

#include <cstdint>
#include <optional>

namespace dovahlink::application {

///  Routes captured level values into whichever play context is currently
///  active, dropping captures when no authoritative context exists.
class ActivePlayContextLevelSink final : public ILevelEventSink {
  public:
    ///  Binds the sink to the active play-context capability.
    explicit ActivePlayContextLevelSink(
        const IActivePlayContext& activePlayContext);

    ///  @copydoc ILevelEventSink::OnLevelCaptured
    void OnLevelCaptured(std::optional<std::int64_t> level) override;

  private:
    ///  Source of the currently active play context.
    const IActivePlayContext& activePlayContext_;
};

} //  namespace dovahlink::application
