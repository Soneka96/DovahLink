#include "application/bridge_callback_registry.hpp"

#include <utility>

namespace dovahlink::application {

BridgeCallbackRegistry::BridgeCallbackRegistry(
    game_state::ICommonLibLevelIncreaseSink& sink)
    : sink_(sink) {}

void BridgeCallbackRegistry::RegisterAll(ContainedWorkRunner callbackRunner) {
    sink_.Register(std::move(callbackRunner));
}

void BridgeCallbackRegistry::UnregisterAll() { sink_.Unregister(); }

} //  namespace dovahlink::application
