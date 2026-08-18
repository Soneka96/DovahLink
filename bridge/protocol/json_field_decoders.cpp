#include "protocol/json_field_decoders.hpp"

#include <limits>
#include <utility>

namespace dovahlink::protocol {

namespace {

/// Creates an unexpected result containing a field-decode error.
std::unexpected<DecodeError> Fail(std::string reason) {
    return std::unexpected(DecodeError{std::move(reason)});
}

}
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

std::expected<std::optional<std::string>, DecodeError> DecodeOptionalString(const boost::json::value* value,
                                                                             std::string_view fieldName) {
    if (!value || value->is_null()) {
        return std::optional<std::string>{};
    }
    if (!value->is_string() || value->get_string().empty()) {
        return Fail(std::string(fieldName) + " must be null or a non-empty string");
    }
    return std::optional<std::string>(std::string(value->get_string()));
}

std::expected<std::optional<std::int64_t>, DecodeError> DecodeOptionalNonNegativeInt(
    const boost::json::value* value, std::string_view fieldName) {
    if (!value || value->is_null()) {
        return std::optional<std::int64_t>{};
    }
    auto decoded = DecodeNonNegativeInt(value, fieldName);
    if (!decoded) {
        return std::unexpected(decoded.error());
    }
    return std::optional<std::int64_t>(*decoded);
}

}  // namespace dovahlink::protocol
