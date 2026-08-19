#include "protocol/json_field_decoders.hpp"

#include <boost/json/array.hpp>

#include <limits>
#include <utility>

namespace dovahlink::protocol {

std::unexpected<DecodeError> Fail(std::string reason) {
    return std::unexpected(DecodeError{std::move(reason)});
}

const boost::json::value* RequireField(const boost::json::object& obj, std::string_view key) {
    return obj.if_contains(key);
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

std::expected<std::string, DecodeError> DecodeString(const boost::json::value* value,
                                                       std::string_view fieldName) {
    if (!value) {
        return Fail("missing required field: " + std::string(fieldName));
    }
    if (!value->is_string()) {
        return Fail(std::string(fieldName) + " must be a string");
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

std::expected<std::vector<std::string>, DecodeError> DecodeStringArray(const boost::json::value* value,
                                                                        std::string_view fieldName) {
    if (!value) {
        return Fail("missing required field: " + std::string(fieldName));
    }
    if (!value->is_array()) {
        return Fail(std::string(fieldName) + " must be an array");
    }
    std::vector<std::string> result;
    result.reserve(value->get_array().size());
    for (const boost::json::value& item : value->get_array()) {
        if (!item.is_string() || item.get_string().empty()) {
            return Fail(std::string(fieldName) + " items must be non-empty strings");
        }
        result.emplace_back(item.get_string());
    }
    return result;
}

}  // namespace dovahlink::protocol
