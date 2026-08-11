#pragma once

#include <cstddef>
#include <cstdint>
#include <optional>
#include <vector>

namespace dovahlink::security {

// Returns `size` cryptographically random bytes sourced from Windows CNG
// (BCryptGenRandom with BCRYPT_USE_SYSTEM_PREFERRED_RNG), or nullopt if the
// underlying CNG call fails. Never uses std::random_device as the security
// source (see ai/context/protocol/security.md). Callers that need security
// material (tokens, session IDs) must treat nullopt as a fatal security
// failure and must not substitute a weaker random source.
//
// The nullopt path is not unit-tested: it requires the underlying CNG call to
// fail, which cannot be forced deterministically without a mockable seam this
// thin, single-call wrapper does not have. Reviewed by inspection instead.
[[nodiscard]] std::optional<std::vector<std::uint8_t>> GenerateRandomBytes(std::size_t size);

}  // namespace dovahlink::security
