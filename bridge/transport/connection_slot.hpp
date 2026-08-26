#pragma once

#include "shared/scoped_release.hpp"

#include <atomic>
#include <optional>

namespace dovahlink::transport {

///  Narrow capability for admitting at most one connection at a time.
class IConnectionSlot {
  public:
    ///  Releases the interface without performing work.
    virtual ~IConnectionSlot() = default;

    ///  Atomically claims the slot, returning no release when already
    ///  occupied. The returned @ref dovahlink::shared::ScopedRelease frees
    ///  the slot when it is destroyed or triggered.
    [[nodiscard]] virtual std::optional<shared::ScopedRelease> TryAcquire() = 0;

    ///  Returns whether the slot is currently occupied.
    [[nodiscard]] virtual bool IsOccupied() const = 0;
};

///  Thread-safe single-connection occupancy guard.
class ConnectionSlot : public IConnectionSlot {
  public:
    ///  Creates an unoccupied slot.
    ConnectionSlot() = default;

    ///  @copydoc IConnectionSlot::TryAcquire
    [[nodiscard]] std::optional<shared::ScopedRelease> TryAcquire() override;

    ///  @copydoc IConnectionSlot::IsOccupied
    [[nodiscard]] bool IsOccupied() const override;

  private:
    ///  Releases a slot owned by a live lease.
    void Release() noexcept;

    ///  Atomic occupancy state shared by racing connection attempts.
    std::atomic<bool> occupied_{false};
};

} //  namespace dovahlink::transport
