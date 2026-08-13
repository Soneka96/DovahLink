#pragma once

#include <atomic>
#include <optional>

namespace dovahlink::transport {

/// Thread-safe single-connection occupancy guard.
class ConnectionSlot {
public:
    /// Move-only ownership of an acquired connection slot.
    ///
    /// The originating @ref ConnectionSlot must outlive the lease.
    class Lease {
    public:
        /// Prevents two leases from owning the same acquired slot.
        Lease(const Lease&) = delete;

        /// Prevents copying ownership of an acquired slot.
        Lease& operator=(const Lease&) = delete;

        /// Transfers ownership of an acquired slot.
        Lease(Lease&& other) noexcept;

        /// Releases the currently owned slot, then transfers ownership.
        Lease& operator=(Lease&& other) noexcept;

        /// Releases the acquired slot.
        ~Lease();

    private:
        friend class ConnectionSlot;

        /// Creates a lease that owns `slot` until destruction.
        explicit Lease(ConnectionSlot& slot) noexcept;

        /// Releases the owned slot, if any.
        void Reset() noexcept;

        /// Borrowed slot released when this lease is destroyed.
        ConnectionSlot* slot_;
    };

    /// Creates an unoccupied slot.
    ConnectionSlot() = default;

    /// Atomically claims the slot, returning no lease when already occupied.
    [[nodiscard]] std::optional<Lease> TryAcquire();

    /// Returns whether the slot is currently occupied.
    [[nodiscard]] bool IsOccupied() const;

private:
    /// Releases a slot owned by a live lease.
    void Release() noexcept;

    /// Atomic occupancy state shared by racing connection attempts.
    std::atomic<bool> occupied_{false};
};

}  // namespace dovahlink::transport
