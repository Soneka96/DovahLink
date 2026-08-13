#pragma once

#include <cstdint>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace dovahlink::security {

/// Encodes bytes as lowercase hexadecimal text, using two characters per byte.
[[nodiscard]] std::string EncodeHex(const std::vector<std::uint8_t>& bytes);

/// Decodes non-empty even-length hexadecimal text without partial results.
/// Accepts uppercase and lowercase digits and returns `std::nullopt` for invalid input.
[[nodiscard]] std::optional<std::vector<std::uint8_t>> DecodeHex(std::string_view hex);

}  // namespace dovahlink::security
