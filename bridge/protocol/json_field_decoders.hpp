#pragma once

#include "protocol/decode_error.hpp"

#include <boost/json/value.hpp>

#include <cstdint>
#include <expected>
#include <string>
#include <string_view>

namespace dovahlink::protocol {

/// Decodes a present JSON field as a non-empty string.
std::expected<std::string, DecodeError> DecodeNonEmptyString(const boost::json::value* value,
                                                              std::string_view fieldName);

/// Decodes a present JSON field as a non-negative signed integer.
std::expected<std::int64_t, DecodeError> DecodeNonNegativeInt(const boost::json::value* value,
                                                               std::string_view fieldName);

}  // namespace dovahlink::protocol
