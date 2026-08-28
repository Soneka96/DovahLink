#pragma once

#include <mutex>
#include <string>
#include <unordered_set>

namespace dovahlink::application {

///  Bounds which state-area keys the bridge may publish or accept
///  subscriptions for. Registration is fixed-capacity per
///  `ai/context/protocol/security.md`'s "Input limits" (`kMaxRegisteredStateAreas`,
///  `bridge/application/constants.hpp`): an unregistered key never allocates
///  queue, barrier, Snapshot-slot, or dirty-marker state, and registration
///  past the bound fails closed rather than growing it.
class IRegisteredStateAreaPolicy {
  public:
    ///  Allows destruction through the interface.
    virtual ~IRegisteredStateAreaPolicy() = default;

    ///  Registers a state-area key, or reports that it is already registered.
    ///  A key already registered is a no-op success, not a failure.
    ///  @param stateArea Canonical state-area identifier.
    ///  @return `true` when the key is registered (already, or newly, within
    ///  the bound); `false` when the bound is already reached and this key
    ///  was not previously registered.
    virtual bool TryRegister(std::string stateArea) = 0;

    ///  Reports whether a state-area key is registered.
    ///  @param stateArea Canonical state-area identifier.
    [[nodiscard]] virtual bool
    IsRegistered(const std::string& stateArea) const = 0;
};

///  Stores the bridge-lifetime set of registered state-area keys for
///  bridge-lifetime consumers. Access is synchronized because registration
///  and lookup may occur from different threads.
class RegisteredStateAreaPolicy final : public IRegisteredStateAreaPolicy {
  public:
    ///  @copydoc IRegisteredStateAreaPolicy::TryRegister
    bool TryRegister(std::string stateArea) override;

    ///  @copydoc IRegisteredStateAreaPolicy::IsRegistered
    [[nodiscard]] bool
    IsRegistered(const std::string& stateArea) const override;

  private:
    ///  Synchronizes access to `registeredAreas_`.
    mutable std::mutex mutex_;

    ///  Registered state-area keys, bounded by `kMaxRegisteredStateAreas`.
    std::unordered_set<std::string> registeredAreas_;
};

} //  namespace dovahlink::application
