#pragma once

#include <cstdint>
#include <optional>

namespace dovahlink::game_state {

// Reads the player's current level. This interface is the seam
// ai/context/skse/testing.md calls for ("test game-state conversion with
// plain representative values; do not require a running Skyrim process"):
// CaptureLevel below depends only on this abstract interface, so it is
// fully testable with a fake implementation. Only the real implementation
// (bridge/game_state/commonlib_level_accessor.hpp, a separate file so
// nothing that depends on CommonLib/RE:: types is pulled into this
// Skyrim-independent target) may touch CommonLib.
class LevelAccessor {
public:
    virtual ~LevelAccessor() = default;

    // Returns the player's raw current level, or nullopt if no player
    // object is available to query right now (e.g. before a save is
    // loaded). This is the raw reading, not yet validated as trustworthy;
    // see CaptureLevel.
    [[nodiscard]] virtual std::optional<std::int64_t> ReadLevel() const = 0;
};

// Converts one LevelAccessor reading into the value CaptureLevel's caller
// publishes as the character state area's level. A raw reading of zero or
// negative is treated as untrustworthy rather than a real value: Skyrim
// characters are always level 1 or higher, so such a reading does not
// represent a plausible level to publish as current
// (ai/context/skse/cpp-style.md: "Treat missing, delayed, and stale values
// explicitly; do not substitute plausible values silently" -- the same
// principle applies to not treating an implausible raw value as valid).
[[nodiscard]] std::optional<std::int64_t> CaptureLevel(const LevelAccessor& accessor);

}  // namespace dovahlink::game_state
