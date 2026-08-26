#pragma once

#include "application/character_state.hpp"

#include <mutex>
#include <optional>

namespace dovahlink::application {

///  Stores and exposes the current character-state snapshot.
class ICharacterStateStore {
  public:
    ///  Allows destruction through the interface.
    virtual ~ICharacterStateStore() = default;

    ///  Stores one captured player level, or an unavailable value.
    ///  @param level Captured level, or no value when unavailable.
    virtual void OnLevelCaptured(std::optional<std::int64_t> level) = 0;

    ///  Returns the most recently captured character state.
    [[nodiscard]] virtual CharacterSnapshot CurrentCharacterSnapshot() const = 0;
};

///  Stores the latest captured character state for bridge-lifetime consumers.
///  Access is synchronized because capture and reads may occur on different
///  threads.
class CharacterStateStore final : public ICharacterStateStore {
  public:
    ///  @copydoc ICharacterStateStore::OnLevelCaptured
    void OnLevelCaptured(std::optional<std::int64_t> level) override;

    ///  @copydoc ICharacterStateStore::CurrentCharacterSnapshot
    [[nodiscard]] CharacterSnapshot CurrentCharacterSnapshot() const;

  private:
    ///  Synchronizes access to `snapshot_`.
    mutable std::mutex mutex_;

    ///  Most recently captured character state.
    CharacterSnapshot snapshot_;
};

} //  namespace dovahlink::application
