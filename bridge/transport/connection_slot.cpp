#include "transport/connection_slot.hpp"

namespace dovahlink::transport {

ConnectionSlot::ConnectionSlot(std::size_t capacity) : capacity_(capacity) {}

std::optional<shared::ScopedRelease> ConnectionSlot::TryAcquire() {
    std::size_t occupied = occupied_.load(std::memory_order_acquire);
    while (occupied < capacity_ &&
           !occupied_.compare_exchange_weak(occupied, occupied + 1,
                                            std::memory_order_acq_rel,
                                            std::memory_order_acquire)) {
    }
    if (occupied >= capacity_) {
        return std::nullopt;
    }
    return shared::ScopedRelease([this] { Release(); });
}

void ConnectionSlot::Release() noexcept {
    occupied_.fetch_sub(1, std::memory_order_release);
}

bool ConnectionSlot::IsOccupied() const {
    return occupied_.load(std::memory_order_acquire) != 0;
}

} //  namespace dovahlink::transport
