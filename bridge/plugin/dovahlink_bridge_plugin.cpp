// SKSE plugin entry point and plugin-lifetime wiring for the bridge. This file
// contains the runtime-specific composition layer; the underlying components
// remain testable without a running Skyrim process.

#include "SKSE/SKSE.h"

#include "application/bridge_config.hpp"
#include "application/bridge_transport.hpp"
#include "application/bridge_worker_pool.hpp"
#include "application/character_state_store.hpp"
#include "application/coordinator.hpp"
#include "application/game_lifecycle_tracker.hpp"
#include "application/pairing_notification_sink.hpp"
#include "application/play_context.hpp"
#include "application/session.hpp"
#include "game_state/commonlib_game_lifecycle_sink.hpp"
#include "game_state/commonlib_level_accessor.hpp"
#include "game_state/commonlib_level_increase_sink.hpp"
#include "game_state/level_increase_handler.hpp"
#include "game_state/runtime_guard.hpp"
#include "security/csprng.hpp"
#include "security/pairing_session.hpp"
#include "security/throttle.hpp"
#include "security/token_provider.hpp"
#include "security/token_store.hpp"
#include "security/trust_store.hpp"
#include "security/windows_trust_store_persistence.hpp"
#include "transport/connection_slot.hpp"
#include "transport/listener.hpp"

#include <spdlog/async.h>
#include <spdlog/sinks/basic_file_sink.h>

#include <boost/asio/io_context.hpp>

#include <memory>
#include <utility>

namespace {

using dovahlink::application::kBridgePort;
using dovahlink::application::kTokenEnvVar;

// Minimal file logging to the SKSE log directory.
/// Configures the default logger when the SKSE log directory is available.
///
/// Uses spdlog's non-blocking async factory rather than a synchronous
/// logger: every SKSE::log:: call in this plugin can run from inside a raw
/// SKSE callback (the messaging listener, the serialization revert
/// callback), and ai/context/skse/architecture.md forbids blocking
/// filesystem work there, independent of whether a specific call site has
/// been observed to crash. (A crash initially suspected to be caused by
/// synchronous logging inside the revert callback was later found to have
/// a different, unrelated cause -- an incorrect pointer dereference in
/// commonlib_game_lifecycle_sink.cpp's kPostLoadGame handling, fixed
/// separately. This change is kept for compliance with the documented
/// rule, not as a proven fix for that incident.) async_factory_nonblock
/// hands each message to a background thread and never blocks the caller;
/// under sustained pressure it drops the oldest queued line rather than
/// stalling (acceptable for diagnostic logging, unlike protocol or
/// transport data).
void SetupLogging() {
    auto path = SKSE::log::log_directory();
    if (!path.has_value()) {
        return;
    }
    *path /= "DovahLinkBridge.log";
    auto logger = spdlog::async_factory_nonblock::create<spdlog::sinks::basic_file_sink_mt>(
        "global", path->string(), /*truncate=*/true);
    logger->set_level(spdlog::level::info);
    logger->flush_on(spdlog::level::info);
    spdlog::set_default_logger(std::move(logger));
}

/// Connects coordinator callback lifecycle calls to the runtime event sink.
class BridgeCallbackRegistry : public dovahlink::application::CallbackRegistry {
public:
    /// Binds the registry to the runtime level-increase sink.
    explicit BridgeCallbackRegistry(dovahlink::game_state::CommonLibLevelIncreaseSink& sink) : sink_(sink) {}

    /// @copydoc dovahlink::application::CallbackRegistry::RegisterAll
    void RegisterAll(dovahlink::application::ContainedWorkRunner callbackRunner) override {
        sink_.Register(std::move(callbackRunner));
    }
    /// @copydoc dovahlink::application::CallbackRegistry::UnregisterAll
    void UnregisterAll() override { sink_.Unregister(); }

private:
    /// Runtime event sink controlled by the coordinator lifecycle.
    dovahlink::game_state::CommonLibLevelIncreaseSink& sink_;
};

/// Displays a freshly generated pairing code via the SKSE log. A temporary stand-in for the real
/// native Skyrim notification, which is stage G's own dedicated adapter (`bridge/plugin`,
/// ai/context/protocol/security.md's "Persistent local trust") -- this class carries no pairing
/// state logic, only where the code goes, matching PairingNotificationSink's own seam contract.
class SkseLogPairingNotificationSink : public dovahlink::application::PairingNotificationSink {
public:
    /// @copydoc dovahlink::application::PairingNotificationSink::NotifyPairingCodeAvailable
    void NotifyPairingCodeAvailable(std::string_view sixDigitCode) override {
        SKSE::log::info("Pairing code: {}", sixDigitCode);
    }
};

// The DovahLink Bridge/mod release version exposed to clients in
// hello_ack.bridgeVersion (ai/context/protocol/compatibility.md), matching
// bridge/vcpkg.json's version-string. Kept in sync with the plugin metadata
// version below by convention; both describe the same release.
constexpr const char* kBridgeVersion = "0.2.0";

}  // namespace

// Hand-written plugin metadata and address-library compatibility declaration.
using namespace std::literals;
SKSEPluginInfo(
    .Version = REL::Version{0, 2, 0, 0},
    .Name = "DovahLink Bridge"sv,
    .Author = "Goncalo"sv,
    .SupportEmail = ""sv,
    .StructCompatibility = SKSE::StructCompatibility::Independent,
    .RuntimeCompatibility = SKSE::VersionIndependence::AddressLibrary,
    .MinimumSKSEVersion = REL::Version{2, 2, 6, 0})

/// Initializes the bridge plugin and schedules startup after game data loads.
SKSEPluginLoad(const SKSE::LoadInterface* skse) {
    SetupLogging();

    // SKSE-QUIRK: see ai/context/skse/runtime-quirks.md#skseinit-must-run-before-any-interface-registration
    // Records this plugin's identity (its SKSE-assigned handle) with the
    // CommonLibSSE-NG API layer. Every call that needs a plugin handle --
    // MessagingInterface::RegisterListener, SerializationInterface's
    // callbacks, and any future use of SKSE::Get*Interface() -- silently
    // fails without this: confirmed empirically (SKSE logged "Failed to
    // register messaging listener" and the plugin was disabled) before this
    // call existed. Must run before any such call below.
    SKSE::Init(skse);

    // Reject unsupported runtime combinations before constructing bridge state.
    REL::Version skyrimVersionRel = skse->RuntimeVersion();
    REL::Version skseVersionRel = REL::Version::unpack(skse->SKSEVersion());
    dovahlink::game_state::RuntimeVersion skyrimVersion{skyrimVersionRel[0], skyrimVersionRel[1],
                                                          skyrimVersionRel[2], skyrimVersionRel[3]};
    dovahlink::game_state::RuntimeVersion skseVersion{skseVersionRel[0], skseVersionRel[1], skseVersionRel[2],
                                                        skseVersionRel[3]};
    if (!dovahlink::game_state::IsSupportedSkyrimVersion(skyrimVersion) ||
        !dovahlink::game_state::IsSupportedSkseVersion(skseVersion)) {
        SKSE::log::error("Unsupported runtime: Skyrim {}.{}.{}.{}, SKSE {}.{}.{}.{}. DovahLink Bridge requires "
                          "exactly Skyrim {}.{}.{}, SKSE {}.{}.{}.",
                          skyrimVersion.major, skyrimVersion.minor, skyrimVersion.build, skyrimVersion.revision,
                          skseVersion.major, skseVersion.minor, skseVersion.build, skseVersion.revision,
                          dovahlink::game_state::kSupportedSkyrimVersion.major,
                          dovahlink::game_state::kSupportedSkyrimVersion.minor,
                          dovahlink::game_state::kSupportedSkyrimVersion.build,
                          dovahlink::game_state::kSupportedSkseVersion.major,
                          dovahlink::game_state::kSupportedSkseVersion.minor,
                          dovahlink::game_state::kSupportedSkseVersion.build);
        return false;
    }

    // A missing or malformed launch token is fatal because no client could
    // authenticate without it.
    static dovahlink::security::WindowsEnvironmentReader environmentReader;
    auto tokenBytes = dovahlink::security::ReadTokenFromEnvironment(environmentReader, kTokenEnvVar);
    if (!tokenBytes.has_value()) {
        SKSE::log::error("DOVAHLINK_BRIDGE_TOKEN is not set to a valid 64-character hex-encoded 256-bit token; "
                          "the bridge cannot authenticate a client without it.");
        return false;
    }

    // Both IPv4 and IPv6 loopback listeners are required; only the port is
    // configurable.
    static boost::asio::io_context ioc;
    auto listenerV4Result =
        dovahlink::transport::LoopbackListener::Create(ioc, dovahlink::transport::LoopbackListener::IpVersion::kV4,
                                                         kBridgePort);
    if (!listenerV4Result.has_value()) {
        SKSE::log::error("Failed to bind the IPv4 loopback listener on port {}.", kBridgePort);
        return false;
    }
    auto listenerV6Result =
        dovahlink::transport::LoopbackListener::Create(ioc, dovahlink::transport::LoopbackListener::IpVersion::kV6,
                                                         kBridgePort);
    if (!listenerV6Result.has_value()) {
        SKSE::log::error("Failed to bind the IPv6 loopback listener on port {}.", kBridgePort);
        return false;
    }
    static dovahlink::transport::LoopbackListener listenerV4 = std::move(*listenerV4Result);
    static dovahlink::transport::LoopbackListener listenerV6 = std::move(*listenerV6Result);

    // This bridge instance's identity, per ARCHITECTURE.md's runtime/identity
    // model: one value for this plugin's whole running lifetime, independent
    // of any play context. Stamped onto every response envelope via
    // BridgeWorkerPool below. A generation failure is not fatal -- it is
    // logged, and every response simply carries no bridgeInstanceId value,
    // the same "unavailable" shape a `null` would already encode.
    static std::optional<std::string> bridgeInstanceId = dovahlink::security::GenerateOpaqueId();
    SKSE::log::info("Bridge instance ID: {}", bridgeInstanceId.value_or("(unavailable)"));

    // Plugin-lifetime application and security state. Declared as function-
    // local statics, in the exact order they depend on each other: a
    // function-local static is guaranteed initialized the first (and only,
    // for SKSEPluginLoad) time control reaches its declaration, so this
    // ordering is exactly the construction order, without needing a
    // hand-rolled aggregate to express the same dependency graph.
    static dovahlink::transport::ConnectionSlot connectionSlot;
    static dovahlink::security::TokenStore tokenStore(std::move(*tokenBytes));
    static dovahlink::security::FailedTokenThrottle tokenThrottle;

    // The persistent per-user trust store's backing file cannot be resolved
    // without LOCALAPPDATA, which every interactive Windows user session sets;
    // treated as fatal the same way a missing launch token is, rather than
    // silently falling back to an in-memory-only store that would forget
    // every paired client on the next restart.
    auto trustStorePath = dovahlink::security::ResolveDefaultTrustStorePath();
    if (!trustStorePath.has_value()) {
        SKSE::log::error("Could not resolve the per-user trust-store path (LOCALAPPDATA unset).");
        return false;
    }
    static dovahlink::security::WindowsTrustStorePersistence trustStorePersistence(*trustStorePath);
    static dovahlink::security::TrustStore trustStore = dovahlink::security::TrustStore::Load(trustStorePersistence);
    static dovahlink::security::FailedTokenThrottle credentialThrottle;
    static dovahlink::security::PairingSession pairingSession;
    static SkseLogPairingNotificationSink pairingNotificationSink;

    static dovahlink::application::SessionManager sessionManager;

    // The authoritative, play-context-owned identity and state per
    // ARCHITECTURE.md's runtime/identity model. `activePlayContext` is
    // lifecycle-driven -- GameLifecycleTracker transitions create and
    // invalidate its play contexts -- and is the sole state message_dispatcher
    // and subscription_handler read from. Declared before
    // `levelIncreaseHandler` below, which routes its captures into whichever
    // context is active through `ActivePlayContextLevelSink`.
    static dovahlink::application::GameLifecycleTracker lifecycleTracker;
    static dovahlink::application::ActivePlayContext activePlayContext;
    static dovahlink::game_state::CommonLibGameLifecycleSink lifecycleSink(lifecycleTracker, activePlayContext);

    static dovahlink::application::ActivePlayContextLevelSink levelSink(activePlayContext);
    static dovahlink::game_state::CommonLibLevelAccessor levelAccessor;
    static dovahlink::game_state::LevelIncreaseHandler levelIncreaseHandler(levelAccessor, levelSink);
    static dovahlink::game_state::CommonLibLevelIncreaseSink levelIncreaseSink(levelIncreaseHandler);
    static BridgeCallbackRegistry callbackRegistry(levelIncreaseSink);

    static dovahlink::application::BridgeTransport bridgeTransport(listenerV4, listenerV6);
    static dovahlink::application::BridgeWorkerPool bridgeWorkerPool(
        listenerV4, listenerV6, connectionSlot, tokenStore, tokenThrottle, trustStore, credentialThrottle,
        sessionManager, activePlayContext, pairingSession, pairingNotificationSink, bridgeInstanceId,
        kBridgeVersion);

    static dovahlink::application::Coordinator coordinator(callbackRegistry, bridgeWorkerPool, bridgeTransport);

    // RE::PlayerCharacter is not valid before data loads, so the initial
    // capture, event registration, worker pool, and transport startup are
    // deferred to kDataLoaded.
    auto* messaging =
        static_cast<SKSE::MessagingInterface*>(skse->QueryInterface(SKSE::LoadInterface::kMessaging));
    if (!messaging) {
        SKSE::log::error("Unable to obtain SKSE's messaging interface; cannot defer startup to kDataLoaded.");
        return false;
    }
    // SKSE-QUIRK: see ai/context/skse/runtime-quirks.md#one-messaginginterfaceregisterlistener-call-per-plugin
    // SKSE allows exactly one MessagingInterface::RegisterListener call per
    // plugin; a second, separate call fails both registrations (confirmed
    // empirically: SKSE logs "Failed to register messaging listener for
    // SKSE" for each). kDataLoaded-gated startup and lifecycle-event
    // logging must therefore share this single listener rather than each
    // registering their own. dovahlink_bridge_plugin_registration_test.cpp
    // enforces this structurally (see ai/context/skse/testing.md); it fails
    // if a second RegisterListener call is ever added to this file.
    lifecycleSink.Register(
        [](dovahlink::application::ContainedWork work) { return coordinator.RunCallbackContained(std::move(work)); });
    messaging->RegisterListener([](SKSE::MessagingInterface::Message* message) {
        if (message->type == SKSE::MessagingInterface::kDataLoaded) {
            (void)coordinator.RunCallbackContained([] {
                levelIncreaseHandler.HandleLevelIncrease();
                coordinator.Start();
                SKSE::log::info("DovahLink Bridge listening on loopback port {} (IPv4 and IPv6).", kBridgePort);
            });
            return;
        }
        lifecycleSink.OnMessage(*message);
    });

    auto* serialization =
        static_cast<SKSE::SerializationInterface*>(skse->QueryInterface(SKSE::LoadInterface::kSerialization));
    if (!serialization) {
        SKSE::log::error(
            "Unable to obtain SKSE's serialization interface; play-context revert events will not be logged.");
    } else {
        serialization->SetRevertCallback([](SKSE::SerializationInterface*) { lifecycleSink.OnRevert(); });
    }

    return true;
}
