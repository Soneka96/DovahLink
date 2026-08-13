#include "security/token_provider.hpp"

#include "security/hex.hpp"

#include <windows.h>

namespace dovahlink::security {

namespace {

// Overwrites the string's contents before it is destroyed. Mirrors
// token_store.cpp's SecureClear for std::vector<uint8_t>; the intermediate
// hex text read from the environment carries the same secret material and
// should not be left for normal deallocation to reclaim without wiping it
/**
 * @brief Securely clears a string's contents.
 *
 * @param value String whose contents are overwritten and cleared.
 */
void SecureClearString(std::string& value) {
    if (!value.empty()) {
        SecureZeroMemory(value.data(), value.size());
    }
    value.clear();
}

}  // namespace

// The failure path (variable genuinely unset vs. set-but-oversized) is not
// unit-tested beyond the "unset" case a fake can express: GetEnvironmentVariableA's
// two-call size-then-fill protocol has no deterministic seam to force the
// second call to race the environment out from under it. Reviewed by
// inspection, matching security/csprng.cpp's precedent for this kind of thin
/**
 * @brief Reads a Windows environment variable.
 *
 * @param name Name of the environment variable to read.
 * @return The variable's value, or `std::nullopt` if it is unset or cannot be retrieved.
 */
std::optional<std::string> WindowsEnvironmentReader::Read(std::string_view name) const {
    std::string nameStr(name);
    DWORD needed = GetEnvironmentVariableA(nameStr.c_str(), nullptr, 0);
    if (needed == 0) {
        return std::nullopt;
    }
    std::string value(needed, '\0');
    DWORD written = GetEnvironmentVariableA(nameStr.c_str(), value.data(), needed);
    if (written == 0 || written >= needed) {
        return std::nullopt;
    }
    value.resize(written);
    return value;
}

/**
 * @brief Loads and validates a hexadecimal token from an environment variable.
 *
 * @param variableName Name of the environment variable containing the token.
 * @return Decoded token bytes when the value is valid and has the required length; an empty optional otherwise.
 */
std::optional<std::vector<std::uint8_t>> ReadTokenFromEnvironment(const EnvironmentReader& env,
                                                                    std::string_view variableName) {
    auto raw = env.Read(variableName);
    if (!raw.has_value() || raw->empty()) {
        return std::nullopt;
    }
    auto decoded = DecodeHex(*raw);
    SecureClearString(*raw);
    if (!decoded.has_value() || decoded->size() != kTokenBytes) {
        return std::nullopt;
    }
    return decoded;
}

}  // namespace dovahlink::security
