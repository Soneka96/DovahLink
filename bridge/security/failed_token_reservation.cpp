#include "security/failed_token_reservation.hpp"

#include <utility>

namespace dovahlink::security {

FailedTokenReservation::FailedTokenReservation(
    IRateWindowCounter& counter,
    std::chrono::steady_clock::time_point now) noexcept
    : counter_(&counter), now_(now) {}

FailedTokenReservation::FailedTokenReservation(
    FailedTokenReservation&& other) noexcept
    : counter_(std::exchange(other.counter_, nullptr)), now_(other.now_) {}

FailedTokenReservation& FailedTokenReservation::operator=(
    FailedTokenReservation&& other) noexcept {
    if (this != &other) {
        Release();
        counter_ = std::exchange(other.counter_, nullptr);
        now_ = other.now_;
    }
    return *this;
}

FailedTokenReservation::~FailedTokenReservation() { Release(); }

void FailedTokenReservation::Commit() {
    if (counter_ == nullptr) {
        return;
    }

    counter_->CommitReservation(now_);
    counter_ = nullptr;
}

void FailedTokenReservation::Release() noexcept {
    if (counter_ != nullptr) {
        counter_->ReleaseReservation();
        counter_ = nullptr;
    }
}

} //  namespace dovahlink::security
