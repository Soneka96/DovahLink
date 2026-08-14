#include "application/timestamp.hpp"

#include <catch2/catch_test_macros.hpp>

#include <chrono>

using dovahlink::application::FormatTimestamp;
using namespace std::chrono;

TEST_CASE("FormatTimestamp matches the canonical fixtures' example value", "[application][timestamp]") {
    // protocol/fixtures' example occurredAt is "2026-08-10T12:00:00Z".
    auto time = sys_days{2026y / August / 10} + hours(12);
    CHECK(FormatTimestamp(time) == "2026-08-10T12:00:00Z");
}

TEST_CASE("FormatTimestamp zero-pads single-digit month, day, hour, minute, and second",
          "[application][timestamp]") {
    auto time = sys_days{2026y / January / 5} + hours(3) + minutes(4) + seconds(9);
    CHECK(FormatTimestamp(time) == "2026-01-05T03:04:09Z");
}

TEST_CASE("FormatTimestamp truncates sub-second precision rather than rounding",
          "[application][timestamp]") {
    auto time = sys_days{2026y / August / 10} + hours(12) + milliseconds(999);
    CHECK(FormatTimestamp(time) == "2026-08-10T12:00:00Z");
}

TEST_CASE("FormatTimestamp handles a year boundary", "[application][timestamp]") {
    auto time = sys_days{2025y / December / 31} + hours(23) + minutes(59) + seconds(59);
    CHECK(FormatTimestamp(time) == "2025-12-31T23:59:59Z");
}
