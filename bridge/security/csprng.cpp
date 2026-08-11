#include "security/csprng.hpp"

#include <bcrypt.h>

namespace dovahlink::security {

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

}  // namespace dovahlink::security
