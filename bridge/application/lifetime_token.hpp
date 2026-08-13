#pragma once

#include <atomic>

namespace dovahlink::application {

/// Guards callbacks from accessing state after the owning coordinator shuts down.
class LifetimeToken {
public:
    /// Creates a valid lifetime token.
    LifetimeToken() = default;

    /// Permanently invalidates the token; repeated calls are harmless.
    void Invalidate() { valid_.store(false, std::memory_order_release); }

    /// Reports whether callbacks may still access the associated state.
    /// @return `true` until the token is invalidated.
    [[nodiscard]] bool IsValid() const { return valid_.load(std::memory_order_acquire); }

private:
    /// Atomic validity state shared with callback readers.
    std::atomic<bool> valid_{true};
};

}  // namespace dovahlink::application
