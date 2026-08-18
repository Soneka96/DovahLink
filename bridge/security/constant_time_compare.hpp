#pragma once

#include <cstdint>
#include <vector>

namespace dovahlink::security {

/// Compares equal-length byte sequences without early exit on content, avoiding a timing
/// side-channel on which byte first differs. Returns `false` immediately for mismatched lengths,
/// since length alone is not the secret this guards.
[[nodiscard]] bool ConstantTimeEquals(const std::vector<std::uint8_t>& a, const std::vector<std::uint8_t>& b);

}  // namespace dovahlink::security
