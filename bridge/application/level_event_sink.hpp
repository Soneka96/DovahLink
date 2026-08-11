#pragma once

#include <cstdint>
#include <optional>

namespace dovahlink::application {

// The application-side receiver for pushed level captures. The game-state
// adapter (the only code allowed to depend on CommonLib/RE:: types, see
// ai/context/skse/architecture.md) calls OnLevelCaptured synchronously from
// its RE::LevelIncrease::Event callback with an already-captured, owned
// value -- never a deferred or repeated read.
//
// This interface intentionally exposes no "get the current level" or "poll"
// method: the only way level data can enter the application layer is by
// being pushed in through this one call. A worker or coordinator bug cannot
// silently start polling Skyrim through this seam, because there is nothing
// here to poll -- attempting to would be a compile error, not a runtime
// mistake to catch. See ai/context/skse/architecture.md's "do not poll
// Skyrim from a worker" rule. For example, `sink.GetCurrentLevel()` is not a
// bug to avoid writing -- it is simply not a member this class has.
class LevelEventSink {
public:
    virtual ~LevelEventSink() = default;

    // Called once per RE::LevelIncrease::Event (and once for the initial
    // capture at plugin startup), with the level already read and copied by
    // the game-state adapter. `level` is nullopt only if the adapter could
    // not establish a trustworthy value at capture time.
    virtual void OnLevelCaptured(std::optional<std::int64_t> level) = 0;
};

}  // namespace dovahlink::application
