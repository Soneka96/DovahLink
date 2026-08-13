#include "protocol/json_field_decoders.hpp"

#include <limits>
#include <utility>

namespace dovahlink::protocol {

namespace {

/**
 * @brief Creates a failed decode result with the specified reason.
 *
 * @param reason Description of the decoding failure.
 * @return An unexpected result containing the decode error.
 */
std::unexpected<DecodeError> Fail(std::string reason) {
    return std::unexpected(DecodeError{std::move(reason)});
}

}  /**
 * @brief Decodes a required field as a non-empty string.
 *
 * @param value JSON value to decode.
 * @param fieldName Name of the field used in error messages.
 * @return The decoded string, or a `DecodeError` describing why decoding failed.
 */

std::expected<std::string, DecodeError> DecodeNonEmptyString(const boost::json::value* value,
                                                               std::string_view fieldName) {
    if (!value) {
        return Fail("missing required field: " + std::string(fieldName));
    }
    if (!value->is_string() || value->get_string().empty()) {
        return Fail(std::string(fieldName) + " must be a non-empty string");
    }
    return std::string(value->get_string());
}

/**
 * @brief Decodes a required JSON field as a non-negative integer.
 *
 * @param value JSON value to decode.
 * @param fieldName Name of the field used in error messages.
 * @return The decoded non-negative integer, or a descriptive decode error.
 */
std::expected<std::int64_t, DecodeError> DecodeNonNegativeInt(const boost::json::value* value,
                                                                std::string_view fieldName) {
    if (!value) {
        return Fail("missing required field: " + std::string(fieldName));
    }
    if (!value->is_int64() && !value->is_uint64()) {
        return Fail(std::string(fieldName) + " must be a non-negative integer");
    }

    std::int64_t result = 0;
    if (value->is_uint64()) {
        std::uint64_t asUint = value->get_uint64();
        if (asUint > static_cast<std::uint64_t>(std::numeric_limits<std::int64_t>::max())) {
            return Fail(std::string(fieldName) + " is out of range");
        }
        result = static_cast<std::int64_t>(asUint);
    } else {
        result = value->get_int64();
    }

    if (result < 0) {
        return Fail(std::string(fieldName) + " must be non-negative");
    }
    return result;
}

}  // namespace dovahlink::protocol
