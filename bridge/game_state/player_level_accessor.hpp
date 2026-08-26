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

///  Reads the current player's level through the CommonLib runtime API.
class PlayerLevelAccessor : public IPlayerLevelAccessor {
  public:
    ///  @copydoc IPlayerLevelAccessor::ReadLevel
    [[nodiscard]] std::optional<std::int64_t> ReadLevel() const override;
};

} //  namespace dovahlink::game_state
