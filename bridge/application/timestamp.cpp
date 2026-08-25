#include "application/timestamp.hpp"

#include <format>

namespace dovahlink::application {

std::string FormatTimestamp(std::chrono::system_clock::time_point time) {
  auto truncated = std::chrono::floor<std::chrono::seconds>(time);
  return std::format("{:%Y-%m-%dT%H:%M:%S}Z", truncated);
}

} // namespace dovahlink::application
