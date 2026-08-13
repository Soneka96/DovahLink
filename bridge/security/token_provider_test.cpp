#include "security/token_provider.hpp"

#include <catch2/catch_test_macros.hpp>

#include <optional>
#include <string>
#include <unordered_map>

using dovahlink::security::EnvironmentReader;
using dovahlink::security::kTokenBytes;
using dovahlink::security::ReadTokenFromEnvironment;

namespace {

constexpr const char* kVarName = "DOVAHLINK_BRIDGE_TOKEN";

// 32 bytes (64 hex characters) of a representative, obviously-fake value.
// Mixed case on purpose, to prove both cases decode.
constexpr const char* kValidHexToken =
    "0123456789abcdefABCDEF00112233445566778899aabbccddeeff0011223344";

/// Stores named environment values for token-provider tests.
class FakeEnvironmentReader : public EnvironmentReader {
public:
    /// @copydoc EnvironmentReader::Read
    [[nodiscard]] std::optional<std::string> Read(std::string_view name) const override {
        auto it = values_.find(std::string(name));
        if (it == values_.end()) {
            return std::nullopt;
        }
        return it->second;
    }

    /// Sets or replaces one environment value.
    void Set(std::string name, std::string value) { values_[std::move(name)] = std::move(value); }

private:
    /// Test environment values keyed by variable name.
    std::unordered_map<std::string, std::string> values_;
};

}  // namespace

TEST_CASE("ReadTokenFromEnvironment decodes a valid 64-character hex token to 32 bytes",
          "[security][token_provider]") {
    FakeEnvironmentReader env;
    env.Set(kVarName, kValidHexToken);

    auto token = ReadTokenFromEnvironment(env, kVarName);

    REQUIRE(token.has_value());
    CHECK(token->size() == kTokenBytes);
    CHECK((*token)[0] == 0x01);
    CHECK((*token)[1] == 0x23);
    CHECK((*token)[8] == 0xAB);  // from the uppercase "AB" pair
    CHECK((*token)[31] == 0x44);
}

TEST_CASE("ReadTokenFromEnvironment returns nullopt when the variable is unset",
          "[security][token_provider]") {
    FakeEnvironmentReader env;
    CHECK_FALSE(ReadTokenFromEnvironment(env, kVarName).has_value());
}

TEST_CASE("ReadTokenFromEnvironment returns nullopt for an empty value", "[security][token_provider]") {
    FakeEnvironmentReader env;
    env.Set(kVarName, "");
    CHECK_FALSE(ReadTokenFromEnvironment(env, kVarName).has_value());
}

TEST_CASE("ReadTokenFromEnvironment returns nullopt for odd-length hex", "[security][token_provider]") {
    FakeEnvironmentReader env;
    env.Set(kVarName, "abc");
    CHECK_FALSE(ReadTokenFromEnvironment(env, kVarName).has_value());
}

TEST_CASE("ReadTokenFromEnvironment returns nullopt for a non-hex character",
          "[security][token_provider]") {
    FakeEnvironmentReader env;
    // 64 characters, but "zz" at the start is not valid hex.
    env.Set(kVarName, std::string("zz") + std::string(62, '0'));
    CHECK_FALSE(ReadTokenFromEnvironment(env, kVarName).has_value());
}

TEST_CASE("ReadTokenFromEnvironment returns nullopt for a token that is too short",
          "[security][token_provider]") {
    FakeEnvironmentReader env;
    // 62 hex characters decodes to 31 bytes, one short of the required 32.
    env.Set(kVarName, std::string(62, '0'));
    CHECK_FALSE(ReadTokenFromEnvironment(env, kVarName).has_value());
}

TEST_CASE("ReadTokenFromEnvironment returns nullopt for a token that is too long",
          "[security][token_provider]") {
    FakeEnvironmentReader env;
    // 66 hex characters decodes to 33 bytes, one over the required 32.
    env.Set(kVarName, std::string(66, '0'));
    CHECK_FALSE(ReadTokenFromEnvironment(env, kVarName).has_value());
}

TEST_CASE("ReadTokenFromEnvironment only reads the variable it is asked for",
          "[security][token_provider]") {
    FakeEnvironmentReader env;
    env.Set("SOME_OTHER_VARIABLE", kValidHexToken);
    CHECK_FALSE(ReadTokenFromEnvironment(env, kVarName).has_value());
}
