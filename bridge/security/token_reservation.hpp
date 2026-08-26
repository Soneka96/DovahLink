#pragma once

#include <functional>
#include <mutex>

namespace dovahlink::security {

///  Move-only, publicly-constructible reservation of a matching, unexpired
///  token pending session admission. Holds exclusive access to its
///  originating `TokenStore` for its lifetime; destruction without @ref
///  Commit releases that access and leaves the token unchanged. Unlike @ref
///  dovahlink::shared::ScopedRelease, the default (no explicit call) action
///  is to do nothing -- only an explicit @ref Commit call consumes the
///  token -- so it is a distinct, purpose-fit type rather than built on that
///  shared utility. Not nested in `TokenStore` and not `friend`-gated, so an
///  `ITokenStore` test double can construct one directly.
class TokenReservation {
  public:
    ///  Binds the lock held for this reservation's lifetime and the action
    ///  @ref Commit runs.
    TokenReservation(std::unique_lock<std::mutex> lock,
                     std::function<void()> commitAction) noexcept;

    ///  Prevents two reservations from controlling the same token.
    TokenReservation(const TokenReservation&) = delete;

    ///  Prevents copying control of reserved token material.
    TokenReservation& operator=(const TokenReservation&) = delete;

    ///  Transfers control of a matching token reservation.
    TokenReservation(TokenReservation&& other) noexcept;

    ///  Releases any current reservation, then transfers control.
    TokenReservation& operator=(TokenReservation&& other) noexcept;

    ///  Releases the token unchanged when the reservation was not committed.
    ~TokenReservation() = default;

    ///  Permanently consumes and securely clears the reserved token, then
    ///  releases the held lock; repeated calls are harmless.
    void Commit();

  private:
    ///  Exclusive store access held until commit or destruction.
    std::unique_lock<std::mutex> lock_;

    ///  Consumes and clears the reserved token; empty once already run.
    std::function<void()> commitAction_;
};

} //  namespace dovahlink::security
