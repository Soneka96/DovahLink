#include "game_state/commonlib_level_accessor.hpp"

#include "RE/Skyrim.h"

namespace dovahlink::game_state {

/**
 * @brief Retrieves the current player's level.
 *
 * @return The player's level, or `std::nullopt` if the player is unavailable.
 */
std::optional<std::int64_t> CommonLibLevelAccessor::ReadLevel() const {
    auto* player = RE::PlayerCharacter::GetSingleton();
    if (!player) {
        return std::nullopt;
    }
    return static_cast<std::int64_t>(player->GetLevel());
}

}  // namespace dovahlink::game_state
