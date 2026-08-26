#pragma once

#include "security/token_reservation.hpp"

#include <chrono>
#include <cstdint>
#include <mutex>
#include <optional>
#include <vector>

namespace dovahlink::security {

///  Narrow capability for reserving and inspecting one plugin-lifetime
///  one-time authentication token.
class ITokenStore {
  public:
    ///  Releases the interface without performing work.
    virtual ~ITokenStore() = default;

    ///  Compares and atomically reserves a matching, unexpired token.
    ///  Comparison is constant-time for equal-length values; mismatches,
    ///  expiry, prior consumption, and empty configured tokens return no
    ///  reservation.
    [[nodiscard]] virtual std::optional<TokenReservation>
    TryReserve(const std::vector<std::uint8_t>& presented) = 0;

    ///  Reports whether the token remains available, clearing it on expiry.
    [[nodiscard]] virtual bool IsAvailable() = 0;

    ///  Returns the token's remaining validity, or `std::nullopt` if consumed
    ///  or expired (clearing it on expiry, matching @ref IsAvailable).
    [[nodiscard]] virtual std::optional<std::chrono::seconds>
    RemainingSeconds() = 0;
};

///  Thread-safe one-time token store with expiry and secure memory clearing.
class TokenStore : public ITokenStore {
  public:
    ///  Stores the expected token and starts its time-to-live countdown.
    explicit TokenStore(
        std::vector<std::uint8_t> expectedToken,
        std::chrono::steady_clock::duration timeToLive = std::chrono::minutes(5));

    ///  Clears any remaining token material.
    ~TokenStore() override;

    ///  Prevents copying token material between stores.
    TokenStore(const TokenStore&) = delete;
    ///  Prevents assigning token material between stores.
    TokenStore& operator=(const TokenStore&) = delete;

    ///  @copydoc ITokenStore::TryReserve
    [[nodiscard]] std::optional<TokenReservation>
    TryReserve(const std::vector<std::uint8_t>& presented) override;

    ///  @copydoc ITokenStore::IsAvailable
    [[nodiscard]] bool IsAvailable() override;

    ///  @copydoc ITokenStore::RemainingSeconds
    [[nodiscard]] std::optional<std::chrono::seconds> RemainingSeconds() override;

  private:
    ///  Checks availability and clears expired token material while locked.
    bool IsAvailableLocked();

    ///  Serializes access to token state.
    std::mutex mutex_;
    ///  Stored token material, cleared after consumption or expiry.
    std::vector<std::uint8_t> token_;
    ///  Monotonic expiration deadline.
    std::chrono::steady_clock::time_point expiresAt_;
    ///  Whether the token has been consumed or expired.
    bool consumed_ = false;
};

} //  namespace dovahlink::security
