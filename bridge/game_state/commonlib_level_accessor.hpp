#pragma once

#include "game_state/level_adapter.hpp"

namespace dovahlink::game_state {

/// Reads the current player's level through the CommonLib runtime API.
class CommonLibLevelAccessor : public LevelAccessor {
public:
    /// @copydoc LevelAccessor::ReadLevel
    [[nodiscard]] std::optional<std::int64_t> ReadLevel() const override;
};

}  // namespace dovahlink::game_state
