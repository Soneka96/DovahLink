#pragma once

#include <atomic>

namespace dovahlink::transport {

/// Thread-safe single-connection occupancy guard.
class ConnectionSlot {
public:
    /// Creates an unoccupied slot.
    ConnectionSlot() = default;

    /// Atomically claims the slot, returning `false` when already occupied.
    [[nodiscard]] bool TryAcquire();

    /// Releases the slot; releasing an unoccupied slot is harmless.
    void Release();

    /// Returns whether the slot is currently occupied.
    [[nodiscard]] bool IsOccupied() const;

private:
    /// Atomic occupancy state shared by racing connection attempts.
    std::atomic<bool> occupied_{false};
};

}  // namespace dovahlink::transport
