#pragma once

#include <chrono>
#include <cstdint>
#include <mutex>
#include <vector>

namespace dovahlink::security {

// A one-time local connection token that can be consumed by exactly one caller.
// See ai/context/protocol/security.md and TASK.md. Thread-safe: TryConsume may
// be called concurrently by multiple connection attempts; exactly one succeeds.
class TokenStore {
public:
    // `expectedToken` is the token value the bridge will accept; it is moved
    // in and cleared by this store once consumed or expired. The caller's own
    // copy (e.g. an environment-variable string) remains the caller's
    // responsibility to clear.
    explicit TokenStore(std::vector<std::uint8_t> expectedToken,
                         std::chrono::steady_clock::duration timeToLive = std::chrono::minutes(5));

    // Clears any remaining token material even if it was never consumed or
    // checked for expiry (e.g. clean shutdown before either happens).
    ~TokenStore();

    TokenStore(const TokenStore&) = delete;
    TokenStore& operator=(const TokenStore&) = delete;

    // Compares `presented` against the expected token in constant time. On the
    // first call where `presented` matches, before expiry, and the token has
    // not already been consumed, atomically marks it consumed, clears the
    // stored token material, and returns true. Every other case (mismatch,
    // expired, already consumed) returns false and never marks the token
    // consumed on a mismatch. Safe to call concurrently.
    [[nodiscard]] bool TryConsume(const std::vector<std::uint8_t>& presented);

    // True until the token has been consumed or has expired. Checking expiry
    // clears the stored token material as a side effect, matching "clear
    // token material after consumption or expiry" (ai/context/protocol/security.md).
    [[nodiscard]] bool IsAvailable();

private:
    // Must be called while mutex_ is held. Clears the token and marks it
    // consumed if expiry is detected.
    bool IsAvailableLocked();

    std::mutex mutex_;
    std::vector<std::uint8_t> token_;  // cleared once consumed or expired.
    std::chrono::steady_clock::time_point expiresAt_;
    bool consumed_ = false;
};

}  // namespace dovahlink::security
