#include "security/csprng.hpp"

#include "security/hex.hpp"

#include <windows.h>

#include <bcrypt.h>

namespace dovahlink::security {

/**
 * @brief Generates cryptographically secure random bytes.
 *
 * @param size Number of bytes to generate.
 * @return A byte vector of the requested size, or `std::nullopt` if generation fails.
 */
std::optional<std::vector<std::uint8_t>> GenerateRandomBytes(std::size_t size) {
    std::vector<std::uint8_t> buffer(size);
    if (size == 0) {
        return buffer;
    }

    NTSTATUS status = BCryptGenRandom(nullptr, buffer.data(), static_cast<ULONG>(size),
                                       BCRYPT_USE_SYSTEM_PREFERRED_RNG);
    if (!BCRYPT_SUCCESS(status)) {
        return std::nullopt;
    }
    return buffer;
}

/**
 * @brief Generates an opaque identifier from cryptographically secure random bytes.
 *
 * @return std::string Hexadecimal encoding of 16 random bytes, or `std::nullopt`
 *         if random-byte generation fails.
 */
std::optional<std::string> GenerateOpaqueId() {
    constexpr std::size_t kOpaqueIdBytes = 16;
    auto bytes = GenerateRandomBytes(kOpaqueIdBytes);
    if (!bytes.has_value()) {
        return std::nullopt;
    }
    return EncodeHex(*bytes);
}

}  // namespace dovahlink::security
