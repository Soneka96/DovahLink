#pragma once

#include "game_state/level_adapter.hpp"

namespace dovahlink::game_state {

// The only implementation of LevelAccessor that touches CommonLib/RE::
// types, per ai/context/skse/architecture.md: "Game-state adapters are the
// only bridge components that may depend directly on CommonLib or Skyrim
// runtime types." Reads through RE::PlayerCharacter::GetSingleton(), the
// approved runtime API for the player's current level (confirmed against
// the pinned CommonLibSSE-NG commit -- see bridge/README.md -- rather than
// assumed: PlayerCharacter inherits GetLevel() from RE::Actor).
//
// Not unit-tested: there is nothing to fake here, this IS the real
// implementation, and it cannot run without a live Skyrim process
// (ai/context/skse/testing.md: "Reserve manual in-game checks for runtime
// hooks... that cannot be represented in unit tests"). Correctness is
// verified by code inspection and the manual in-game verification record
// TASK.md requires before Phase 1 is declared complete.
//
// This type lives in a separate translation unit and CMake target from the
// Skyrim-independent bridge/game_state/level_adapter.* files so that
// dovahlink_bridge_tests never needs to link CommonLibSSE-NG.
class CommonLibLevelAccessor : public LevelAccessor {
public:
    [[nodiscard]] std::optional<std::int64_t> ReadLevel() const override;
};

}  // namespace dovahlink::game_state
