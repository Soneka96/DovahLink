// Skyrim-independent process harness for the real bridge stack. It uses a
// deterministic character level, accepts the same authentication token as the
// plugin, prints READY followed by its generated bridge instance ID after
// startup, and handles increase_level, new_game, load_game, load_game_fail,
// revert, and quit commands on standard input.

#include "application/bridge_config.hpp"
#include "application/bridge_transport.hpp"
#include "application/bridge_worker_pool.hpp"
#include "application/character_state_store.hpp"
#include "application/coordinator.hpp"
#include "application/game_lifecycle_tracker.hpp"
#include "application/play_context.hpp"
#include "application/session.hpp"
#include "security/csprng.hpp"
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
    void RegisterAll(dovahlink::application::ContainedWorkRunner) override {}
    /// @copydoc dovahlink::application::CallbackRegistry::UnregisterAll
    void UnregisterAll() override {}
};

/// Processes one lifecycle event through the tracker and applies its
/// resulting transition to the active play context, mirroring
/// game_state/commonlib_game_lifecycle_sink.cpp's per-signal handling without
/// any SKSE dependency.
/// @return The transition produced by this event.
dovahlink::application::GameLifecycleTracker::Transition ProcessLifecycleEvent(
    dovahlink::application::GameLifecycleTracker& tracker,
    dovahlink::application::ActivePlayContext& activePlayContext, dovahlink::application::LifecycleEvent event) {
    auto transition = tracker.HandleEvent(event);
    dovahlink::application::ApplyLifecycleTransition(activePlayContext, transition);
    return transition;
}

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
    dovahlink::application::GameLifecycleTracker lifecycleTracker;
    dovahlink::application::ActivePlayContext activePlayContext;

    NoOpCallbackRegistry callbackRegistry;
    dovahlink::application::BridgeTransport bridgeTransport(listenerV4, listenerV6);
    dovahlink::application::BridgeWorkerPool bridgeWorkerPool(listenerV4, listenerV6, connectionSlot, tokenStore,
                                                              tokenThrottle, sessionManager, characterStateStore,
                                                              activePlayContext);
    dovahlink::application::Coordinator coordinator(callbackRegistry, bridgeWorkerPool, bridgeTransport);

    coordinator.Start();
    std::cout << "READY" << std::endl;

    // Skyrim-independent stand-in for the real plugin's bridgeInstanceId
    // generation (dovahlink_bridge_plugin.cpp): a fresh identity per harness
    // process launch, printed so a process-boundary test can observe it
    // changing across a kill-and-relaunch cycle without a running Skyrim.
    auto bridgeInstanceId = dovahlink::security::GenerateOpaqueId();
    std::cout << "BRIDGE_INSTANCE " << bridgeInstanceId.value_or("(unavailable)") << std::endl;

    std::int64_t level = 5;
    characterStateStore.OnLevelCaptured(level);

    std::string line;
    while (std::getline(std::cin, line)) {
        if (line == "increase_level") {
            ++level;
            characterStateStore.OnLevelCaptured(level);
            std::cout << "LEVEL " << level << std::endl;
        } else if (line == "new_game") {
            auto transition =
                ProcessLifecycleEvent(lifecycleTracker, activePlayContext, dovahlink::application::LifecycleEvent::kNewGame);
            std::cout << "PLAY_CONTEXT " << transition.newPlayContextId.value_or("(none)") << std::endl;
        } else if (line == "load_game") {
            // Two raw signals in sequence, matching a real SKSE load: the
            // save is selected (kPreLoadGame, invalidating any current
            // context) before it finishes loading (kPostLoadGameSuccess,
            // minting the new one). Only the final transition's ID is
            // reported; both are applied to activePlayContext.
            ProcessLifecycleEvent(lifecycleTracker, activePlayContext,
                                  dovahlink::application::LifecycleEvent::kPreLoadGame);
            auto transition = ProcessLifecycleEvent(lifecycleTracker, activePlayContext,
                                                    dovahlink::application::LifecycleEvent::kPostLoadGameSuccess);
            std::cout << "PLAY_CONTEXT " << transition.newPlayContextId.value_or("(none)") << std::endl;
        } else if (line == "load_game_fail") {
            // Same two-signal shape as load_game, but the load itself fails:
            // kPreLoadGame still invalidates any current context, and
            // kPostLoadGameFailure settles into kNoContext without minting a
            // replacement.
            ProcessLifecycleEvent(lifecycleTracker, activePlayContext,
                                  dovahlink::application::LifecycleEvent::kPreLoadGame);
            auto transition = ProcessLifecycleEvent(lifecycleTracker, activePlayContext,
                                                    dovahlink::application::LifecycleEvent::kPostLoadGameFailure);
            std::cout << "PLAY_CONTEXT " << transition.newPlayContextId.value_or("(none)") << std::endl;
        } else if (line == "revert") {
            auto transition =
                ProcessLifecycleEvent(lifecycleTracker, activePlayContext, dovahlink::application::LifecycleEvent::kRevert);
            std::cout << "PLAY_CONTEXT " << transition.newPlayContextId.value_or("(none)") << std::endl;
        } else if (line == "quit") {
            break;
        }
    }

    coordinator.Shutdown();
    return 0;
}
