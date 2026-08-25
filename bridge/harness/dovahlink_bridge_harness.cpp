//  Skyrim-independent process harness for the real bridge stack. It uses a
//  deterministic character level, accepts the same authentication token as the
//  plugin, prints READY followed by its generated bridge instance ID and the
//  loopback port it actually bound after startup, and handles increase_level,
//  new_game, load_game, load_game_fail, revert, revoke <clientId>,
//  block <clientId>, unblock <clientId>, trust_reset <clientId>,
//  factory_reset, and quit commands on standard input.

#include "application/bridge_config.hpp"
#include "application/bridge_transport.hpp"
#include "application/bridge_worker_pool.hpp"
#include "application/coordinator.hpp"
#include "application/game_lifecycle_tracker.hpp"
#include "application/pairing_notification_sink.hpp"
#include "application/play_context.hpp"
#include "application/session.hpp"
#include "security/csprng.hpp"
#include "security/pairing_session.hpp"
#include "security/throttle.hpp"
#include "security/token_provider.hpp"
#include "security/token_store.hpp"
#include "security/trust_store.hpp"
#include "security/windows_trust_store_persistence.hpp"
#include "transport/connection_slot.hpp"
#include "transport/listener.hpp"

#include <boost/asio/io_context.hpp>

#include <chrono>
#include <cstdint>
#include <filesystem>
#include <iostream>
#include <limits>
#include <optional>
#include <string>
#include <string_view>
#include <utility>

namespace {

using dovahlink::application::kBridgePort;
using dovahlink::application::kBridgeVersion;
using dovahlink::application::kTokenEnvVar;

constexpr const char* kTokenTtlEnvVar = "DOVAHLINK_HARNESS_TOKEN_TTL_SECONDS";
constexpr const char* kPlayContextIdOverrideEnvVar =
    "DOVAHLINK_HARNESS_PLAY_CONTEXT_ID_OVERRIDE";
constexpr const char* kTrustStorePathOverrideEnvVar =
    "DOVAHLINK_HARNESS_TRUST_STORE_PATH_OVERRIDE";
constexpr const char* kPortOverrideEnvVar = "DOVAHLINK_HARNESS_PORT_OVERRIDE";
constexpr std::string_view kRevokeCommandPrefix = "revoke ";
constexpr std::string_view kBlockCommandPrefix = "block ";
constexpr std::string_view kUnblockCommandPrefix = "unblock ";
constexpr std::string_view kTrustResetCommandPrefix = "trust_reset ";
constexpr std::string_view kFactoryResetCommand = "factory_reset";

///  Provides no-op callback registration for the Skyrim-independent harness.
class NoOpCallbackRegistry : public dovahlink::application::CallbackRegistry {
  public:
    ///  @copydoc dovahlink::application::CallbackRegistry::RegisterAll
    void RegisterAll(dovahlink::application::ContainedWorkRunner) override {}
    ///  @copydoc dovahlink::application::CallbackRegistry::UnregisterAll
    void UnregisterAll() override {}
};

///  Displays a freshly generated pairing code by printing it to stdout, matching
///  the harness's existing "print an observable signal for the test driver"
///  convention (READY, BRIDGE_INSTANCE, LEVEL, PLAY_CONTEXT above). Stands in
///  for the real Skyrim notification (stage G); a .NET validation-client
///  scenario reads this line the same way it already reads the others.
class StdoutPairingNotificationSink
    : public dovahlink::application::PairingNotificationSink {
  public:
    ///  @copydoc
    ///  dovahlink::application::PairingNotificationSink::NotifyPairingCodeAvailable
    void NotifyPairingCodeAvailable(std::string_view sixDigitCode) override {
        std::cout << "PAIRING_CODE " << sixDigitCode << std::endl;
    }

    ///  @copydoc
    ///  dovahlink::application::PairingNotificationSink::NotifyPairingCodeIncorrect
    void NotifyPairingCodeIncorrect(std::string_view sixDigitCode) override {
        std::cout << "PAIRING_CODE_INCORRECT " << sixDigitCode << std::endl;
    }

    ///  @copydoc
    ///  dovahlink::application::PairingNotificationSink::NotifyPairingAttemptsExhausted
    void NotifyPairingAttemptsExhausted() override {
        std::cout << "PAIRING_ATTEMPTS_EXHAUSTED" << std::endl;
    }
};

///  Processes one lifecycle event through the tracker and applies its
///  resulting transition to the active play context, mirroring
///  game_state/commonlib_game_lifecycle_sink.cpp's per-signal handling without
///  any SKSE dependency.
///  @return The transition produced by this event.
dovahlink::application::GameLifecycleTracker::Transition ProcessLifecycleEvent(
    dovahlink::application::GameLifecycleTracker& tracker,
    dovahlink::application::ActivePlayContext& activePlayContext,
    dovahlink::application::LifecycleEvent event) {
    auto transition = tracker.HandleEvent(event);
    dovahlink::application::ApplyLifecycleTransition(activePlayContext,
                                                     transition);
    return transition;
}

///  Reads a positive harness-only token lifetime override from the environment.
std::optional<std::chrono::steady_clock::duration>
ReadTokenTtlOverride(const dovahlink::security::EnvironmentReader& env) {
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

///  Reads a harness-only forced play-context ID from the environment, so a
///  process-boundary test can force a coincidental ID collision across a
///  kill-and-relaunch cycle and prove bridgeInstanceId -- not playContextId
///  alone -- is what the wire comparison actually depends on. Absent in real
///  play; every minted ID is otherwise CSPRNG-backed
///  (security::GenerateOpaqueId).
std::optional<std::string>
ReadPlayContextIdOverride(const dovahlink::security::EnvironmentReader& env) {
    auto raw = env.Read(kPlayContextIdOverrideEnvVar);
    if (!raw.has_value() || raw->empty()) {
        return std::nullopt;
    }
    return raw;
}

///  Reads a harness-only loopback port override from the environment, so each
///  test run can bind its own OS-assigned port (a value of `"0"`) instead of
///  every invocation sharing the real, documented `kBridgePort` -- eliminating
///  the bind collisions concurrent harness-spawning test runs would otherwise
///  race on. Absent in real play; the real Skyrim plugin never reads this
///  variable and always binds `kBridgePort`.
std::optional<std::uint16_t>
ReadPortOverride(const dovahlink::security::EnvironmentReader& env) {
    auto raw = env.Read(kPortOverrideEnvVar);
    if (!raw.has_value() || raw->empty()) {
        return std::nullopt;
    }
    try {
        std::size_t consumed = 0;
        int parsed = std::stoi(*raw, &consumed);
        //  std::stoi silently ignores trailing non-numeric characters (e.g.
        //  "8080xyz" parses as 8080); requiring the whole string to be consumed
        //  rejects that instead of accepting it.
        if (consumed != raw->size() || parsed < 0 ||
            parsed > std::numeric_limits<std::uint16_t>::max()) {
            return std::nullopt;
        }
        return static_cast<std::uint16_t>(parsed);
    } catch (const std::exception&) {
        return std::nullopt;
    }
}

///  Reads a harness-only trust-store path override from the environment, so each
///  test run can isolate its trust store instead of every invocation sharing the
///  real per-user production file
///  (`security::ResolveDefaultTrustStorePath`) across runs.
std::optional<std::filesystem::path>
ReadTrustStorePathOverride(const dovahlink::security::EnvironmentReader& env) {
    auto raw = env.Read(kTrustStorePathOverrideEnvVar);
    if (!raw.has_value() || raw->empty()) {
        return std::nullopt;
    }
    return std::filesystem::path(*raw);
}

} //  namespace
///  Runs the standalone bridge integration harness.
int main() {
    dovahlink::security::WindowsEnvironmentReader environmentReader;
    auto tokenRead = dovahlink::security::ReadTokenFromEnvironment(
        environmentReader, kTokenEnvVar);
    if (tokenRead.outcome != dovahlink::security::TokenReadOutcome::kValid) {
        std::cerr << "DOVAHLINK_BRIDGE_TOKEN is not set to a valid 64-character "
                     "hex-encoded "
                     "256-bit token; the harness cannot authenticate a client "
                     "without it.\n";
        return 1;
    }

    boost::asio::io_context ioc;
    //  An override of "0" asks the OS for any free port instead of the fixed,
    //  documented kBridgePort -- harness-only, so concurrent test runs each get
    //  their own isolated bind instead of racing for the one real port. The real
    //  Skyrim plugin never sets this and always binds kBridgePort unconditionally.
    std::uint16_t requestedPortV4 =
        ReadPortOverride(environmentReader).value_or(kBridgePort);
    auto listenerV4Result = dovahlink::transport::LoopbackListener::Create(
        ioc, dovahlink::transport::LoopbackListener::IpVersion::kV4,
        requestedPortV4);
    if (!listenerV4Result.has_value()) {
        std::cerr << "Failed to bind the IPv4 loopback listener on port "
                  << requestedPortV4 << ".\n";
        return 1;
    }
    auto listenerV4 = std::move(*listenerV4Result);
    //  Resolves to the OS-assigned value when requestedPortV4 was 0, or otherwise
    //  echoes it back unchanged -- either way, the single source of truth for what
    //  port this process actually bound, reported to the caller below rather than
    //  assumed.
    std::uint16_t resolvedPort = listenerV4.LocalEndpoint().port();

    auto listenerV6Result = dovahlink::transport::LoopbackListener::Create(
        ioc, dovahlink::transport::LoopbackListener::IpVersion::kV6,
        resolvedPort);
    if (!listenerV6Result.has_value()) {
        std::cerr << "Failed to bind the IPv6 loopback listener on port "
                  << resolvedPort << ".\n";
        return 1;
    }
    auto listenerV6 = std::move(*listenerV6Result);

    dovahlink::transport::ConnectionSlot connectionSlot;
    auto tokenTtl =
        ReadTokenTtlOverride(environmentReader).value_or(std::chrono::minutes(5));
    dovahlink::security::TokenStore tokenStore(std::move(tokenRead.bytes),
                                               tokenTtl);
    dovahlink::security::FailedTokenThrottle tokenThrottle;

    auto trustStorePath = ReadTrustStorePathOverride(environmentReader);
    if (!trustStorePath.has_value()) {
        trustStorePath = dovahlink::security::ResolveDefaultTrustStorePath();
    }
    if (!trustStorePath.has_value()) {
        std::cerr
            << "Could not resolve a trust-store path (LOCALAPPDATA unset and no "
               "DOVAHLINK_HARNESS_TRUST_STORE_PATH_OVERRIDE given).\n";
        return 1;
    }
    dovahlink::security::WindowsTrustStorePersistence trustStorePersistence(
        *trustStorePath);
    auto trustStore =
        dovahlink::security::TrustStore::Load(trustStorePersistence);
    dovahlink::security::FailedTokenThrottle credentialThrottle;
    dovahlink::security::PairingSession pairingSession;
    StdoutPairingNotificationSink pairingNotificationSink;

    dovahlink::application::SessionManager sessionManager;
    auto playContextIdOverride = ReadPlayContextIdOverride(environmentReader);
    dovahlink::application::GameLifecycleTracker lifecycleTracker(
        [playContextIdOverride]() -> std::optional<std::string> {
            if (playContextIdOverride.has_value()) {
                return playContextIdOverride;
            }
            return dovahlink::security::GenerateOpaqueId();
        });
    dovahlink::application::ActivePlayContext activePlayContext;
    //  Routes "increase_level" captures below into whichever play context is
    //  active, the same seam dovahlink_bridge_plugin.cpp's real
    //  LevelIncreaseHandler uses; a capture with no active context is dropped,
    //  matching real play (main menu, before any load).
    dovahlink::application::ActivePlayContextLevelSink levelSink(
        activePlayContext);

    //  Skyrim-independent stand-in for the real plugin's bridgeInstanceId
    //  generation (dovahlink_bridge_plugin.cpp): a fresh identity per harness
    //  process launch, stamped onto every response envelope via
    //  BridgeWorkerPool below and printed after READY so a process-boundary
    //  test can observe it changing across a kill-and-relaunch cycle without
    //  a running Skyrim.
    auto bridgeInstanceId = dovahlink::security::GenerateOpaqueId();

    NoOpCallbackRegistry callbackRegistry;
    dovahlink::application::BridgeTransport bridgeTransport(listenerV4,
                                                            listenerV6);
    dovahlink::application::BridgeWorkerPool bridgeWorkerPool(
        listenerV4, listenerV6, connectionSlot, tokenStore, tokenThrottle,
        trustStore, credentialThrottle, sessionManager, activePlayContext,
        pairingSession, pairingNotificationSink, bridgeInstanceId,
        kBridgeVersion);
    dovahlink::application::Coordinator coordinator(
        callbackRegistry, bridgeWorkerPool, bridgeTransport);

    coordinator.Start();
    std::cout << "READY" << std::endl;
    std::cout << "BRIDGE_INSTANCE " << bridgeInstanceId.value_or("(unavailable)")
              << std::endl;
    std::cout << "PORT " << resolvedPort << std::endl;

    std::int64_t level = 5;

    std::string line;
    while (std::getline(std::cin, line)) {
        if (line == "increase_level") {
            ++level;
            levelSink.OnLevelCaptured(level);
            std::cout << "LEVEL " << level << std::endl;
        } else if (line == "new_game") {
            auto transition = ProcessLifecycleEvent(
                lifecycleTracker, activePlayContext,
                dovahlink::application::LifecycleEvent::kNewGame);
            std::cout << "PLAY_CONTEXT "
                      << transition.newPlayContextId.value_or("(none)") << std::endl;
        } else if (line == "load_game") {
            //  Two raw signals in sequence, matching a real SKSE load: the
            //  save is selected (kPreLoadGame, invalidating any current
            //  context) before it finishes loading (kPostLoadGameSuccess,
            //  minting the new one). Only the final transition's ID is
            //  reported; both are applied to activePlayContext.
            ProcessLifecycleEvent(
                lifecycleTracker, activePlayContext,
                dovahlink::application::LifecycleEvent::kPreLoadGame);
            auto transition = ProcessLifecycleEvent(
                lifecycleTracker, activePlayContext,
                dovahlink::application::LifecycleEvent::kPostLoadGameSuccess);
            std::cout << "PLAY_CONTEXT "
                      << transition.newPlayContextId.value_or("(none)") << std::endl;
        } else if (line == "load_game_fail") {
            //  Same two-signal shape as load_game, but the load itself fails:
            //  kPreLoadGame still invalidates any current context, and
            //  kPostLoadGameFailure settles into kNoContext without minting a
            //  replacement.
            ProcessLifecycleEvent(
                lifecycleTracker, activePlayContext,
                dovahlink::application::LifecycleEvent::kPreLoadGame);
            auto transition = ProcessLifecycleEvent(
                lifecycleTracker, activePlayContext,
                dovahlink::application::LifecycleEvent::kPostLoadGameFailure);
            std::cout << "PLAY_CONTEXT "
                      << transition.newPlayContextId.value_or("(none)") << std::endl;
        } else if (line == "revert") {
            auto transition = ProcessLifecycleEvent(
                lifecycleTracker, activePlayContext,
                dovahlink::application::LifecycleEvent::kRevert);
            std::cout << "PLAY_CONTEXT "
                      << transition.newPlayContextId.value_or("(none)") << std::endl;
        } else if (line.starts_with(kRevokeCommandPrefix)) {
            //  Test-only shortcut straight to TrustStore::Revoke: a real deployment
            //  only reaches revocation through TrustDeviceAdminService (security.md's "Trust
            //  administration surface"), but the harness already knows the clientId a
            //  scenario paired, so it skips the shortId lookup that surface exists
            //  for. Still exercises the same ActiveSessionDisconnector force-close
            //  primitive TrustDeviceAdminService::RevokeByShortId itself calls, so a
            //  scenario can prove revoke-while-connected disconnects the live session
            //  immediately, not just the next reconnect attempt.
            std::string revokedClientId = line.substr(kRevokeCommandPrefix.size());
            if (trustStore.Revoke(revokedClientId)) {
                bridgeWorkerPool.DisconnectIfClientActive(revokedClientId, "revoked");
                std::cout << "REVOKED " << revokedClientId << std::endl;
            } else {
                std::cout << "REVOKE_FAILED " << revokedClientId << std::endl;
            }
        } else if (line.starts_with(kBlockCommandPrefix)) {
            //  Test-only shortcut straight to TrustStore::Block, mirroring the "revoke
            //  " shortcut above: skips TrustDeviceAdminService's shortId lookup (the harness
            //  already knows the clientId a scenario paired) but still exercises the
            //  same ActiveSessionDisconnector/PairingSession::TryCancel primitives
            //  TrustDeviceAdminService::BlockByShortId itself calls, so a scenario can prove
            //  block-while-connected disconnects the live session and cancels any
            //  owned pairing challenge immediately, not just the next
            //  reconnect/pairing attempt.
            std::string blockedClientId = line.substr(kBlockCommandPrefix.size());
            if (trustStore.Block(blockedClientId) ==
                dovahlink::security::BlockOutcome::kBlocked) {
                (void)pairingSession.TryCancel(blockedClientId,
                                               std::chrono::steady_clock::now());
                bridgeWorkerPool.DisconnectIfClientActive(blockedClientId, "blocked");
                std::cout << "BLOCKED " << blockedClientId << std::endl;
            } else {
                std::cout << "BLOCK_FAILED " << blockedClientId << std::endl;
            }
        } else if (line.starts_with(kUnblockCommandPrefix)) {
            //  Test-only shortcut straight to TrustStore::Unblock, mirroring "block
            //  "/"revoke " above.
            std::string unblockedClientId = line.substr(kUnblockCommandPrefix.size());
            if (trustStore.Unblock(unblockedClientId) ==
                dovahlink::security::UnblockOutcome::kUnblocked) {
                std::cout << "UNBLOCKED " << unblockedClientId << std::endl;
            } else {
                std::cout << "UNBLOCK_FAILED " << unblockedClientId << std::endl;
            }
        } else if (line.starts_with(kTrustResetCommandPrefix)) {
            //  Test-only shortcut straight to TrustStore::ResetTrust, mirroring
            //  "revoke "/"block " above: skips TrustResetService's
            //  confirmation-challenge gate but still exercises the same
            //  ActiveSessionDisconnector::DisconnectIfClientActive primitive
            //  TrustResetService::ResetTrust itself calls for a formerly-trusted
            //  client's active session -- targeted by clientId (not DisconnectActive)
            //  because Reset Trust only revokes previously-trusted devices, so an
            //  active session that never held trust must not be disconnected by it.
            std::string trustResetClientId =
                line.substr(kTrustResetCommandPrefix.size());
            if (trustStore.ResetTrust()) {
                bridgeWorkerPool.DisconnectIfClientActive(trustResetClientId,
                                                          "trust_reset");
                std::cout << "TRUST_RESET " << trustResetClientId << std::endl;
            } else {
                std::cout << "TRUST_RESET_FAILED " << trustResetClientId << std::endl;
            }
        } else if (line == kFactoryResetCommand) {
            //  Test-only shortcut straight to TrustStore::Reset, bypassing
            //  TrustResetService's StartFactoryReset/ConfirmFactoryReset
            //  confirmation-challenge dance. Uses DisconnectActive rather than
            //  DisconnectIfClientActive, mirroring
            //  TrustResetService::ConfirmFactoryReset itself: Factory Reset wipes
            //  every known device record unconditionally, so any active session --
            //  trusted or not -- is disconnected, unlike trust_reset above.
            if (trustStore.Reset()) {
                bridgeWorkerPool.DisconnectActive("factory_reset");
                std::cout << "FACTORY_RESET" << std::endl;
            } else {
                std::cout << "FACTORY_RESET_FAILED" << std::endl;
            }
        } else if (line == "quit") {
            break;
        }
    }

    coordinator.Shutdown();
    return 0;
}
