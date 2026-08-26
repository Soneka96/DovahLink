#pragma once

#include "game_state/player_level_accessor.hpp"

#include <cstdint>
#include <optional>

namespace dovahlink::game_state {

///  Converts a raw level reading into a positive level or an unavailable value.
[[nodiscard]] std::optional<std::int64_t>
CaptureLevel(const IPlayerLevelAccessor& accessor);

} //  namespace dovahlink::game_state
