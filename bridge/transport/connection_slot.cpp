#include "transport/connection_slot.hpp"

namespace dovahlink::transport {

/**
 * @brief Attempts to acquire the connection slot.
 *
 * @return `true` if the slot was acquired, `false` if it was already occupied.
 */
bool ConnectionSlot::TryAcquire() {
    bool expected = false;
    return occupied_.compare_exchange_strong(expected, true, std::memory_order_acq_rel);
}

/**
 * @brief Marks the connection slot as unoccupied.
 */
void ConnectionSlot::Release() {
    occupied_.store(false, std::memory_order_release);
}

/**
 * @brief Determines whether the connection slot is occupied.
 *
 * @return `true` if the slot is occupied, `false` otherwise.
 */
bool ConnectionSlot::IsOccupied() const {
    return occupied_.load(std::memory_order_acquire);
}

}  // namespace dovahlink::transport
