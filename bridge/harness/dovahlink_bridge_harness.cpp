// A Skyrim-independent process that runs the real bridge stack (Coordinator,
// BridgeTransport, BridgeWorkerPool, and every security/application/protocol
// component beneath them) against a fake, deterministic character level
// instead of a running Skyrim process. Exists so integration/ scenarios (a
// separate, independent .NET client per TASK.md) can exercise the bridge's
// real transport and protocol behavior without requiring Skyrim, matching
// ai/context/integration/testing.md's "do not use a running Skyrim process
// for tests that only verify protocol mapping... or application state
// transitions."
//
// Reads DOVAHLINK_BRIDGE_TOKEN the same way the real plugin does and listens
// on the same documented port (application/bridge_config.hpp). Once ready,
// prints "READY" and reads newline-delimited commands from stdin so a
// scenario can change the underlying level on demand:
//
//   increase_level   Increments the harness's own level counter and pushes
//                    the new value into the shared character state store,
//                    the same call the real plugin's LevelIncreaseHandler
//                    would make. Per bridge/README.md's "Live event delivery
//                    is deferred to Phase 1.5", this is observable only by a
//                    client that asks again (subscribe or snapshot_request),
//                    never as an unprompted push -- prints "LEVEL <n>" once
//                    applied.
//   quit             Shuts the coordinator down and exits cleanly.
//
// A no-op CallbackRegistry stands in for the plugin's CommonLib event sink:
// the harness has no RE::LevelIncrease::Event to register, since level
// changes are driven entirely by the increase_level command instead.

#include "application/bridge_config.hpp"
#include "application/bridge_transport.hpp"
#include "application/bridge_worker_pool.hpp"
#include "application/character_state_store.hpp"
#include "application/coordinator.hpp"
#include "application/session.hpp"
#include "security/throttle.hpp"
#include "security/token_provider.hpp"
#include "security/token_store.hpp"
#include "transport/connection_slot.hpp"
#include "transport/listener.hpp"

#include <boost/asio/io_context.hpp>

#include <cstdint>
#include <iostream>
#include <string>
#include <utility>

namespace {

using dovahlink::application::kBridgePort;
using dovahlink::application::kTokenEnvVar;

class NoOpCallbackRegistry : public dovahlink::application::CallbackRegistry {
public:
    void RegisterAll() override {}
    void UnregisterAll() override {}
};

}  // namespace

int main() {
    dovahlink::security::WindowsEnvironmentReader environmentReader;
    auto tokenBytes = dovahlink::security::ReadTokenFromEnvironment(environmentReader, kTokenEnvVar);
    if (!tokenBytes.has_value()) {
        std::cerr << "DOVAHLINK_BRIDGE_TOKEN is not set to a valid 64-character hex-encoded "
                     "256-bit token; the harness cannot authenticate a client without it.\n";
        return 1;
    }

    boost::asio::io_context ioc;
    auto listenerV4Result = dovahlink::transport::LoopbackListener::Create(
        ioc, dovahlink::transport::LoopbackListener::IpVersion::kV4, kBridgePort);
    if (!listenerV4Result.has_value()) {
        std::cerr << "Failed to bind the IPv4 loopback listener on port " << kBridgePort << ".\n";
        return 1;
    }
    auto listenerV6Result = dovahlink::transport::LoopbackListener::Create(
        ioc, dovahlink::transport::LoopbackListener::IpVersion::kV6, kBridgePort);
    if (!listenerV6Result.has_value()) {
        std::cerr << "Failed to bind the IPv6 loopback listener on port " << kBridgePort << ".\n";
        return 1;
    }
    auto listenerV4 = std::move(*listenerV4Result);
    auto listenerV6 = std::move(*listenerV6Result);

    dovahlink::transport::ConnectionSlot connectionSlot;
    dovahlink::security::TokenStore tokenStore(std::move(*tokenBytes));
    dovahlink::security::FailedTokenThrottle tokenThrottle;
    dovahlink::application::SessionManager sessionManager;
    dovahlink::application::CharacterStateStore characterStateStore;

    NoOpCallbackRegistry callbackRegistry;
    dovahlink::application::BridgeTransport bridgeTransport(listenerV4, listenerV6);
    dovahlink::application::BridgeWorkerPool bridgeWorkerPool(listenerV4, listenerV6, connectionSlot, tokenStore,
                                                                tokenThrottle, sessionManager, characterStateStore);
    dovahlink::application::Coordinator coordinator(callbackRegistry, bridgeWorkerPool, bridgeTransport);

    coordinator.Start();
    std::cout << "READY" << std::endl;

    std::int64_t level = 5;
    characterStateStore.OnLevelCaptured(level);

    std::string line;
    while (std::getline(std::cin, line)) {
        if (line == "increase_level") {
            ++level;
            characterStateStore.OnLevelCaptured(level);
            std::cout << "LEVEL " << level << std::endl;
        } else if (line == "quit") {
            break;
        }
    }

    coordinator.Shutdown();
    return 0;
}
