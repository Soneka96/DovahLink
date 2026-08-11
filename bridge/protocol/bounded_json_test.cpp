#include "protocol/bounded_json.hpp"

#include "security/limits.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cstddef>
#include <string>

using dovahlink::protocol::BoundedJsonError;
using dovahlink::protocol::ParseBoundedJson;

TEST_CASE("a well-formed message within every limit parses successfully", "[protocol][bounded_json]") {
    auto result = ParseBoundedJson(R"({"a": 1, "b": [1, 2, 3], "c": "hello"})");
    REQUIRE(result.has_value());
    CHECK(result->is_object());
}

TEST_CASE("input larger than the frame limit is rejected before parsing", "[protocol][bounded_json]") {
    const std::string oversized(dovahlink::security::kMaxInboundFrameBytes + 1, 'x');
    auto result = ParseBoundedJson(oversized);
    REQUIRE_FALSE(result.has_value());
    CHECK(result.error() == BoundedJsonError::kFrameTooLarge);
}

TEST_CASE("malformed JSON is rejected", "[protocol][bounded_json]") {
    auto result = ParseBoundedJson("{not json");
    REQUIRE_FALSE(result.has_value());
    CHECK(result.error() == BoundedJsonError::kInvalidJson);
}

TEST_CASE("nesting deeper than the depth limit is rejected", "[protocol][bounded_json]") {
    // Boost.JSON enforces max_depth during parsing itself and fails the parse; DovahLink
    // surfaces that as kInvalidJson rather than a dedicated code (see bounded_json.hpp).
    std::string nested;
    for (int i = 0; i < dovahlink::security::kMaxJsonNestingDepth + 1; ++i) {
        nested += "[";
    }
    for (int i = 0; i < dovahlink::security::kMaxJsonNestingDepth + 1; ++i) {
        nested += "]";
    }

    auto result = ParseBoundedJson(nested);
    REQUIRE_FALSE(result.has_value());
    CHECK(result.error() == BoundedJsonError::kInvalidJson);
}

TEST_CASE("a string longer than the string limit is rejected", "[protocol][bounded_json]") {
    const std::string longString(dovahlink::security::kMaxStringLengthBytes + 1, 'x');
    const std::string text = R"({"value": ")" + longString + R"("})";

    auto result = ParseBoundedJson(text);
    REQUIRE_FALSE(result.has_value());
    CHECK(result.error() == BoundedJsonError::kStringTooLong);
}

TEST_CASE("an object key longer than the string limit is rejected", "[protocol][bounded_json]") {
    const std::string longKey(dovahlink::security::kMaxStringLengthBytes + 1, 'k');
    const std::string text = "{\"" + longKey + "\":0}";

    auto result = ParseBoundedJson(text);
    REQUIRE_FALSE(result.has_value());
    CHECK(result.error() == BoundedJsonError::kStringTooLong);
}

TEST_CASE("an array longer than the array limit is rejected", "[protocol][bounded_json]") {
    std::string text = "[";
    for (std::size_t i = 0; i < dovahlink::security::kMaxArrayItems + 1; ++i) {
        if (i != 0) {
            text += ",";
        }
        text += "0";
    }
    text += "]";

    auto result = ParseBoundedJson(text);
    REQUIRE_FALSE(result.has_value());
    CHECK(result.error() == BoundedJsonError::kArrayTooLong);
}

TEST_CASE("an object with more members than the limit is rejected", "[protocol][bounded_json]") {
    std::string text = "{";
    for (std::size_t i = 0; i < dovahlink::security::kMaxObjectMembers + 1; ++i) {
        if (i != 0) {
            text += ",";
        }
        text += "\"k" + std::to_string(i) + "\":0";
    }
    text += "}";

    auto result = ParseBoundedJson(text);
    REQUIRE_FALSE(result.has_value());
    CHECK(result.error() == BoundedJsonError::kTooManyObjectMembers);
}

TEST_CASE("a string exactly at the string limit is accepted", "[protocol][bounded_json]") {
    const std::string maxString(dovahlink::security::kMaxStringLengthBytes, 'x');
    const std::string text = R"({"value": ")" + maxString + R"("})";

    auto result = ParseBoundedJson(text);
    REQUIRE(result.has_value());
}

TEST_CASE("an array exactly at the array limit is accepted", "[protocol][bounded_json]") {
    std::string text = "[";
    for (std::size_t i = 0; i < dovahlink::security::kMaxArrayItems; ++i) {
        if (i != 0) {
            text += ",";
        }
        text += "0";
    }
    text += "]";

    auto result = ParseBoundedJson(text);
    REQUIRE(result.has_value());
}

TEST_CASE("an object exactly at the member limit is accepted", "[protocol][bounded_json]") {
    std::string text = "{";
    for (std::size_t i = 0; i < dovahlink::security::kMaxObjectMembers; ++i) {
        if (i != 0) {
            text += ",";
        }
        text += "\"k" + std::to_string(i) + "\":0";
    }
    text += "}";

    auto result = ParseBoundedJson(text);
    REQUIRE(result.has_value());
}

TEST_CASE("empty input is rejected as invalid JSON", "[protocol][bounded_json]") {
    auto result = ParseBoundedJson("");
    REQUIRE_FALSE(result.has_value());
    CHECK(result.error() == BoundedJsonError::kInvalidJson);
}

TEST_CASE("a violation nested inside a valid structure is still rejected", "[protocol][bounded_json]") {
    const std::string longString(dovahlink::security::kMaxStringLengthBytes + 1, 'x');
    const std::string text = R"({"outer": [{"inner": ")" + longString + R"("}]})";

    auto result = ParseBoundedJson(text);
    REQUIRE_FALSE(result.has_value());
    CHECK(result.error() == BoundedJsonError::kStringTooLong);
}
