#include "transport/connection_slot.hpp"

namespace dovahlink::transport {

std::optional<shared::ScopedRelease> ConnectionSlot::TryAcquire() {
    bool expected = false;
    if (!occupied_.compare_exchange_strong(expected, true,
                                           std::memory_order_acq_rel)) {
        return std::nullopt;
    }
    return shared::ScopedRelease([this] { Release(); });
}

void ConnectionSlot::Release() noexcept {
    occupied_.store(false, std::memory_order_release);
}

bool ConnectionSlot::IsOccupied() const {
    return occupied_.load(std::memory_order_acquire);
}

} //  namespace dovahlink::transport
