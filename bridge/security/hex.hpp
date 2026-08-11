#pragma once

#include <cstdint>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace dovahlink::security {

// Encodes `bytes` as lowercase hex, two characters per byte.
[[nodiscard]] std::string EncodeHex(const std::vector<std::uint8_t>& bytes);

// Decodes `hex` as lowercase-or-uppercase hex. Returns nullopt for empty
// input, odd length, or any non-hex character; never partially decodes.
[[nodiscard]] std::optional<std::vector<std::uint8_t>> DecodeHex(std::string_view hex);

}  // namespace dovahlink::security
