#pragma once

#include <chrono>
#include <cstdint>
#include <mutex>
#include <vector>

namespace dovahlink::security {

/// Thread-safe one-time token store with expiry and secure memory clearing.
class TokenStore {
public:
    /// Stores the expected token and starts its time-to-live countdown.
    explicit TokenStore(std::vector<std::uint8_t> expectedToken,
                        std::chrono::steady_clock::duration timeToLive = std::chrono::minutes(5));

    /// Clears any remaining token material.
    ~TokenStore();

    /// Prevents copying token material between stores.
    TokenStore(const TokenStore&) = delete;
    /// Prevents assigning token material between stores.
    TokenStore& operator=(const TokenStore&) = delete;

    /// Compares and atomically consumes a matching, unexpired token.
    /// Comparison is constant-time for equal-length values; mismatches,
    /// expiry, and prior consumption return `false` without consuming it.
    [[nodiscard]] bool TryConsume(const std::vector<std::uint8_t>& presented);

    /// Reports whether the token remains available, clearing it on expiry.
    [[nodiscard]] bool IsAvailable();

private:
    /// Checks availability and clears expired token material while locked.
    bool IsAvailableLocked();

    /// Serializes access to token state.
    std::mutex mutex_;
    /// Stored token material, cleared after consumption or expiry.
    std::vector<std::uint8_t> token_;
    /// Monotonic expiration deadline.
    std::chrono::steady_clock::time_point expiresAt_;
    /// Whether the token has been consumed or expired.
    bool consumed_ = false;
};

}  // namespace dovahlink::security
