#include "security/environment_reader.hpp"

#include "security/hex.hpp"

#include <windows.h>

#include <memory>
#include <utility>

namespace dovahlink::security {

namespace {

//  Overwrites the string's contents before it is destroyed. Mirrors
//  token_store.cpp's SecureClear for std::vector<uint8_t>; the intermediate
//  hex text read from the environment carries the same secret material and
//  should not be left for normal deallocation to reclaim without wiping it
///  Overwrites and clears an intermediate secret string.
void SecureClearString(std::string* value) noexcept {
    if (!value->empty()) {
        SecureZeroMemory(value->data(), value->size());
    }
    value->clear();
}

} //  namespace

std::optional<std::string>
WindowsEnvironmentReader::Read(std::string_view name) const {
    std::string nameStr(name);
    DWORD needed = GetEnvironmentVariableA(nameStr.c_str(), nullptr, 0);
    if (needed == 0) {
        return std::nullopt;
    }
    std::string value(needed, '\0');
    std::unique_ptr<std::string, void (*)(std::string*)> clearValue(
        &value, SecureClearString);
    DWORD written =
        GetEnvironmentVariableA(nameStr.c_str(), value.data(), needed);
    if (written == 0 || written >= needed) {
        return std::nullopt;
    }
    value.resize(written);
    return std::optional<std::string>(std::in_place, value);
}

TokenReadResult ReadTokenFromEnvironment(const IEnvironmentReader& env,
                                         std::string_view variableName) {
    auto raw = env.Read(variableName);
    if (!raw.has_value()) {
        return TokenReadResult{.outcome = TokenReadOutcome::kMissing, .bytes = {}};
    }
    std::unique_ptr<std::string, void (*)(std::string*)> clearRaw(
        &*raw, SecureClearString);
    if (raw->empty()) {
        return TokenReadResult{.outcome = TokenReadOutcome::kMissing, .bytes = {}};
    }
    auto decoded = DecodeHex(*raw);
    if (!decoded.has_value() || decoded->size() != kTokenBytes) {
        return TokenReadResult{.outcome = TokenReadOutcome::kMalformed,
                               .bytes = {}};
    }
    return TokenReadResult{.outcome = TokenReadOutcome::kValid,
                           .bytes = std::move(*decoded)};
}

} //  namespace dovahlink::security
