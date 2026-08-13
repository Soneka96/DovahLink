#include "application/character_state_store.hpp"

namespace dovahlink::application {

/**
 * @brief Records the character's captured level.
 *
 * @param level Captured level, or no value when a level was not captured.
 */
void CharacterStateStore::OnLevelCaptured(std::optional<std::int64_t> level) {
    std::lock_guard<std::mutex> lock(mutex_);
    snapshot_.level = level;
}

/**
 * @brief Gets the current character snapshot.
 *
 * @return CharacterSnapshot Copy of the current character state.
 */
CharacterSnapshot CharacterStateStore::CurrentCharacterSnapshot() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return snapshot_;
}

}  // namespace dovahlink::application
