#pragma once

#include "security/failed_token_reservation.hpp"
#include "security/rate_window_counter.hpp"

#include <chrono>
#include <optional>

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

    ///  Atomically reserves one failed-attempt slot for credential validation.
    ///  The returned reservation must be committed for an invalid credential
    ///  or released for a successful one.
    ///  @param now Timestamp used to prune expired failures before admission.
    ///  @return A reservation when capacity remains, or no value when the
    ///      configured limit is already occupied.
    [[nodiscard]] virtual std::optional<FailedTokenReservation>
    TryReserve(std::chrono::steady_clock::time_point now) = 0;
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

    ///  @copydoc IFailedTokenThrottle::TryReserve
    [[nodiscard]] std::optional<FailedTokenReservation>
    TryReserve(std::chrono::steady_clock::time_point now) override;

  private:
    ///  Counter tracking failed attempts across all connections.
    RateWindowCounter counter_;
};

} //  namespace dovahlink::security
