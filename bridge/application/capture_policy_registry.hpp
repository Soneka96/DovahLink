#pragma once

#include "application/capture_policy.hpp"

#include <mutex>
#include <optional>
#include <string>
#include <unordered_map>

namespace dovahlink::application {

///  Records the `CapturePolicy` declared for each captured value.
class ICapturePolicyRegistry {
  public:
    ///  Allows destruction through the interface.
    virtual ~ICapturePolicyRegistry() = default;

    ///  Records the policy for a captured-value key, replacing any policy
    ///  previously registered for the same key.
    ///  @param key Canonical identifier for the captured value.
    ///  @param policy Capture policy declared for that value.
    virtual void Register(std::string key, CapturePolicy policy) = 0;

    ///  Returns the registered policy for a captured-value key.
    ///  @param key Canonical identifier for the captured value.
    ///  @return The registered policy, or no value when the key is
    ///  unregistered.
    [[nodiscard]] virtual std::optional<CapturePolicy>
    PolicyFor(const std::string& key) const = 0;
};

///  Stores the declared capture policy per captured-value key for
///  bridge-lifetime consumers. Access is synchronized because registration
///  and lookup may occur from different threads.
class CapturePolicyRegistry final : public ICapturePolicyRegistry {
  public:
    ///  @copydoc ICapturePolicyRegistry::Register
    void Register(std::string key, CapturePolicy policy) override;

    ///  @copydoc ICapturePolicyRegistry::PolicyFor
    [[nodiscard]] std::optional<CapturePolicy>
    PolicyFor(const std::string& key) const override;

  private:
    ///  Synchronizes access to `policies_`.
    mutable std::mutex mutex_;

    ///  Registered policy per captured-value key.
    std::unordered_map<std::string, CapturePolicy> policies_;
};

} //  namespace dovahlink::application
