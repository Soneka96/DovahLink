#pragma once

#include "protocol/bounded_json.hpp"
#include "protocol/envelope.hpp"

#include <catch2/catch_test_macros.hpp>

#include <filesystem>
#include <fstream>
#include <sstream>
#include <string>
#include <utility>

// Test-only helpers for loading canonical protocol fixtures from the configured
// fixture directory.
namespace dovahlink::protocol::test_support {

/// Reads a protocol fixture file as its complete binary contents.
inline std::string ReadFixture(const std::string& relativePath) {
    std::filesystem::path path = std::filesystem::path(DOVAHLINK_FIXTURES_DIR) / relativePath;
    std::ifstream file(path, std::ios::binary);
    REQUIRE(file.is_open());
    std::ostringstream buffer;
    buffer << file.rdbuf();
    return buffer.str();
}

/// Reads and decodes a protocol envelope fixture.
inline Envelope DecodeFixtureEnvelope(const std::string& relativePath) {
    auto parsed = ParseBoundedJson(ReadFixture(relativePath));
    REQUIRE(parsed.has_value());
    auto envelope = DecodeEnvelope(*parsed);
    REQUIRE(envelope.has_value());
    return std::move(*envelope);
}

}  // namespace dovahlink::protocol::test_support
