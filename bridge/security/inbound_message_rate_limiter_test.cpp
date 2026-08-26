#include "security/inbound_message_rate_limiter.hpp"

#include <catch2/catch_test_macros.hpp>

#include <chrono>

using dovahlink::security::InboundMessageRateLimiter;

namespace {
///  Clock type used for deterministic sliding-window assertions.
using Clock = std::chrono::steady_clock;
} //  namespace

TEST_CASE(
    "InboundMessageRateLimiter allows the first 100 messages within one second",
    "[security][inbound_message_rate_limiter]") {
    InboundMessageRateLimiter limiter;
    Clock::time_point t0 = Clock::now();

    for (int i = 0; i < 100; ++i) {
        CHECK_FALSE(limiter.RecordMessageAndCheckLimit(t0));
    }
}

TEST_CASE(
    "InboundMessageRateLimiter rejects the 101st message within one second",
    "[security][inbound_message_rate_limiter]") {
    InboundMessageRateLimiter limiter;
    Clock::time_point t0 = Clock::now();

    for (int i = 0; i < 100; ++i) {
        REQUIRE_FALSE(limiter.RecordMessageAndCheckLimit(t0));
    }
    CHECK(limiter.RecordMessageAndCheckLimit(t0));
}

TEST_CASE(
    "InboundMessageRateLimiter allows messages again once a second has passed",
    "[security][inbound_message_rate_limiter]") {
    InboundMessageRateLimiter limiter;
    Clock::time_point t0 = Clock::now();

    for (int i = 0; i < 100; ++i) {
        REQUIRE_FALSE(limiter.RecordMessageAndCheckLimit(t0));
    }
    REQUIRE(limiter.RecordMessageAndCheckLimit(t0)); //  101st, blocked

    CHECK_FALSE(limiter.RecordMessageAndCheckLimit(t0 + std::chrono::seconds(1) +
                                                   std::chrono::milliseconds(1)));
}
