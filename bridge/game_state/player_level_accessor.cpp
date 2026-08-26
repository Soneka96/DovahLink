#include "game_state/player_level_accessor.hpp"

#include "RE/Skyrim.h"

namespace dovahlink::game_state {

std::optional<std::int64_t> PlayerLevelAccessor::ReadLevel() const {
    auto* player = RE::PlayerCharacter::GetSingleton();
    if (!player) {
        return std::nullopt;
    }
    return static_cast<std::int64_t>(player->GetLevel());
}

} //  namespace dovahlink::game_state
