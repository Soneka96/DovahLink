#pragma once

#include "application/character_state.hpp"
#include "application/level_event_sink.hpp"
#include "application/subscription_handler.hpp"

#include <mutex>
#include <optional>

namespace dovahlink::application {

/// Stores the latest captured character state for bridge-lifetime consumers.
/// Access is synchronized because capture and reads may occur on different threads.
class CharacterStateStore : public LevelEventSink, public CharacterStateProvider {
public:
    /// @copydoc LevelEventSink::OnLevelCaptured
    void OnLevelCaptured(std::optional<std::int64_t> level) override;

    /// @copydoc CharacterStateProvider::CurrentCharacterSnapshot
    [[nodiscard]] CharacterSnapshot CurrentCharacterSnapshot() const override;

private:
    /// Synchronizes access to `snapshot_`.
    mutable std::mutex mutex_;

    /// Most recently captured character state.
    CharacterSnapshot snapshot_;
};

}  // namespace dovahlink::application
