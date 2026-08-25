#include "security/constant_time_compare.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cstdint>
#include <vector>

using dovahlink::security::ConstantTimeEquals;

TEST_CASE("ConstantTimeEquals reports true for identical non-empty buffers",
          "[security][constant_time_compare]") {
    std::vector<std::uint8_t> a{1, 2, 3, 4};
    std::vector<std::uint8_t> b{1, 2, 3, 4};
    CHECK(ConstantTimeEquals(a, b));
}

TEST_CASE("ConstantTimeEquals reports true for two empty buffers",
          "[security][constant_time_compare]") {
    CHECK(ConstantTimeEquals({}, {}));
}

TEST_CASE("ConstantTimeEquals reports false for equal-length buffers differing "
          "in one byte",
          "[security][constant_time_compare]") {
    std::vector<std::uint8_t> a{1, 2, 3, 4};
    std::vector<std::uint8_t> b{1, 2, 9, 4};
    CHECK_FALSE(ConstantTimeEquals(a, b));
}

TEST_CASE("ConstantTimeEquals reports false for differing lengths regardless "
          "of shared prefix",
          "[security][constant_time_compare]") {
    std::vector<std::uint8_t> a{1, 2, 3};
    std::vector<std::uint8_t> b{1, 2, 3, 4};
    CHECK_FALSE(ConstantTimeEquals(a, b));
    CHECK_FALSE(ConstantTimeEquals(b, a));
}

TEST_CASE("ConstantTimeEquals reports false for an empty buffer against a "
          "non-empty one",
          "[security][constant_time_compare]") {
    std::vector<std::uint8_t> nonEmpty{1};
    CHECK_FALSE(ConstantTimeEquals({}, nonEmpty));
    CHECK_FALSE(ConstantTimeEquals(nonEmpty, {}));
}

TEST_CASE("ConstantTimeEquals reports false the same way whether buffers "
          "differ at the first byte, "
          "the last byte, or every byte",
          "[security][constant_time_compare]") {
    std::vector<std::uint8_t> reference{1, 2, 3, 4};
    std::vector<std::uint8_t> differsFirst{9, 2, 3, 4};
    std::vector<std::uint8_t> differsLast{1, 2, 3, 9};
    std::vector<std::uint8_t> differsEveryByte{9, 9, 9, 9};
    CHECK_FALSE(ConstantTimeEquals(reference, differsFirst));
    CHECK_FALSE(ConstantTimeEquals(reference, differsLast));
    CHECK_FALSE(ConstantTimeEquals(reference, differsEveryByte));
}
