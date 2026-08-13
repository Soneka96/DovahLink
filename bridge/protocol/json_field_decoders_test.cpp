#include "protocol/json_field_decoders.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/parse.hpp>
#include <boost/json/value.hpp>

using dovahlink::protocol::DecodeNonEmptyString;
using dovahlink::protocol::DecodeNonNegativeInt;

namespace {

/// Parses a short JSON value used as decoder-test input.
boost::json::value Json(const char* text) { return boost::json::parse(text); }

}  // namespace

TEST_CASE("DecodeNonEmptyString accepts a non-empty string", "[protocol][json_field_decoders]") {
    boost::json::value value = Json(R"("hello")");
    auto result = DecodeNonEmptyString(&value, "field");
    REQUIRE(result.has_value());
    CHECK(*result == "hello");
}

TEST_CASE("DecodeNonEmptyString rejects a null field pointer (missing field)", "[protocol][json_field_decoders]") {
    auto result = DecodeNonEmptyString(nullptr, "field");
    REQUIRE_FALSE(result.has_value());
}

TEST_CASE("DecodeNonEmptyString rejects an empty string", "[protocol][json_field_decoders]") {
    boost::json::value value = Json(R"("")");
    auto result = DecodeNonEmptyString(&value, "field");
    REQUIRE_FALSE(result.has_value());
}

TEST_CASE("DecodeNonEmptyString rejects a non-string value", "[protocol][json_field_decoders]") {
    boost::json::value value = Json("5");
    auto result = DecodeNonEmptyString(&value, "field");
    REQUIRE_FALSE(result.has_value());
}

TEST_CASE("DecodeNonNegativeInt accepts a positive int64", "[protocol][json_field_decoders]") {
    boost::json::value value = Json("42");
    auto result = DecodeNonNegativeInt(&value, "field");
    REQUIRE(result.has_value());
    CHECK(*result == 42);
}

TEST_CASE("DecodeNonNegativeInt accepts zero", "[protocol][json_field_decoders]") {
    boost::json::value value = Json("0");
    auto result = DecodeNonNegativeInt(&value, "field");
    REQUIRE(result.has_value());
    CHECK(*result == 0);
}

TEST_CASE("DecodeNonNegativeInt rejects a null field pointer (missing field)", "[protocol][json_field_decoders]") {
    auto result = DecodeNonNegativeInt(nullptr, "field");
    REQUIRE_FALSE(result.has_value());
}

TEST_CASE("DecodeNonNegativeInt rejects a negative integer", "[protocol][json_field_decoders]") {
    boost::json::value value = Json("-1");
    auto result = DecodeNonNegativeInt(&value, "field");
    REQUIRE_FALSE(result.has_value());
}

TEST_CASE("DecodeNonNegativeInt rejects a non-integer value", "[protocol][json_field_decoders]") {
    boost::json::value value = Json(R"("42")");
    auto result = DecodeNonNegativeInt(&value, "field");
    REQUIRE_FALSE(result.has_value());
}

TEST_CASE("DecodeNonNegativeInt rejects a value too large to fit in int64", "[protocol][json_field_decoders]") {
    boost::json::value value = Json("18446744073709551615");  // UINT64_MAX
    auto result = DecodeNonNegativeInt(&value, "field");
    REQUIRE_FALSE(result.has_value());
}

TEST_CASE("DecodeNonNegativeInt accepts the largest representable int64 value",
          "[protocol][json_field_decoders]") {
    boost::json::value value = Json("9223372036854775807");  // INT64_MAX
    auto result = DecodeNonNegativeInt(&value, "field");
    REQUIRE(result.has_value());
    CHECK(*result == 9223372036854775807LL);
}
