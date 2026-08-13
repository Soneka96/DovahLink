#include "security/token_store.hpp"

#include <windows.h>

#include <utility>

namespace dovahlink::security {

namespace {

// Constant-time comparison: always inspects every byte of both buffers
// regardless of where they first differ, avoiding a timing side-channel that
/**
 * @brief Compares two byte sequences using a constant-time byte comparison.
 *
 * @param a First byte sequence.
 * @param b Second byte sequence.
 * @return `true` if both sequences have the same length and contents, `false` otherwise.
 */
bool ConstantTimeEquals(const std::vector<std::uint8_t>& a, const std::vector<std::uint8_t>& b) {
    if (a.size() != b.size()) {
        return false;
    }
    std::uint8_t diff = 0;
    for (std::size_t i = 0; i < a.size(); ++i) {
        diff |= static_cast<std::uint8_t>(a[i] ^ b[i]);
    }
    return diff == 0;
}

// Overwrites the buffer's contents before releasing it, using a Windows API
/**
 * @brief Securely clears a byte buffer and releases its storage.
 *
 * @param buffer The byte buffer to erase and clear.
 */
void SecureClear(std::vector<std::uint8_t>& buffer) {
    if (!buffer.empty()) {
        SecureZeroMemory(buffer.data(), buffer.size());
    }
    buffer.clear();
    buffer.shrink_to_fit();
}

}  /**
     * @brief Initializes a token store with an expiration duration.
     *
     * @param expectedToken Token required for successful consumption.
     * @param timeToLive Duration for which the token remains available.
     */

TokenStore::TokenStore(std::vector<std::uint8_t> expectedToken,
                        std::chrono::steady_clock::duration timeToLive)
    : token_(std::move(expectedToken)), expiresAt_(std::chrono::steady_clock::now() + timeToLive) {}

/**
 * @brief Securely clears the stored token when the token store is destroyed.
 */
TokenStore::~TokenStore() {
    std::lock_guard<std::mutex> lock(mutex_);
    SecureClear(token_);
}

/**
 * @brief Determines whether the token remains available for consumption.
 *
 * Expired tokens are securely cleared and permanently marked unavailable.
 *
 * @return `true` if the token is available, `false` if it has been consumed or expired.
 */
bool TokenStore::IsAvailableLocked() {
    if (consumed_) {
        return false;
    }
    if (std::chrono::steady_clock::now() >= expiresAt_) {
        SecureClear(token_);
        consumed_ = true;  // expired tokens are permanently unavailable too.
        return false;
    }
    return true;
}

/**
 * @brief Attempts to consume the stored token using the presented token.
 *
 * @param presented Token to compare with the stored token.
 * @return `true` if the token matches and is consumed, `false` if the token is unavailable or does not match.
 */
bool TokenStore::TryConsume(const std::vector<std::uint8_t>& presented) {
    std::lock_guard<std::mutex> lock(mutex_);

    // ponytail: the availability check below returns before the constant-time
    // compare, so an already-consumed/expired store rejects faster than a live
    // wrong guess -- a timing signal for *store state*, not for token content
    // (the byte-for-byte compare itself, when it runs, remains constant-time).
    // Acceptable for the Phase 1 proof (loopback-only, 5 attempts/60s, single
    // dev token; see TASK.md); revisit if a future pairing design needs this
    // hidden too.
    if (!IsAvailableLocked()) {
        return false;
    }
    if (!ConstantTimeEquals(token_, presented)) {
        return false;
    }

    consumed_ = true;
    SecureClear(token_);
    return true;
}

/**
 * @brief Determines whether the stored token is available for consumption.
 *
 * @return `true` if the token has not been consumed or expired, `false` otherwise.
 */
bool TokenStore::IsAvailable() {
    std::lock_guard<std::mutex> lock(mutex_);
    return IsAvailableLocked();
}

}  // namespace dovahlink::security
