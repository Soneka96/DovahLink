#include "security/hex.hpp"

namespace dovahlink::security {

namespace {

/// Converts one hexadecimal digit to its numeric nibble value.
std::optional<std::uint8_t> HexNibble(char c) {
    if (c >= '0' && c <= '9') {
        return static_cast<std::uint8_t>(c - '0');
    }
    if (c >= 'a' && c <= 'f') {
        return static_cast<std::uint8_t>(c - 'a' + 10);
    }
    if (c >= 'A' && c <= 'F') {
        return static_cast<std::uint8_t>(c - 'A' + 10);
    }
    return std::nullopt;
}

}
std::string EncodeHex(const std::vector<std::uint8_t>& bytes) {
    static constexpr char kDigits[] = "0123456789abcdef";
    std::string hex;
    hex.reserve(bytes.size() * 2);
    for (std::uint8_t byte : bytes) {
        hex.push_back(kDigits[(byte >> 4) & 0x0F]);
        hex.push_back(kDigits[byte & 0x0F]);
    }
    return hex;
}

std::optional<std::vector<std::uint8_t>> DecodeHex(std::string_view hex) {
    if (hex.empty() || hex.size() % 2 != 0) {
        return std::nullopt;
    }
    std::vector<std::uint8_t> bytes;
    bytes.reserve(hex.size() / 2);
    for (std::size_t i = 0; i < hex.size(); i += 2) {
        auto high = HexNibble(hex[i]);
        auto low = HexNibble(hex[i + 1]);
        if (!high.has_value() || !low.has_value()) {
            return std::nullopt;
        }
        bytes.push_back(static_cast<std::uint8_t>((*high << 4) | *low));
    }
    return bytes;
}

}  // namespace dovahlink::security
