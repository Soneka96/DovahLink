#pragma once

#include "protocol/bounded_json.hpp"
#include "protocol/envelope.hpp"

#include <catch2/catch_test_macros.hpp>

#include <algorithm>
#include <filesystem>
#include <fstream>
#include <sstream>
#include <string>
#include <utility>
#include <vector>

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

/// Returns every JSON fixture path below the configured fixture directory in
/// deterministic order.
inline std::vector<std::string> DiscoverFixturePaths() {
    const std::filesystem::path fixtureRoot(DOVAHLINK_FIXTURES_DIR);
    std::vector<std::string> fixturePaths;
    for (const auto& entry : std::filesystem::recursive_directory_iterator(fixtureRoot)) {
        if (entry.is_regular_file() && entry.path().extension() == ".json") {
            fixturePaths.push_back(std::filesystem::relative(entry.path(), fixtureRoot).generic_string());
        }
    }
    std::ranges::sort(fixturePaths);
    return fixturePaths;
}

}  // namespace dovahlink::protocol::test_support
