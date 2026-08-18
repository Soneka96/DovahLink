#pragma once

#include "protocol/decode_error.hpp"

#include <boost/json/object.hpp>
#include <boost/json/value.hpp>

#include <cstdint>
#include <expected>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace dovahlink::protocol {

/// Creates an unexpected result containing a field-decode error. The shared error-construction
/// point every payload codec in `protocol/` uses, so a decode failure is always built the same way.
std::unexpected<DecodeError> Fail(std::string reason);

/// Returns a field value from `obj`, or `nullptr` when `key` is absent. A present key whose value
/// is JSON `null` still returns a valid, non-null pointer -- the individual field decoders (for
/// example `DecodeOptionalString`) are what treat presence-as-`null` and absence identically.
const boost::json::value* RequireField(const boost::json::object& obj, std::string_view key);

/// Decodes a present JSON field as a non-empty string.
std::expected<std::string, DecodeError> DecodeNonEmptyString(const boost::json::value* value,
                                                              std::string_view fieldName);

/// Decodes a present JSON field as a non-negative signed integer.
std::expected<std::int64_t, DecodeError> DecodeNonNegativeInt(const boost::json::value* value,
                                                               std::string_view fieldName);

/// Decodes an optional string field: absence and JSON `null` decode
/// identically to `std::nullopt`, matching how the canonical envelope treats
/// a nullable field (protocol/schema/README.md).
/// @param value Field value, or `nullptr` when the field is absent.
/// @param fieldName Field name used in error messages.
/// @return `std::nullopt` when the field is absent or `null`; the decoded
///     string otherwise. An error when present but neither `null` nor a
///     non-empty string.
std::expected<std::optional<std::string>, DecodeError> DecodeOptionalString(const boost::json::value* value,
                                                                             std::string_view fieldName);

/// Decodes an optional non-negative integer field: absence and JSON `null` decode identically to
/// `std::nullopt`, matching `DecodeOptionalString`'s own absence/`null` treatment.
/// @param value Field value, or `nullptr` when the field is absent.
/// @param fieldName Field name used in error messages.
/// @return `std::nullopt` when the field is absent or `null`; the decoded integer otherwise. An
///     error when present but neither `null` nor a non-negative integer.
std::expected<std::optional<std::int64_t>, DecodeError> DecodeOptionalNonNegativeInt(
    const boost::json::value* value, std::string_view fieldName);

/// Decodes a present JSON field as an array containing only non-empty strings.
std::expected<std::vector<std::string>, DecodeError> DecodeStringArray(const boost::json::value* value,
                                                                        std::string_view fieldName);

}  // namespace dovahlink::protocol
