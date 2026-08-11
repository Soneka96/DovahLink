#include "transport/connection_slot.hpp"

namespace dovahlink::transport {

bool ConnectionSlot::TryAcquire() {
    bool expected = false;
    return occupied_.compare_exchange_strong(expected, true, std::memory_order_acq_rel);
}

void ConnectionSlot::Release() {
    occupied_.store(false, std::memory_order_release);
}

bool ConnectionSlot::IsOccupied() const {
    return occupied_.load(std::memory_order_acquire);
}

}  // namespace dovahlink::transport
