#include "plugin/warning_rate_limit.hpp"

#include <catch2/catch_test_macros.hpp>

using dovahlink::adapter::plugin::HasWarningIntervalElapsed;

TEST_CASE("warning rate limit admits the first log", "[plugin][logging]") {
    CHECK(HasWarningIntervalElapsed(0, 10'000, 30'000));
}

TEST_CASE("warning rate limit suppresses logs before the interval boundary",
          "[plugin][logging]") {
    CHECK_FALSE(HasWarningIntervalElapsed(10'000, 39'999, 30'000));
}

TEST_CASE("warning rate limit admits a log at and after the boundary",
          "[plugin][logging]") {
    CHECK(HasWarningIntervalElapsed(10'000, 40'000, 30'000));
    CHECK(HasWarningIntervalElapsed(10'000, 45'000, 30'000));
}
