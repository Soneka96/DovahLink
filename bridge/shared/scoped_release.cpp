#include "shared/scoped_release.hpp"

#include <utility>

namespace dovahlink::shared {

ScopedRelease::ScopedRelease(std::function<void()> action) noexcept
    : action_(std::move(action)) {}

ScopedRelease::ScopedRelease(ScopedRelease&& other) noexcept
    : action_(std::move(other.action_)) {
    other.action_ = nullptr;
}

ScopedRelease& ScopedRelease::operator=(ScopedRelease&& other) noexcept {
    if (this != &other) {
        Trigger();
        action_ = std::move(other.action_);
        other.action_ = nullptr;
    }
    return *this;
}

ScopedRelease::~ScopedRelease() { Trigger(); }

void ScopedRelease::Trigger() noexcept {
    if (action_) {
        auto action = std::move(action_);
        action_ = nullptr;
        action();
    }
}

void ScopedRelease::Dismiss() noexcept { action_ = nullptr; }

} //  namespace dovahlink::shared
