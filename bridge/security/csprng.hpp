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
[[nodiscard]] std::optional<std::vector<std::uint8_t>>
GenerateRandomBytes(std::size_t size);

/// Generates a 128-bit cryptographically random opaque identifier as hex text.
[[nodiscard]] std::optional<std::string> GenerateOpaqueId();

/// Generates a candidate numeric code of exactly `digits` decimal digits, using
/// the system CSPRNG and zero-padding on the left. `digits` must be between 1
/// and 9 inclusive -- the underlying 32-bit value cannot represent a wider
/// range; this is a precondition on trusted internal callers, not validated
/// against external input.
[[nodiscard]] std::optional<std::string>
GenerateNumericCode(std::size_t digits);

} // namespace dovahlink::security
