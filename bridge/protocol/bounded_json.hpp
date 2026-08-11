#pragma once

#include <boost/json/value.hpp>

#include <expected>
#include <string_view>

namespace dovahlink::protocol {

enum class BoundedJsonError {
    kFrameTooLarge,
    kInvalidJson,
    kStringTooLong,
    kArrayTooLong,
    kTooManyObjectMembers,
};

// Parses one JSON text against the Phase 1 input limits in bridge/security/limits.hpp,
// rejecting oversized, malformed, or over-limit input before it reaches message-specific
// decoding. See ai/context/protocol/security.md.
//
// A frame over kMaxInboundFrameBytes is rejected without being parsed at all. Nesting deeper
// than kMaxJsonNestingDepth is enforced by Boost.JSON's own parser and surfaces as
// kInvalidJson, since the parse simply fails; it has no dedicated error code here.
std::expected<boost::json::value, BoundedJsonError> ParseBoundedJson(std::string_view text);

}  // namespace dovahlink::protocol
