#include "transport/connection_slot.hpp"

namespace dovahlink::transport {

ConnectionSlot::Lease::Lease(ConnectionSlot& slot) noexcept : slot_(&slot) {}

ConnectionSlot::Lease::Lease(Lease&& other) noexcept : slot_(other.slot_) {
    other.slot_ = nullptr;
}

ConnectionSlot::Lease&
ConnectionSlot::Lease::operator=(Lease&& other) noexcept {
    if (this != &other) {
        Reset();
        slot_ = other.slot_;
        other.slot_ = nullptr;
    }
    return *this;
}

ConnectionSlot::Lease::~Lease() { Reset(); }

void ConnectionSlot::Lease::Reset() noexcept {
    if (slot_ != nullptr) {
        slot_->Release();
        slot_ = nullptr;
    }
}

std::optional<ConnectionSlot::Lease> ConnectionSlot::TryAcquire() {
    bool expected = false;
    if (!occupied_.compare_exchange_strong(expected, true,
                                           std::memory_order_acq_rel)) {
        return std::nullopt;
    }
    return Lease(*this);
}

void ConnectionSlot::Release() noexcept {
    occupied_.store(false, std::memory_order_release);
}

bool ConnectionSlot::IsOccupied() const {
    return occupied_.load(std::memory_order_acquire);
}

} //  namespace dovahlink::transport
