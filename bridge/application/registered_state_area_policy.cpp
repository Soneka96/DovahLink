#include "application/registered_state_area_policy.hpp"

#include "application/constants.hpp"

#include <utility>

namespace dovahlink::application {

bool RegisteredStateAreaPolicy::TryRegister(std::string stateArea) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (registeredAreas_.contains(stateArea)) {
        return true;
    }
    if (registeredAreas_.size() >= kMaxRegisteredStateAreas) {
        return false;
    }
    registeredAreas_.insert(std::move(stateArea));
    return true;
}

bool RegisteredStateAreaPolicy::IsRegistered(
    const std::string& stateArea) const {
    std::lock_guard<std::mutex> lock(mutex_);
    return registeredAreas_.contains(stateArea);
}

} //  namespace dovahlink::application
