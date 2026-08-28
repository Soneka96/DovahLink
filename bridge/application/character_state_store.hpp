#pragma once

#include "application/character_snapshot.hpp"

#include <mutex>
#include <optional>

namespace dovahlink::application {

///  Stores and exposes the current character-state snapshot.
class ICharacterStateStore {
  public:
    ///  Allows destruction through the interface.
    virtual ~ICharacterStateStore() = default;

    ///  Stores one captured player level, or an unavailable value, only when
    ///  it differs from the value already stored. Comparing
    ///  `std::optional<std::int64_t>` directly gives the correct
    ///  available/unavailable-transition rule for free: an unavailable
    ///  capture equals a prior unavailable capture (no change), while any
    ///  other differing pair -- including a transition to or from
    ///  unavailable -- is a change.
    ///  @param level Captured level, or no value when unavailable.
    ///  @return `true` when the stored value changed; `false` when it
    ///  already matched `level`.
    virtual bool OnLevelCaptured(std::optional<std::int64_t> level) = 0;

    ///  Returns the most recently captured character state.
    [[nodiscard]] virtual CharacterSnapshot CurrentCharacterSnapshot() const = 0;
};

///  Stores the latest captured character state for bridge-lifetime consumers.
///  Access is synchronized because capture and reads may occur on different
///  threads.
class CharacterStateStore final : public ICharacterStateStore {
  public:
    ///  @copydoc ICharacterStateStore::OnLevelCaptured
    bool OnLevelCaptured(std::optional<std::int64_t> level) override;

    ///  @copydoc ICharacterStateStore::CurrentCharacterSnapshot
    [[nodiscard]] CharacterSnapshot CurrentCharacterSnapshot() const;

  private:
    ///  Synchronizes access to `snapshot_`.
    mutable std::mutex mutex_;

    ///  Most recently captured character state.
    CharacterSnapshot snapshot_;
};

} //  namespace dovahlink::application
