#pragma once

#include "shared/enums.hpp"

#include <boost/json/value.hpp>

#include <expected>
#include <string_view>

namespace dovahlink::protocol {

/// Parses one JSON text while enforcing all inbound size and nesting limits.
std::expected<boost::json::value, BoundedJsonError>
ParseBoundedJson(std::string_view text);

} // namespace dovahlink::protocol
