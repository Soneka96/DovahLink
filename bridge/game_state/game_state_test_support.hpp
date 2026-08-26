#pragma once

#include "game_state/player_level_accessor.hpp"

#include <gmock/gmock.h>

#include <cstdint>
#include <optional>

namespace dovahlink::game_state::test_support {

///  GoogleMock player-level accessor contract double.
class MockPlayerLevelAccessor : public IPlayerLevelAccessor {
  public:
    ///  Mocks the raw level read used by game-state consumers.
    MOCK_METHOD(std::optional<std::int64_t>, ReadLevel, (),
                (const, override));
};

} //  namespace dovahlink::game_state::test_support
