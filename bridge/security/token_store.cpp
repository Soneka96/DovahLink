#include "security/token_store.hpp"

#include "security/constant_time_compare.hpp"

#include <windows.h>

#include <utility>

namespace dovahlink::security {

namespace {

// Overwrites the buffer's contents before releasing it, using a Windows API
/// Overwrites a byte buffer without performing a potentially throwing
/// reallocation.
void SecureClear(std::vector<std::uint8_t> &buffer) noexcept {
  if (!buffer.empty()) {
    SecureZeroMemory(buffer.data(), buffer.size());
  }
  buffer.clear();
}

} // namespace
TokenStore::TokenStore(std::vector<std::uint8_t> expectedToken,
                       std::chrono::steady_clock::duration timeToLive)
    : token_(std::move(expectedToken)),
      expiresAt_(std::chrono::steady_clock::now() + timeToLive),
      consumed_(token_.empty()) {}

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
    consumed_ = true; // expired tokens are permanently unavailable too.
    return false;
  }
  return true;
}

TokenStore::Reservation::Reservation(TokenStore &store,
                                     std::unique_lock<std::mutex> lock) noexcept
    : store_(&store), lock_(std::move(lock)) {}

TokenStore::Reservation::Reservation(Reservation &&other) noexcept
    : store_(other.store_), lock_(std::move(other.lock_)) {
  other.store_ = nullptr;
}

TokenStore::Reservation &
TokenStore::Reservation::operator=(Reservation &&other) noexcept {
  if (this != &other) {
    lock_ = std::move(other.lock_);
    store_ = other.store_;
    other.store_ = nullptr;
  }
  return *this;
}

void TokenStore::Reservation::Commit() {
  if (store_ == nullptr) {
    return;
  }
  store_->consumed_ = true;
  SecureClear(store_->token_);
  store_ = nullptr;
  lock_.unlock();
}

std::optional<TokenStore::Reservation>
TokenStore::TryReserve(const std::vector<std::uint8_t> &presented) {
  std::unique_lock<std::mutex> lock(mutex_);

  // Known limitation: the availability check below returns before the
  // constant-time compare, so an already-consumed/expired store rejects faster
  // than a live wrong guess -- a timing signal for *store state*, not for token
  // content (the byte-for-byte compare itself, when it runs, remains
  // constant-time). Acceptable for the current loopback-only pairing
  // constraints; revisit if a future pairing design needs this state
  // distinction hidden too.
  if (!IsAvailableLocked()) {
    return std::nullopt;
  }
  if (!ConstantTimeEquals(token_, presented)) {
    return std::nullopt;
  }

  return Reservation(*this, std::move(lock));
}

bool TokenStore::IsAvailable() {
  std::lock_guard<std::mutex> lock(mutex_);
  return IsAvailableLocked();
}

std::optional<std::chrono::seconds> TokenStore::RemainingSeconds() {
  std::lock_guard<std::mutex> lock(mutex_);
  if (!IsAvailableLocked()) {
    return std::nullopt;
  }
  return std::chrono::duration_cast<std::chrono::seconds>(
      expiresAt_ - std::chrono::steady_clock::now());
}

} // namespace dovahlink::security
