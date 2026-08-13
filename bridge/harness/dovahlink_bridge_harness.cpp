// Skyrim-independent process harness for the real bridge stack. It uses a
// deterministic character level, accepts the same authentication token as the
// plugin, prints READY after startup, and handles increase_level and quit
// commands on standard input.

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

#include <chrono>
#include <cstdint>
#include <iostream>
#include <optional>
#include <string>
#include <utility>

namespace {

using dovahlink::application::kBridgePort;
using dovahlink::application::kTokenEnvVar;

constexpr const char* kTokenTtlEnvVar = "DOVAHLINK_HARNESS_TOKEN_TTL_SECONDS";

/// Provides no-op callback registration for the Skyrim-independent harness.
class NoOpCallbackRegistry : public dovahlink::application::CallbackRegistry {
public:
    /// @copydoc dovahlink::application::CallbackRegistry::RegisterAll
    void RegisterAll() override {}
    /// @copydoc dovahlink::application::CallbackRegistry::UnregisterAll
    void UnregisterAll() override {}
};

/// Reads a positive harness-only token lifetime override from the environment.
std::optional<std::chrono::steady_clock::duration> ReadTokenTtlOverride(
    const dovahlink::security::EnvironmentReader& env) {
    auto raw = env.Read(kTokenTtlEnvVar);
    if (!raw.has_value() || raw->empty()) {
        return std::nullopt;
    }
    try {
        long long seconds = std::stoll(*raw);
        if (seconds <= 0) {
            return std::nullopt;
        }
        return std::chrono::seconds(seconds);
    } catch (const std::exception&) {
        return std::nullopt;
    }
}

}  // namespace
/// Runs the standalone bridge integration harness.
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
    auto tokenTtl = ReadTokenTtlOverride(environmentReader).value_or(std::chrono::minutes(5));
    dovahlink::security::TokenStore tokenStore(std::move(*tokenBytes), tokenTtl);
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
