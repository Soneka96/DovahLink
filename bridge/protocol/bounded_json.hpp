#pragma once

#include <boost/json/value.hpp>

#include <expected>
#include <string_view>

namespace dovahlink::protocol {

/// Identifies the first input-size or JSON-shape limit violated during parsing.
enum class BoundedJsonError {
    /// The complete inbound frame exceeds the configured byte limit.
    kFrameTooLarge,
    /// The input is not valid JSON or exceeds the parser's nesting limit.
    kInvalidJson,
    /// A JSON string or object key exceeds the configured byte limit.
    kStringTooLong,
    /// A JSON array exceeds the configured item limit.
    kArrayTooLong,
    /// A JSON object exceeds the configured member limit.
    kTooManyObjectMembers,
};

/// Parses one JSON text while enforcing all inbound size and nesting limits.
std::expected<boost::json::value, BoundedJsonError> ParseBoundedJson(std::string_view text);

}  // namespace dovahlink::protocol
