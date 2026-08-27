#pragma once

#include "shared/enums.hpp"

#include <cstdint>
#include <vector>

namespace dovahlink::security {

///  Result of reading and hex-decoding the developer-authentication token from
///  an environment value.
struct TokenReadResult {
    ///  Which of the three documented outcomes occurred.
    TokenReadOutcome outcome;
    ///  Decoded token bytes; empty unless `outcome == TokenReadOutcome::kValid`.
    std::vector<std::uint8_t> bytes;
};

} //  namespace dovahlink::security
