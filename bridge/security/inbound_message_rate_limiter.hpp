#pragma once

#include "security/rate_window_counter.hpp"

#include <chrono>

namespace dovahlink::security {

///  Narrow capability for pacing per-connection inbound message volume.
class IInboundMessageRateLimiter {
  public:
    ///  Releases the interface without performing work.
    virtual ~IInboundMessageRateLimiter() = default;

    ///  Records a message and reports whether the rate limit is exceeded.
    [[nodiscard]] virtual bool
    RecordMessageAndCheckLimit(std::chrono::steady_clock::time_point now) = 0;
};

///  Per-connection inbound message-rate limiter.
class InboundMessageRateLimiter : public IInboundMessageRateLimiter {
  public:
    ///  Creates a limiter using the configured inbound-message window.
    InboundMessageRateLimiter();

    ///  @copydoc IInboundMessageRateLimiter::RecordMessageAndCheckLimit
    [[nodiscard]] bool RecordMessageAndCheckLimit(
        std::chrono::steady_clock::time_point now) override;

  private:
    ///  Counter tracking messages for one connection.
    RateWindowCounter counter_;
};

} //  namespace dovahlink::security
