#include "protocol/json_field_decoders.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/parse.hpp>
#include <boost/json/value.hpp>

#include <string>
#include <vector>

using dovahlink::protocol::DecodeNonEmptyString;
using dovahlink::protocol::DecodeNonNegativeInt;
using dovahlink::protocol::DecodeOptionalNonNegativeInt;
using dovahlink::protocol::DecodeOptionalString;
using dovahlink::protocol::DecodeStringArray;
using dovahlink::protocol::Fail;
using dovahlink::protocol::RequireField;

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

TEST_CASE("DecodeOptionalString treats an absent field pointer as no value", "[protocol][json_field_decoders]") {
    auto result = DecodeOptionalString(nullptr, "field");
    REQUIRE(result.has_value());
    CHECK_FALSE(result->has_value());
}

TEST_CASE("DecodeOptionalString treats a JSON null as no value", "[protocol][json_field_decoders]") {
    boost::json::value value = Json("null");
    auto result = DecodeOptionalString(&value, "field");
    REQUIRE(result.has_value());
    CHECK_FALSE(result->has_value());
}

TEST_CASE("DecodeOptionalString accepts a non-empty string", "[protocol][json_field_decoders]") {
    boost::json::value value = Json(R"("bridge-1")");
    auto result = DecodeOptionalString(&value, "field");
    REQUIRE(result.has_value());
    REQUIRE(result->has_value());
    CHECK(**result == "bridge-1");
}

TEST_CASE("DecodeOptionalString rejects an empty string", "[protocol][json_field_decoders]") {
    boost::json::value value = Json(R"("")");
    auto result = DecodeOptionalString(&value, "field");
    REQUIRE_FALSE(result.has_value());
}

TEST_CASE("DecodeOptionalString rejects a non-string, non-null value", "[protocol][json_field_decoders]") {
    boost::json::value value = Json("5");
    auto result = DecodeOptionalString(&value, "field");
    REQUIRE_FALSE(result.has_value());
}

TEST_CASE("DecodeOptionalString rejects a boolean value", "[protocol][json_field_decoders]") {
    boost::json::value value = Json("true");
    auto result = DecodeOptionalString(&value, "field");
    REQUIRE_FALSE(result.has_value());
}

TEST_CASE("DecodeOptionalNonNegativeInt treats an absent field pointer as no value",
          "[protocol][json_field_decoders]") {
    auto result = DecodeOptionalNonNegativeInt(nullptr, "field");
    REQUIRE(result.has_value());
    CHECK_FALSE(result->has_value());
}

TEST_CASE("DecodeOptionalNonNegativeInt treats a JSON null as no value", "[protocol][json_field_decoders]") {
    boost::json::value value = Json("null");
    auto result = DecodeOptionalNonNegativeInt(&value, "field");
    REQUIRE(result.has_value());
    CHECK_FALSE(result->has_value());
}

TEST_CASE("DecodeOptionalNonNegativeInt accepts a non-negative integer", "[protocol][json_field_decoders]") {
    boost::json::value value = Json("42");
    auto result = DecodeOptionalNonNegativeInt(&value, "field");
    REQUIRE(result.has_value());
    REQUIRE(result->has_value());
    CHECK(**result == 42);
}

TEST_CASE("DecodeOptionalNonNegativeInt rejects a negative integer", "[protocol][json_field_decoders]") {
    boost::json::value value = Json("-1");
    auto result = DecodeOptionalNonNegativeInt(&value, "field");
    REQUIRE_FALSE(result.has_value());
}

TEST_CASE("DecodeOptionalNonNegativeInt rejects a non-integer, non-null value",
          "[protocol][json_field_decoders]") {
    boost::json::value value = Json(R"("42")");
    auto result = DecodeOptionalNonNegativeInt(&value, "field");
    REQUIRE_FALSE(result.has_value());
}

TEST_CASE("DecodeStringArray accepts an array of non-empty strings", "[protocol][json_field_decoders]") {
    boost::json::value value = Json(R"(["a", "b"])");
    auto result = DecodeStringArray(&value, "field");
    REQUIRE(result.has_value());
    CHECK(*result == std::vector<std::string>{"a", "b"});
}

TEST_CASE("DecodeStringArray accepts an empty array", "[protocol][json_field_decoders]") {
    boost::json::value value = Json("[]");
    auto result = DecodeStringArray(&value, "field");
    REQUIRE(result.has_value());
    CHECK(result->empty());
}

TEST_CASE("DecodeStringArray rejects a null field pointer (missing field)", "[protocol][json_field_decoders]") {
    auto result = DecodeStringArray(nullptr, "field");
    REQUIRE_FALSE(result.has_value());
}

TEST_CASE("DecodeStringArray rejects a non-array value", "[protocol][json_field_decoders]") {
    boost::json::value value = Json(R"("not an array")");
    auto result = DecodeStringArray(&value, "field");
    REQUIRE_FALSE(result.has_value());
}

TEST_CASE("DecodeStringArray rejects an array containing a non-string item",
          "[protocol][json_field_decoders]") {
    boost::json::value value = Json(R"(["a", 1])");
    auto result = DecodeStringArray(&value, "field");
    REQUIRE_FALSE(result.has_value());
}

TEST_CASE("DecodeStringArray rejects an array containing an empty string", "[protocol][json_field_decoders]") {
    boost::json::value value = Json(R"(["a", ""])");
    auto result = DecodeStringArray(&value, "field");
    REQUIRE_FALSE(result.has_value());
}

TEST_CASE("Fail wraps its reason in a DecodeError", "[protocol][json_field_decoders]") {
    auto result = Fail("something went wrong");
    CHECK(result.error().reason == "something went wrong");
}

TEST_CASE("RequireField returns the value for a present key", "[protocol][json_field_decoders]") {
    boost::json::object obj = Json(R"({"field": "value"})").get_object();
    const boost::json::value* found = RequireField(obj, "field");
    REQUIRE(found != nullptr);
    CHECK(found->as_string() == "value");
}

TEST_CASE("RequireField returns non-null for a present key whose value is JSON null",
          "[protocol][json_field_decoders]") {
    boost::json::object obj = Json(R"({"field": null})").get_object();
    const boost::json::value* found = RequireField(obj, "field");
    REQUIRE(found != nullptr);
    CHECK(found->is_null());
}

TEST_CASE("RequireField returns nullptr for an absent key", "[protocol][json_field_decoders]") {
    boost::json::object obj = Json(R"({})").get_object();
    CHECK(RequireField(obj, "field") == nullptr);
}
