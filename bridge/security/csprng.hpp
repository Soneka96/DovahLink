#pragma once

#include <cstddef>
#include <cstdint>
#include <optional>
#include <string>
#include <vector>

namespace dovahlink::security {

/// Generates cryptographic random bytes using the Windows system RNG.
/// Returns `std::nullopt` when the underlying CNG call fails; callers must not
/// replace that failure with a weaker random source.
[[nodiscard]] std::optional<std::vector<std::uint8_t>> GenerateRandomBytes(std::size_t size);

/// Generates a 128-bit cryptographically random opaque identifier as hex text.
[[nodiscard]] std::optional<std::string> GenerateOpaqueId();

}  // namespace dovahlink::security
