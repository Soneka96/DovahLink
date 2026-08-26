#include "security/inbound_message_rate_limiter.hpp"

#include "security/limits.hpp"

namespace dovahlink::security {

InboundMessageRateLimiter::InboundMessageRateLimiter()
    : counter_(kInboundMessageRateWindow) {}

bool InboundMessageRateLimiter::RecordMessageAndCheckLimit(
    std::chrono::steady_clock::time_point now) {
    return counter_.RecordEvent(now) > kMaxInboundMessagesPerSecond;
}

} //  namespace dovahlink::security
