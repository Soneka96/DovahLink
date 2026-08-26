#pragma once

#include "security/rate_window_counter.hpp"

#include <chrono>

namespace dovahlink::security {

///  Move-only reservation of one failed-authentication attempt slot. The slot
///  remains occupied until @ref Commit records the attempt or destruction
///  releases it. The referenced counter must outlive this reservation.
class FailedTokenReservation {
  public:
    ///  Binds an already-acquired slot to its originating counter.
    ///  @param counter Counter that owns the reserved slot for this object's
    ///      lifetime.
    ///  @param now Timestamp used if the reservation is committed.
    FailedTokenReservation(IRateWindowCounter& counter,
                           std::chrono::steady_clock::time_point now) noexcept;

    ///  Prevents two reservations from controlling one attempt slot.
    FailedTokenReservation(const FailedTokenReservation&) = delete;

    ///  Prevents copying reservation ownership.
    FailedTokenReservation& operator=(const FailedTokenReservation&) = delete;

    ///  Transfers reservation ownership from `other`.
    FailedTokenReservation(FailedTokenReservation&& other) noexcept;

    ///  Releases this reservation, then transfers ownership from `other`.
    FailedTokenReservation&
    operator=(FailedTokenReservation&& other) noexcept;

    ///  Releases an uncommitted slot without recording a failure.
    ~FailedTokenReservation();

    ///  Records the failed attempt and releases its reserved slot.
    ///  Repeated calls are harmless. If recording fails, the reservation stays
    ///  owned so destruction can release the slot.
    void Commit();

  private:
    ///  Releases this object's uncommitted slot and clears its ownership.
    void Release() noexcept;

    ///  Counter that owns this reservation, or `nullptr` after completion.
    IRateWindowCounter* counter_;

    ///  Timestamp recorded when this reservation commits.
    std::chrono::steady_clock::time_point now_;
};

} //  namespace dovahlink::security
