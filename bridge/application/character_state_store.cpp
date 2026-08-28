#include "application/character_state_store.hpp"

namespace dovahlink::application {

bool CharacterStateStore::OnLevelCaptured(std::optional<std::int64_t> level) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (hasCaptured_ && snapshot_.level == level) {
        return false;
    }
    hasCaptured_ = true;
    snapshot_.level = level;
    return true;
}

CharacterSnapshot CharacterStateStore::CurrentCharacterSnapshot() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return snapshot_;
}

} //  namespace dovahlink::application
