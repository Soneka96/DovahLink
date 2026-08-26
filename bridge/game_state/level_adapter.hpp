#pragma once

#include <cstdint>
#include <optional>

namespace dovahlink::game_state {

///  Reads the player's current level without exposing the runtime adapter.
class IPlayerLevelAccessor {
  public:
    ///  Allows destruction through the interface.
    virtual ~IPlayerLevelAccessor() = default;

    ///  Returns the raw current level, or `std::nullopt` when unavailable.
    [[nodiscard]] virtual std::optional<std::int64_t> ReadLevel() const = 0;
};

///  Converts a raw level reading into a positive level or an unavailable value.
[[nodiscard]] std::optional<std::int64_t>
CaptureLevel(const IPlayerLevelAccessor& accessor);

} //  namespace dovahlink::game_state
