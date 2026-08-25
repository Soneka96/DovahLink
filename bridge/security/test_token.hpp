#pragma once

namespace dovahlink::security {

/// 32 bytes (64 hex characters) of a representative, obviously-fake token value
/// shared by tests across the bridge. Mixed case on purpose, to prove both
/// cases decode. Test-only fixture; never used to authenticate anything real.
inline constexpr const char *kValidHexToken =
    "0123456789abcdefABCDEF00112233445566778899aabbccddeeff0011223344";

} // namespace dovahlink::security
