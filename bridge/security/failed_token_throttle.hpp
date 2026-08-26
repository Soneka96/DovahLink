#pragma once

#include "security/rate_window_counter.hpp"

#include <chrono>

namespace dovahlink::security {

///  Narrow capability for pacing failed one-time-token authentication
///  attempts.
class IFailedTokenThrottle {
  public:
    ///  Releases the interface without performing work.
    virtual ~IFailedTokenThrottle() = default;

    ///  Reports whether authentication attempts are currently blocked.
    [[nodiscard]] virtual bool
    IsBlocked(std::chrono::steady_clock::time_point now) = 0;

    ///  Records one failed authentication attempt.
    virtual void RecordFailure(std::chrono::steady_clock::time_point now) = 0;
};

///  Global failed-token attempt throttle shared across connection attempts.
class FailedTokenThrottle : public IFailedTokenThrottle {
  public:
    ///  Creates a throttle using the configured failed-token window.
    FailedTokenThrottle();

    ///  @copydoc IFailedTokenThrottle::IsBlocked
    [[nodiscard]] bool IsBlocked(std::chrono::steady_clock::time_point now) override;

    ///  @copydoc IFailedTokenThrottle::RecordFailure
    void RecordFailure(std::chrono::steady_clock::time_point now) override;

  private:
    ///  Counter tracking failed attempts across all connections.
    RateWindowCounter counter_;
};

} //  namespace dovahlink::security
