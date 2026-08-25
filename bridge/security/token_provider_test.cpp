#include "security/token_provider.hpp"

#include "security/test_token.hpp"

#include <catch2/catch_test_macros.hpp>

#include <windows.h>

#include <memory>
#include <optional>
#include <string>
#include <unordered_map>

using dovahlink::security::EnvironmentReader;
using dovahlink::security::kTokenBytes;
using dovahlink::security::kValidHexToken;
using dovahlink::security::ReadTokenFromEnvironment;
using dovahlink::security::TokenReadOutcome;
using dovahlink::security::WindowsEnvironmentReader;

namespace {

constexpr const char* kVarName = "DOVAHLINK_BRIDGE_TOKEN";

///  Stores named environment values for token-provider tests.
class FakeEnvironmentReader : public EnvironmentReader {
  public:
    ///  @copydoc EnvironmentReader::Read
    [[nodiscard]] std::optional<std::string>
    Read(std::string_view name) const override {
        auto it = values_.find(std::string(name));
        if (it == values_.end()) {
            return std::nullopt;
        }
        return it->second;
    }

    ///  Sets or replaces one environment value.
    void Set(std::string name, std::string value) {
        values_[std::move(name)] = std::move(value);
    }

  private:
    ///  Test environment values keyed by variable name.
    std::unordered_map<std::string, std::string> values_;
};

} //  namespace

TEST_CASE(
    "WindowsEnvironmentReader returns the exact process environment value",
    "[security][token_provider]") {
    constexpr const char* kReaderTestVar = "DOVAHLINK_TOKEN_PROVIDER_READER_TEST";
    REQUIRE(SetEnvironmentVariableA(kReaderTestVar, "reader-value") != 0);
    std::unique_ptr<const char, void (*)(const char*)> clearVariable(
        kReaderTestVar, [](const char* name) noexcept {
            (void)SetEnvironmentVariableA(name, nullptr);
        });

    WindowsEnvironmentReader env;
    auto value = env.Read(kReaderTestVar);

    REQUIRE(value.has_value());
    CHECK(*value == "reader-value");
}

TEST_CASE("WindowsEnvironmentReader returns nullopt for an unset process "
          "environment variable",
          "[security][token_provider]") {
    constexpr const char* kReaderTestVar =
        "DOVAHLINK_TOKEN_PROVIDER_MISSING_TEST";
    REQUIRE(SetEnvironmentVariableA(kReaderTestVar, nullptr) != 0);

    WindowsEnvironmentReader env;
    CHECK_FALSE(env.Read(kReaderTestVar).has_value());
}

TEST_CASE("ReadTokenFromEnvironment decodes a valid 64-character hex token to "
          "32 bytes",
          "[security][token_provider]") {
    FakeEnvironmentReader env;
    env.Set(kVarName, kValidHexToken);

    auto result = ReadTokenFromEnvironment(env, kVarName);

    CHECK(result.outcome == TokenReadOutcome::kValid);
    REQUIRE(result.bytes.size() == kTokenBytes);
    CHECK(result.bytes[0] == 0x01);
    CHECK(result.bytes[1] == 0x23);
    CHECK(result.bytes[8] == 0xAB); //  from the uppercase "AB" pair
    CHECK(result.bytes[31] == 0x44);
}

TEST_CASE("ReadTokenFromEnvironment treats an unset variable as Missing",
          "[security][token_provider]") {
    FakeEnvironmentReader env;
    auto result = ReadTokenFromEnvironment(env, kVarName);
    CHECK(result.outcome == TokenReadOutcome::kMissing);
    CHECK(result.bytes.empty());
}

TEST_CASE(
    "ReadTokenFromEnvironment treats an empty value as Missing, not Malformed",
    "[security][token_provider]") {
    FakeEnvironmentReader env;
    env.Set(kVarName, "");
    auto result = ReadTokenFromEnvironment(env, kVarName);
    CHECK(result.outcome == TokenReadOutcome::kMissing);
    CHECK(result.bytes.empty());
}

TEST_CASE("ReadTokenFromEnvironment treats odd-length hex as Malformed",
          "[security][token_provider]") {
    FakeEnvironmentReader env;
    env.Set(kVarName, "abc");
    auto result = ReadTokenFromEnvironment(env, kVarName);
    CHECK(result.outcome == TokenReadOutcome::kMalformed);
    CHECK(result.bytes.empty());
}

TEST_CASE("ReadTokenFromEnvironment treats a non-hex character as Malformed",
          "[security][token_provider]") {
    FakeEnvironmentReader env;
    //  64 characters, but "zz" at the start is not valid hex.
    env.Set(kVarName, std::string("zz") + std::string(62, '0'));
    auto result = ReadTokenFromEnvironment(env, kVarName);
    CHECK(result.outcome == TokenReadOutcome::kMalformed);
    CHECK(result.bytes.empty());
}

TEST_CASE("ReadTokenFromEnvironment treats a too-short token as Malformed",
          "[security][token_provider]") {
    FakeEnvironmentReader env;
    //  62 hex characters decodes to 31 bytes, one short of the required 32.
    env.Set(kVarName, std::string(62, '0'));
    auto result = ReadTokenFromEnvironment(env, kVarName);
    CHECK(result.outcome == TokenReadOutcome::kMalformed);
    CHECK(result.bytes.empty());
}

TEST_CASE("ReadTokenFromEnvironment treats a too-long token as Malformed",
          "[security][token_provider]") {
    FakeEnvironmentReader env;
    //  66 hex characters decodes to 33 bytes, one over the required 32.
    env.Set(kVarName, std::string(66, '0'));
    auto result = ReadTokenFromEnvironment(env, kVarName);
    CHECK(result.outcome == TokenReadOutcome::kMalformed);
    CHECK(result.bytes.empty());
}

TEST_CASE("ReadTokenFromEnvironment only reads the variable it is asked for",
          "[security][token_provider]") {
    FakeEnvironmentReader env;
    env.Set("SOME_OTHER_VARIABLE", kValidHexToken);
    CHECK(ReadTokenFromEnvironment(env, kVarName).outcome ==
          TokenReadOutcome::kMissing);
}

TEST_CASE("ReadTokenFromEnvironment isolates variables for the Malformed and "
          "Valid outcomes too",
          "[security][token_provider]") {
    FakeEnvironmentReader env;
    env.Set(kVarName, "not-hex");
    env.Set("SOME_OTHER_VARIABLE", kValidHexToken);
    CHECK(ReadTokenFromEnvironment(env, kVarName).outcome ==
          TokenReadOutcome::kMalformed);

    FakeEnvironmentReader env2;
    env2.Set(kVarName, kValidHexToken);
    env2.Set("SOME_OTHER_VARIABLE", "not-hex");
    CHECK(ReadTokenFromEnvironment(env2, kVarName).outcome ==
          TokenReadOutcome::kValid);
}
