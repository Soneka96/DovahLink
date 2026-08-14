#pragma once

#include "protocol/decode_error.hpp"

#include <boost/json/value.hpp>

#include <cstdint>
#include <expected>
#include <optional>
#include <string>
#include <string_view>

namespace dovahlink::protocol {

/// Decodes a present JSON field as a non-empty string.
std::expected<std::string, DecodeError> DecodeNonEmptyString(const boost::json::value* value,
                                                              std::string_view fieldName);

/// Decodes a present JSON field as a non-negative signed integer.
std::expected<std::int64_t, DecodeError> DecodeNonNegativeInt(const boost::json::value* value,
                                                               std::string_view fieldName);

/// Decodes a version-gated identity field that is absent in v1, and in v2 is
/// either JSON `null` or a non-empty string (protocol/schema/README.md's v2
/// encoding rule: v1 omits the key, v2 emits `null` or a value).
/// @param value Field value, or `nullptr` when the field is absent.
/// @param fieldName Field name used in error messages.
/// @return `std::nullopt` when the field is absent or `null`; the decoded
///     string otherwise. An error when present but neither `null` nor a
///     non-empty string.
std::expected<std::optional<std::string>, DecodeError> DecodeOptionalString(const boost::json::value* value,
                                                                             std::string_view fieldName);

}  // namespace dovahlink::protocol
