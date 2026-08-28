#include "application/capture_policy_registry.hpp"

#include <utility>

namespace dovahlink::application {

void CapturePolicyRegistry::Register(std::string key, CapturePolicy policy) {
    std::lock_guard<std::mutex> lock(mutex_);
    policies_.insert_or_assign(std::move(key), policy);
}

std::optional<CapturePolicy>
CapturePolicyRegistry::PolicyFor(const std::string& key) const {
    std::lock_guard<std::mutex> lock(mutex_);
    auto it = policies_.find(key);
    if (it == policies_.end()) {
        return std::nullopt;
    }
    return it->second;
}

} //  namespace dovahlink::application
