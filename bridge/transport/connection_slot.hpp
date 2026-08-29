#pragma once

#include "shared/scoped_release.hpp"

#include <atomic>
#include <cstddef>
#include <optional>

namespace dovahlink::transport {

///  Narrow capability for admitting at most one connection at a time.
class IConnectionSlot {
  public:
    ///  Releases the interface without performing work.
    virtual ~IConnectionSlot() = default;

    ///  Atomically claims one available capacity unit, returning no release
    ///  when the configured capacity is occupied. The returned @ref
    ///  dovahlink::shared::ScopedRelease frees that unit when it is destroyed
    ///  or triggered.
    [[nodiscard]] virtual std::optional<shared::ScopedRelease> TryAcquire() = 0;

    ///  Returns whether the slot is currently occupied.
    [[nodiscard]] virtual bool IsOccupied() const = 0;
};

///  Thread-safe bounded connection-occupancy guard.
class ConnectionSlot : public IConnectionSlot {
  public:
    ///  Creates an unoccupied admission gate with the given capacity.
    ///  @param capacity Maximum number of simultaneous leases.
    explicit ConnectionSlot(std::size_t capacity = 1);

    ///  @copydoc IConnectionSlot::TryAcquire
    [[nodiscard]] std::optional<shared::ScopedRelease> TryAcquire() override;

    ///  @copydoc IConnectionSlot::IsOccupied
    [[nodiscard]] bool IsOccupied() const override;

  private:
    ///  Releases a slot owned by a live lease.
    void Release() noexcept;

    ///  Maximum number of simultaneous leases.
    const std::size_t capacity_;

    ///  Atomic occupancy count shared by racing connection attempts.
    std::atomic<std::size_t> occupied_{0};
};

} //  namespace dovahlink::transport
