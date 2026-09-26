#include "window_lifecycle_state.h"

bool WindowLifecycleState::BeginClose(std::uint64_t generation) noexcept {
    if (generation == 0 || pending_close_generation_.has_value() ||
        close_cleanup_completed_) {
        return false;
    }
    pending_close_generation_ = generation;
    return true;
}

bool WindowLifecycleState::CompleteClose(std::uint64_t generation) noexcept {
    if (!pending_close_generation_.has_value() ||
        pending_close_generation_.value() != generation) {
        return false;
    }
    pending_close_generation_.reset();
    close_cleanup_completed_ = true;
    return true;
}

bool WindowLifecycleState::HasPendingClose() const noexcept {
    return pending_close_generation_.has_value();
}

bool WindowLifecycleState::CloseCleanupCompleted() const noexcept {
    return close_cleanup_completed_;
}

std::optional<std::uint64_t>
WindowLifecycleState::PendingCloseGeneration() const noexcept {
    return pending_close_generation_;
}

bool WindowLifecycleState::BeginSessionEndCleanup(bool sessionEnded) noexcept {
    if (!sessionEnded || session_end_cleanup_requested_) {
        return false;
    }
    session_end_cleanup_requested_ = true;
    return true;
}

void WindowLifecycleState::ResetForWindow() noexcept {
    pending_close_generation_.reset();
    session_end_cleanup_requested_ = false;
    close_cleanup_completed_ = false;
}
