#include "identity/adapter_play_context_state.hpp"

namespace dovahlink::adapter::identity {

std::optional<std::array<std::byte, 16>>
AdapterPlayContextState::CurrentPlayContext() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return playContextId_;
}

void AdapterPlayContextState::SetCurrentPlayContext(
    std::array<std::byte, 16> playContextId) {
    std::lock_guard<std::mutex> lock(mutex_);
    playContextId_ = playContextId;
}

void AdapterPlayContextState::ClearCurrentPlayContext() {
    std::lock_guard<std::mutex> lock(mutex_);
    playContextId_.reset();
}

} //  namespace dovahlink::adapter::identity
