#pragma once

#include <chrono>
#include <cstdint>
#include <mutex>
#include <optional>
#include <vector>

namespace dovahlink::security {

/// Thread-safe one-time token store with expiry and secure memory clearing.
class TokenStore {
public:
    /// Move-only reservation of a matching token pending session admission.
    ///
    /// The originating @ref TokenStore must outlive the reservation.
    class Reservation {
    public:
        /// Prevents two reservations from controlling the same token.
        Reservation(const Reservation&) = delete;

        /// Prevents copying control of reserved token material.
        Reservation& operator=(const Reservation&) = delete;

        /// Transfers control of a matching token reservation.
        Reservation(Reservation&& other) noexcept;

        /// Releases any current reservation, then transfers control.
        Reservation& operator=(Reservation&& other) noexcept;

        /// Releases the token unchanged when the reservation was not committed.
        ~Reservation() = default;

        /// Permanently consumes and securely clears the reserved token; repeated calls are harmless.
        void Commit();

    private:
        friend class TokenStore;

        /// Creates a reservation while holding exclusive access to `store`.
        Reservation(TokenStore& store, std::unique_lock<std::mutex> lock) noexcept;

        /// Store whose token is reserved, or null after move or commit.
        TokenStore* store_;

        /// Exclusive store access retained until rollback or commit.
        std::unique_lock<std::mutex> lock_;
    };

    /// Stores the expected token and starts its time-to-live countdown.
    explicit TokenStore(std::vector<std::uint8_t> expectedToken,
                        std::chrono::steady_clock::duration timeToLive = std::chrono::minutes(5));

    /// Clears any remaining token material.
    ~TokenStore();

    /// Prevents copying token material between stores.
    TokenStore(const TokenStore&) = delete;
    /// Prevents assigning token material between stores.
    TokenStore& operator=(const TokenStore&) = delete;

    /// Compares and atomically reserves a matching, unexpired token.
    /// Comparison is constant-time for equal-length values; mismatches,
    /// expiry, prior consumption, and empty configured tokens return no reservation.
    [[nodiscard]] std::optional<Reservation> TryReserve(const std::vector<std::uint8_t>& presented);

    /// Reports whether the token remains available, clearing it on expiry.
    [[nodiscard]] bool IsAvailable();

private:
    /// Checks availability and clears expired token material while locked.
    bool IsAvailableLocked();

    /// Serializes access to token state.
    std::mutex mutex_;
    /// Stored token material, cleared after consumption or expiry.
    std::vector<std::uint8_t> token_;
    /// Monotonic expiration deadline.
    std::chrono::steady_clock::time_point expiresAt_;
    /// Whether the token has been consumed or expired.
    bool consumed_ = false;
};

}  // namespace dovahlink::security
