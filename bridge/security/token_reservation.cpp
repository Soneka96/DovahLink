#include "security/token_reservation.hpp"

#include <utility>

namespace dovahlink::security {

TokenReservation::TokenReservation(std::unique_lock<std::mutex> lock,
                                   std::function<void()> commitAction) noexcept
    : lock_(std::move(lock)), commitAction_(std::move(commitAction)) {}

TokenReservation::TokenReservation(TokenReservation&& other) noexcept
    : lock_(std::move(other.lock_)),
      commitAction_(std::move(other.commitAction_)) {
    other.commitAction_ = nullptr;
}

TokenReservation&
TokenReservation::operator=(TokenReservation&& other) noexcept {
    if (this != &other) {
        //  std::unique_lock's own move assignment unlocks *this's currently
        //  held mutex, if any, before taking over `other`'s -- an overwritten,
        //  uncommitted reservation is abandoned (mutex released, token
        //  unchanged), matching what destroying it without Commit() does.
        lock_ = std::move(other.lock_);
        commitAction_ = std::move(other.commitAction_);
        other.commitAction_ = nullptr;
    }
    return *this;
}

void TokenReservation::Commit() {
    if (!commitAction_) {
        return;
    }
    auto action = std::move(commitAction_);
    commitAction_ = nullptr;
    action();
    lock_.unlock();
}

} //  namespace dovahlink::security
