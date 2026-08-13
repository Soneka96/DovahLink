#include "application/timestamp.hpp"

#include <format>

namespace dovahlink::application {

/**
 * @brief Formats a timestamp as a whole-second UTC-style ISO 8601 string.
 *
 * @param time Timestamp to format.
 * @return std::string Timestamp in the `YYYY-MM-DDTHH:MM:SSZ` format.
 */
std::string FormatTimestamp(std::chrono::system_clock::time_point time) {
    auto truncated = std::chrono::floor<std::chrono::seconds>(time);
    return std::format("{:%Y-%m-%dT%H:%M:%S}Z", truncated);
}

}  // namespace dovahlink::application
