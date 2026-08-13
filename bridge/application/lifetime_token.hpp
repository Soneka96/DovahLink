#pragma once

#include <atomic>

namespace dovahlink::application {

// An independently-owned token letting a callback or transport completion
// check whether the coordinator that registered it is still alive, without
// needing to touch coordinator state directly. See
// ai/context/skse/architecture.md: "Transport completion callbacks must use
// an in-flight counter or a lifetime token owned independently of the
// coordinator so no callback can access destroyed coordinator or transport
// state. The token remains valid until every callback has returned."
//
// A completion handler captures a copy of the shared_ptr<LifetimeToken>
// (never a raw pointer or reference to the coordinator) when it is
// scheduled, and checks IsValid() before touching anything coordinator- or
// transport-owned. The coordinator calls Invalidate() during shutdown,
// before any of that state is destroyed.
//
// Invalidation is one-way and permanent: this coordinator starts once and
// shuts down once for the plugin's whole lifetime
// (ai/context/skse/architecture.md), so there is no later "generation" of
// activity to distinguish or revive -- once invalid, always invalid.
class LifetimeToken {
public:
    /**
 * @brief Creates a valid lifetime token.
 */
LifetimeToken() = default;

    /**
 * @brief Permanently invalidates the lifetime token.
 *
 * Repeated calls have no additional effect.
 */
    void Invalidate() { valid_.store(false, std::memory_order_release); }

    /**
 * @brief Determines whether the lifetime token remains valid.
 *
 * @return `true` until the token is invalidated, `false` afterward.
 */
    [[nodiscard]] bool IsValid() const { return valid_.load(std::memory_order_acquire); }

private:
    std::atomic<bool> valid_{true};
};

}  // namespace dovahlink::application
