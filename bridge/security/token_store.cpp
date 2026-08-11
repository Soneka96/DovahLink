#include "security/token_store.hpp"

#include <windows.h>

#include <utility>

namespace dovahlink::security {

namespace {

// Constant-time comparison: always inspects every byte of both buffers
// regardless of where they first differ, avoiding a timing side-channel that
// could reveal how many leading bytes of a guess were correct.
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
// specifically guaranteed not to be optimized away as a dead store.
void SecureClear(std::vector<std::uint8_t>& buffer) {
    if (!buffer.empty()) {
        SecureZeroMemory(buffer.data(), buffer.size());
    }
    buffer.clear();
    buffer.shrink_to_fit();
}

}  // namespace

TokenStore::TokenStore(std::vector<std::uint8_t> expectedToken,
                        std::chrono::steady_clock::duration timeToLive)
    : token_(std::move(expectedToken)), expiresAt_(std::chrono::steady_clock::now() + timeToLive) {}

TokenStore::~TokenStore() {
    std::lock_guard<std::mutex> lock(mutex_);
    SecureClear(token_);
}

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

bool TokenStore::IsAvailable() {
    std::lock_guard<std::mutex> lock(mutex_);
    return IsAvailableLocked();
}

}  // namespace dovahlink::security
