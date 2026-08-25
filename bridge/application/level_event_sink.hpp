#pragma once

#include <cstdint>
#include <optional>

namespace dovahlink::application {

///  Receives owned player-level captures from the game-state boundary.
class ILevelEventSink {
  public:
    ///  Releases the interface without performing work.
    virtual ~ILevelEventSink() = default;

    ///  Stores or forwards one captured level value.
    ///  @param level Captured level, or no value when unavailable.
    virtual void OnLevelCaptured(std::optional<std::int64_t> level) = 0;
};

} //  namespace dovahlink::application
