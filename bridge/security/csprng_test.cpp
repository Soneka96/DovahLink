#include "security/csprng.hpp"

#include <catch2/catch_test_macros.hpp>

#include <algorithm>
#include <cstdint>

using dovahlink::security::GenerateRandomBytes;

TEST_CASE("GenerateRandomBytes returns the requested number of bytes (256-bit token size)",
          "[security][csprng]") {
    auto bytes = GenerateRandomBytes(32);  // 256 bits, per TASK.md's one-time token size.
    REQUIRE(bytes.has_value());
    CHECK(bytes->size() == 32);
}

TEST_CASE("GenerateRandomBytes is size-agnostic for a non-token size", "[security][csprng]") {
    auto bytes = GenerateRandomBytes(16);
    REQUIRE(bytes.has_value());
    CHECK(bytes->size() == 16);
}

TEST_CASE("GenerateRandomBytes returns an empty buffer for size zero", "[security][csprng]") {
    auto bytes = GenerateRandomBytes(0);
    REQUIRE(bytes.has_value());
    CHECK(bytes->empty());
}

TEST_CASE("GenerateRandomBytes produces different output across calls", "[security][csprng]") {
    auto first = GenerateRandomBytes(32);
    auto second = GenerateRandomBytes(32);
    REQUIRE(first.has_value());
    REQUIRE(second.has_value());
    CHECK(*first != *second);
}

TEST_CASE("GenerateRandomBytes output is not all zero bytes", "[security][csprng]") {
    // A cheap sanity check against a degenerate all-zero RNG; not a statistical
    // randomness test. The probability of a genuine CSPRNG producing 32 zero
    // bytes by chance is astronomically small.
    auto bytes = GenerateRandomBytes(32);
    REQUIRE(bytes.has_value());
    bool allZero = std::all_of(bytes->begin(), bytes->end(), [](std::uint8_t b) { return b == 0; });
    CHECK_FALSE(allZero);
}
