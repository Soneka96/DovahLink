#include "application/character_state_store.hpp"

namespace dovahlink::application {

void CharacterStateStore::OnLevelCaptured(std::optional<std::int64_t> level) {
    std::lock_guard<std::mutex> lock(mutex_);
    snapshot_.level = level;
}

CharacterSnapshot CharacterStateStore::CurrentCharacterSnapshot() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return snapshot_;
}

} //  namespace dovahlink::application
