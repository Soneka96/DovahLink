#include "game_state/level_adapter.hpp"

namespace dovahlink::game_state {

/**
 * @brief Captures a valid level value from the accessor.
 *
 * @param accessor Source of the level value.
 * @return The level when it is greater than zero; an empty optional otherwise.
 */
std::optional<std::int64_t> CaptureLevel(const LevelAccessor& accessor) {
    std::optional<std::int64_t> raw = accessor.ReadLevel();
    if (!raw.has_value() || *raw <= 0) {
        return std::nullopt;
    }
    return raw;
}

}  // namespace dovahlink::game_state
