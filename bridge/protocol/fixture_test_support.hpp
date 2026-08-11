#pragma once

#include "protocol/bounded_json.hpp"
#include "protocol/envelope.hpp"

#include <catch2/catch_test_macros.hpp>

#include <filesystem>
#include <fstream>
#include <sstream>
#include <string>
#include <utility>

// Test-only helpers for loading protocol/fixtures/*.json files by path relative to
// that directory. DOVAHLINK_FIXTURES_DIR is defined by bridge/CMakeLists.txt. Not
// part of dovahlink_bridge_core: only test .cpp files include this header.
namespace dovahlink::protocol::test_support {

inline std::string ReadFixture(const std::string& relativePath) {
    std::filesystem::path path = std::filesystem::path(DOVAHLINK_FIXTURES_DIR) / relativePath;
    std::ifstream file(path, std::ios::binary);
    REQUIRE(file.is_open());
    std::ostringstream buffer;
    buffer << file.rdbuf();
    return buffer.str();
}

inline Envelope DecodeFixtureEnvelope(const std::string& relativePath) {
    auto parsed = ParseBoundedJson(ReadFixture(relativePath));
    REQUIRE(parsed.has_value());
    auto envelope = DecodeEnvelope(*parsed);
    REQUIRE(envelope.has_value());
    return std::move(*envelope);
}

}  // namespace dovahlink::protocol::test_support
