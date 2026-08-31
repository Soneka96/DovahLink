#include "ipc/adapter_ipc_hmac.hpp"

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>

#include <bcrypt.h>

#include <algorithm>
#include <span>
#include <stdexcept>

namespace dovahlink::adapter::ipc {

namespace {

///  Closes a BCrypt algorithm provider handle on scope exit.
class ScopedAlgorithmHandle {
public:
  ///  Takes ownership of `handle`.
  explicit ScopedAlgorithmHandle(BCRYPT_ALG_HANDLE handle) : handle_(handle) {}

  ///  Closes the owned handle, if any.
  ~ScopedAlgorithmHandle() {
    if (handle_ != nullptr) {
      BCryptCloseAlgorithmProvider(handle_, 0);
    }
  }

  ScopedAlgorithmHandle(const ScopedAlgorithmHandle &) = delete;
  ScopedAlgorithmHandle &operator=(const ScopedAlgorithmHandle &) = delete;

private:
  ///  The owned algorithm provider handle, or `nullptr`.
  BCRYPT_ALG_HANDLE handle_;
};

///  Destroys a BCrypt hash object handle on scope exit.
class ScopedHashHandle {
public:
  ///  Takes ownership of `handle`.
  explicit ScopedHashHandle(BCRYPT_HASH_HANDLE handle) : handle_(handle) {}

  ///  Destroys the owned handle, if any.
  ~ScopedHashHandle() {
    if (handle_ != nullptr) {
      BCryptDestroyHash(handle_);
    }
  }

  ScopedHashHandle(const ScopedHashHandle &) = delete;
  ScopedHashHandle &operator=(const ScopedHashHandle &) = delete;

private:
  ///  The owned hash object handle, or `nullptr`.
  BCRYPT_HASH_HANDLE handle_;
};

///  BCrypt's byte-buffer parameters are `PUCHAR` (non-const) even for
///  read-only input such as a hash key or hashed data; the API never writes
///  through them. This cast is the standard, safe way to call such Windows
///  APIs from a `const` source span.
PUCHAR AsMutableInputBuffer(std::span<const std::byte> bytes) {
  return reinterpret_cast<PUCHAR>(const_cast<std::byte *>(bytes.data()));
}

} //  namespace

std::array<std::byte, kIpcHostProofBytes>
ComputeIpcHmacSha256(std::span<const std::byte> key,
                     std::span<const std::byte> message) {
  BCRYPT_ALG_HANDLE algorithmHandle = nullptr;
  if (!BCRYPT_SUCCESS(
          BCryptOpenAlgorithmProvider(&algorithmHandle, BCRYPT_SHA256_ALGORITHM,
                                      nullptr, BCRYPT_ALG_HANDLE_HMAC_FLAG))) {
    throw std::runtime_error(
        "BCryptOpenAlgorithmProvider failed for HMAC-SHA256.");
  }
  ScopedAlgorithmHandle scopedAlgorithm(algorithmHandle);

  BCRYPT_HASH_HANDLE hashHandle = nullptr;
  if (!BCRYPT_SUCCESS(BCryptCreateHash(algorithmHandle, &hashHandle, nullptr, 0,
                                       AsMutableInputBuffer(key),
                                       static_cast<ULONG>(key.size()), 0))) {
    throw std::runtime_error("BCryptCreateHash failed for HMAC-SHA256.");
  }
  ScopedHashHandle scopedHash(hashHandle);

  if (!message.empty() &&
      !BCRYPT_SUCCESS(BCryptHashData(hashHandle, AsMutableInputBuffer(message),
                                     static_cast<ULONG>(message.size()), 0))) {
    throw std::runtime_error("BCryptHashData failed for HMAC-SHA256.");
  }

  std::array<std::byte, kIpcHostProofBytes> digest{};
  if (!BCRYPT_SUCCESS(BCryptFinishHash(hashHandle,
                                       reinterpret_cast<PUCHAR>(digest.data()),
                                       static_cast<ULONG>(digest.size()), 0))) {
    throw std::runtime_error("BCryptFinishHash failed for HMAC-SHA256.");
  }

  return digest;
}

std::array<std::byte, kIpcHostProofMessageBytes> BuildHostProofMessage(
    const std::array<std::byte, kIpcChallengeBytes> &challenge,
    std::uint64_t correlationId,
    const std::array<std::byte, 16> &adapterInstanceId,
    const std::array<std::byte, kIpcOwnerLifetimeIdBytes> &ownerLifetimeId) {
  constexpr std::size_t kCorrelationIdOffset = kIpcChallengeBytes;
  constexpr std::size_t kAdapterInstanceIdOffset = kCorrelationIdOffset + 8;
  constexpr std::size_t kOwnerLifetimeIdOffset = kAdapterInstanceIdOffset + 16;

  std::array<std::byte, kIpcHostProofMessageBytes> message{};
  std::ranges::copy(challenge, message.begin());
  for (int index = 0; index < 8; ++index) {
    message[kCorrelationIdOffset + static_cast<std::size_t>(index)] =
        static_cast<std::byte>((correlationId >> (8 * index)) & 0xFF);
  }
  std::ranges::copy(adapterInstanceId,
                    message.begin() +
                        static_cast<std::ptrdiff_t>(kAdapterInstanceIdOffset));
  std::ranges::copy(ownerLifetimeId,
                    message.begin() +
                        static_cast<std::ptrdiff_t>(kOwnerLifetimeIdOffset));
  return message;
}

bool ConstantTimeEqual(std::span<const std::byte> a,
                       std::span<const std::byte> b) {
  if (a.size() != b.size()) {
    return false;
  }

  std::byte accumulatedDifference{0};
  for (std::size_t index = 0; index < a.size(); ++index) {
    accumulatedDifference |= (a[index] ^ b[index]);
  }
  return accumulatedDifference == std::byte{0};
}

std::array<std::byte, kIpcChallengeBytes> GenerateIpcChallenge() {
  std::array<std::byte, kIpcChallengeBytes> challenge{};
  if (!BCRYPT_SUCCESS(
          BCryptGenRandom(nullptr, reinterpret_cast<PUCHAR>(challenge.data()),
                          static_cast<ULONG>(challenge.size()),
                          BCRYPT_USE_SYSTEM_PREFERRED_RNG))) {
    throw std::runtime_error("BCryptGenRandom failed for the Hello challenge.");
  }
  return challenge;
}

} //  namespace dovahlink::adapter::ipc
