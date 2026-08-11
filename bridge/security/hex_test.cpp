#include "security/hex.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cstdint>
#include <vector>

using dovahlink::security::DecodeHex;
using dovahlink::security::EncodeHex;

TEST_CASE("EncodeHex produces lowercase two-digit-per-byte output", "[security][hex]") {
    std::vector<std::uint8_t> bytes{0x00, 0x0F, 0xAB, 0xFF};
    CHECK(EncodeHex(bytes) == "000fabff");
}

TEST_CASE("EncodeHex of an empty vector is an empty string", "[security][hex]") {
    CHECK(EncodeHex({}).empty());
}

TEST_CASE("DecodeHex decodes lowercase and uppercase hex to the same bytes", "[security][hex]") {
    auto lower = DecodeHex("0fabff");
    auto upper = DecodeHex("0FABFF");
    REQUIRE(lower.has_value());
    REQUIRE(upper.has_value());
    CHECK(*lower == std::vector<std::uint8_t>{0x0F, 0xAB, 0xFF});
    CHECK(*lower == *upper);
}

TEST_CASE("DecodeHex rejects odd-length input", "[security][hex]") {
    CHECK_FALSE(DecodeHex("abc").has_value());
}

TEST_CASE("DecodeHex rejects empty input", "[security][hex]") {
    CHECK_FALSE(DecodeHex("").has_value());
}

TEST_CASE("DecodeHex rejects a non-hex character", "[security][hex]") {
    CHECK_FALSE(DecodeHex("zz00").has_value());
}

TEST_CASE("EncodeHex then DecodeHex round-trips arbitrary bytes", "[security][hex]") {
    std::vector<std::uint8_t> bytes{0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF};
    auto decoded = DecodeHex(EncodeHex(bytes));
    REQUIRE(decoded.has_value());
    CHECK(*decoded == bytes);
}
