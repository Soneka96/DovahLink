#include "identity/adapter_play_context_state.hpp"

namespace dovahlink::adapter::identity {

std::array<std::byte, 16> AdapterPlayContextState::CurrentPlayContext() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return playContextId_;
}

void AdapterPlayContextState::SetCurrentPlayContext(
    std::array<std::byte, 16> playContextId) {
    std::lock_guard<std::mutex> lock(mutex_);
    playContextId_ = playContextId;
}

} //  namespace dovahlink::adapter::identity
