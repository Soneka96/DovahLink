#pragma once

#include <cstdint>

namespace dovahlink::adapter::plugin {

///  Determines whether a monotonic-time warning window permits a new log.
///  @param lastWarningMilliseconds The previous log time, or zero if none.
///  @param nowMilliseconds The current steady-clock time in milliseconds.
///  @param minimumIntervalMilliseconds The minimum delay between logs.
///  @return Whether a warning may be logged now.
[[nodiscard]] constexpr bool HasWarningIntervalElapsed(
    std::int64_t lastWarningMilliseconds, std::int64_t nowMilliseconds,
    std::int64_t minimumIntervalMilliseconds) noexcept {
    return lastWarningMilliseconds == 0 ||
           nowMilliseconds - lastWarningMilliseconds >=
               minimumIntervalMilliseconds;
}

} //  namespace dovahlink::adapter::plugin
