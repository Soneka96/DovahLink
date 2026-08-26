#pragma once

#include "application/contained_work.hpp"

namespace dovahlink::application {

///  Registers and unregisters runtime callbacks owned by the coordinator.
class IBridgeCallbackRegistry {
  public:
    ///  Releases the interface without performing work.
    virtual ~IBridgeCallbackRegistry() = default;

    ///  Registers all callbacks required by the bridge.
    ///  @param callbackRunner Guarded containment boundary retained by callbacks.
    virtual void RegisterAll(ContainedWorkRunner callbackRunner) = 0;

    ///  Unregisters all callbacks before owned state is destroyed.
    virtual void UnregisterAll() = 0;
};

} //  namespace dovahlink::application
