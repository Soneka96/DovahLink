#include "ipc/adapter_ipc_hmac.hpp"

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>

#include <bcrypt.h>

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

} //  namespace dovahlink::adapter::ipc
