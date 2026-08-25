#pragma once

#include <chrono>
#include <string>

namespace dovahlink::application {

/// Formats a time as a second-precision UTC RFC 3339 timestamp.
/// @param time Wall-clock time to format.
/// @return Timestamp suitable for a protocol `occurredAt` field.
[[nodiscard]] std::string
FormatTimestamp(std::chrono::system_clock::time_point time);

} // namespace dovahlink::application
