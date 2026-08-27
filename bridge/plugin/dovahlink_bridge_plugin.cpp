//  SKSE plugin entry point and plugin-lifetime wiring for the bridge. This file
//  contains the runtime-specific composition layer; the underlying components
//  remain testable without a running Skyrim process.

#include "SKSE/SKSE.h"

#include "application/active_play_context_level_sink.hpp"
#include "application/active_play_context_reader.hpp"
#include "application/bridge_callback_registry.hpp"
#include "application/bridge_config.hpp"
#include "application/bridge_transport.hpp"
#include "application/bridge_worker_pool.hpp"
#include "application/character_state_store.hpp"
#include "application/connection_session.hpp"
#include "application/coordinator.hpp"
#include "application/game_behavior_config.hpp"
#include "application/handshake_handler.hpp"
#include "application/pairing_notification_sink.hpp"
#include "application/play_context_lifecycle.hpp"
#include "application/session.hpp"
#include "application/trust_device_admin_service.hpp"
#include "application/trust_reset_service.hpp"
#include "game_state/commonlib_game_behavior_compatibility.hpp"
#include "game_state/commonlib_game_lifecycle_sink.hpp"
#include "game_state/commonlib_level_increase_sink.hpp"
#include "game_state/commonlib_pairing_notification_sink.hpp"
#include "game_state/commonlib_trust_admin_papyrus_adapter.hpp"
#include "game_state/level_increase_handler.hpp"
#include "game_state/player_level_accessor.hpp"
#include "game_state/runtime_guard.hpp"
#include "security/csprng.hpp"
#include "security/factory_reset_challenge.hpp"
#include "security/failed_token_throttle.hpp"
#include "security/pairing_session.hpp"
#include "security/token_provider.hpp"
#include "security/token_store.hpp"
#include "security/trust_store.hpp"
#include "security/windows_trust_store_persistence.hpp"
#include "transport/connection_slot.hpp"
#include "transport/loopback_listener.hpp"

#include <spdlog/async.h>
#include <spdlog/sinks/basic_file_sink.h>

#include <boost/asio/io_context.hpp>

#include <memory>
#include <utility>

namespace {

using dovahlink::application::kBridgePort;
using dovahlink::application::kBridgeVersion;
using dovahlink::application::kGameBehaviorConfigPath;
using dovahlink::application::kTokenEnvVar;

//  Minimal file logging to the SKSE log directory.
///  Configures the default logger when the SKSE log directory is available.
///
///  Uses spdlog's non-blocking async factory rather than a synchronous
///  logger: every SKSE::log:: call in this plugin can run from inside a raw
///  SKSE callback (the messaging listener, the serialization revert
///  callback), and ai/context/skse/architecture.md forbids blocking
///  filesystem work there, independent of whether a specific call site has
///  been observed to crash. (A crash initially suspected to be caused by
///  synchronous logging inside the revert callback was later found to have
///  a different, unrelated cause -- an incorrect pointer dereference in
///  commonlib_game_lifecycle_sink.cpp's kPostLoadGame handling, fixed
///  separately. This change is kept for compliance with the documented
///  rule, not as a proven fix for that incident.) async_factory_nonblock
///  hands each message to a background thread and never blocks the caller;
///  under sustained pressure it drops the oldest queued line rather than
///  stalling (acceptable for diagnostic logging, unlike protocol or
///  transport data).
void SetupLogging() {
    auto path = SKSE::log::log_directory();
    if (!path.has_value()) {
        return;
    }
    *path /= "DovahLinkBridge.log";
    auto logger =
        spdlog::async_factory_nonblock::create<spdlog::sinks::basic_file_sink_mt>(
            "global", path->string(), /*truncate=*/true);
    logger->set_level(spdlog::level::info);
    logger->flush_on(spdlog::level::info);
    spdlog::set_default_logger(std::move(logger));
}

} //  namespace

//  Hand-written plugin metadata and address-library compatibility declaration.
using namespace std::literals;
SKSEPluginInfo(
        .Version = REL::Version{0, 3, 3, 0}, .Name = "DovahLink Bridge"sv,
        .Author = "Soneka96"sv, .SupportEmail = ""sv,
        .StructCompatibility = SKSE::StructCompatibility::Independent,
        .RuntimeCompatibility = SKSE::VersionIndependence::AddressLibrary,
        .MinimumSKSEVersion = REL::Version{2, 2, 6, 0})

    ///  Initializes the bridge plugin and schedules startup after game data
    ///  loads.
    SKSEPluginLoad(const SKSE::LoadInterface* skse) {
    SetupLogging();

    //  SKSE-QUIRK: see
    //  ai/context/skse/runtime-quirks.md#skseinit-must-run-before-any-interface-registration
    //  Records this plugin's identity (its SKSE-assigned handle) with the
    //  CommonLibSSE-NG API layer. Every call that needs a plugin handle --
    //  MessagingInterface::RegisterListener, SerializationInterface's
    //  callbacks, and any future use of SKSE::Get*Interface() -- silently
    //  fails without this: confirmed empirically (SKSE logged "Failed to
    //  register messaging listener" and the plugin was disabled) before this
    //  call existed. Must run before any such call below.
    SKSE::Init(skse);

    if (!dovahlink::game_state::IsCurrentWindowsVersionSupported()) {
        SKSE::log::error("Unsupported Windows runtime. DovahLink Bridge requires "
                         "Windows 10 or later.");
        return false;
    }

    //  Reject unsupported runtime combinations before constructing bridge state.
    REL::Version skyrimVersionRel = skse->RuntimeVersion();
    REL::Version skseVersionRel = REL::Version::unpack(skse->SKSEVersion());
    dovahlink::game_state::RuntimeVersion skyrimVersion{
        skyrimVersionRel[0], skyrimVersionRel[1], skyrimVersionRel[2],
        skyrimVersionRel[3]};
    dovahlink::game_state::RuntimeVersion skseVersion{
        skseVersionRel[0], skseVersionRel[1], skseVersionRel[2],
        skseVersionRel[3]};
    if (!dovahlink::game_state::IsSupportedSkyrimVersion(skyrimVersion) ||
        !dovahlink::game_state::IsSupportedSkseVersion(skseVersion)) {
        SKSE::log::error("Unsupported runtime: Skyrim {}.{}.{}.{}, SKSE "
                         "{}.{}.{}.{}. DovahLink Bridge requires "
                         "exactly Skyrim {}.{}.{}, SKSE {}.{}.{}.",
                         skyrimVersion.major, skyrimVersion.minor,
                         skyrimVersion.build, skyrimVersion.revision,
                         skseVersion.major, skseVersion.minor, skseVersion.build,
                         skseVersion.revision,
                         dovahlink::game_state::kSupportedSkyrimVersion.major,
                         dovahlink::game_state::kSupportedSkyrimVersion.minor,
                         dovahlink::game_state::kSupportedSkyrimVersion.build,
                         dovahlink::game_state::kSupportedSkseVersion.major,
                         dovahlink::game_state::kSupportedSkseVersion.minor,
                         dovahlink::game_state::kSupportedSkseVersion.build);
        return false;
    }

    //  Runtime compatibility toggles (both default enabled): keeping Skyrim
    //  active while unfocused so the pairing code stays visible while the
    //  player is in the DovahLink app, and patching achievement eligibility
    //  back on for a modded load order. Read once, early, so both outcomes
    //  are logged before any other setup and can be disabled independently
    //  through Data/SKSE/Plugins/DovahLinkBridge.ini.
    static dovahlink::application::FilesystemGameBehaviorConfigFileReader
        gameBehaviorConfigReader;
    dovahlink::application::GameBehaviorConfig behaviorConfig =
        dovahlink::application::ReadGameBehaviorConfig(gameBehaviorConfigReader,
                                                       kGameBehaviorConfigPath);
    SKSE::log::info("Always-active mode: {}",
                    behaviorConfig.alwaysActive ? "enabled" : "disabled");
    SKSE::log::info("Achievement compatibility: {}",
                    behaviorConfig.achievementCompat ? "enabled" : "disabled");
    if (behaviorConfig.alwaysActive) {
        dovahlink::game_state::ApplyAlwaysActiveSetting();
    }
    if (behaviorConfig.achievementCompat) {
        dovahlink::game_state::InstallAchievementCompatibilityPatch();
    }

    //  The developer token is optional, per ai/context/protocol/security.md's
    //  "Developer authentication": normal users never set it and authenticate
    //  through pairing instead, so a missing or malformed value must never
    //  fail plugin load (that would surface to the user only as SKSE's
    //  generic "incompatible plugin" failure, with no indication of the real
    //  cause). A missing or malformed value disables only the separate
    //  one_time_local_token auth path; TokenStore below already treats an
    //  empty token as permanently unavailable, so passing it an empty vector
    //  is sufficient -- no other component needs to know why it is empty.
    //  The token's own value is never logged in any branch.
    static dovahlink::security::WindowsEnvironmentReader environmentReader;
    auto tokenRead = dovahlink::security::ReadTokenFromEnvironment(
        environmentReader, kTokenEnvVar);
    switch (tokenRead.outcome) {
    case dovahlink::security::TokenReadOutcome::kMissing:
        SKSE::log::info("{} is not set; developer-token authentication is "
                        "disabled. Normal users do not need "
                        "it -- pair through the in-game pairing flow instead.",
                        kTokenEnvVar);
        break;
    case dovahlink::security::TokenReadOutcome::kMalformed:
        SKSE::log::warn("{} is set but is not a valid token; developer-token "
                        "authentication is disabled. "
                        "Expected exactly 64 hexadecimal characters, with no "
                        "\"0x\" prefix and no separators. "
                        "Pairing is unaffected.",
                        kTokenEnvVar);
        break;
    case dovahlink::security::TokenReadOutcome::kValid:
        SKSE::log::info("{} is set; developer-token authentication is enabled.",
                        kTokenEnvVar);
        break;
    }

    //  Both IPv4 and IPv6 loopback listeners are required; only the port is
    //  configurable.
    static boost::asio::io_context ioc;
    auto listenerV4Result = dovahlink::transport::LoopbackListener::Create(
        ioc, dovahlink::transport::LoopbackListener::IpVersion::kV4, kBridgePort);
    if (!listenerV4Result.has_value()) {
        SKSE::log::error("Failed to bind the IPv4 loopback listener on port {}.",
                         kBridgePort);
        return false;
    }
    auto listenerV6Result = dovahlink::transport::LoopbackListener::Create(
        ioc, dovahlink::transport::LoopbackListener::IpVersion::kV6, kBridgePort);
    if (!listenerV6Result.has_value()) {
        SKSE::log::error("Failed to bind the IPv6 loopback listener on port {}.",
                         kBridgePort);
        return false;
    }
    static dovahlink::transport::LoopbackListener listenerV4 =
        std::move(*listenerV4Result);
    static dovahlink::transport::LoopbackListener listenerV6 =
        std::move(*listenerV6Result);

    //  This bridge instance's identity, per ARCHITECTURE.md's runtime/identity
    //  model: one value for this plugin's whole running lifetime, independent
    //  of any play context. Stamped onto every response envelope via
    //  BridgeWorkerPool below. A generation failure is not fatal -- it is
    //  logged, and every response simply carries no bridgeInstanceId value,
    //  the same "unavailable" shape a `null` would already encode.
    static std::optional<std::string> bridgeInstanceId =
        dovahlink::security::GenerateOpaqueId();
    SKSE::log::info("Bridge instance ID: {}",
                    bridgeInstanceId.value_or("(unavailable)"));

    //  Plugin-lifetime application and security state. Declared as function-
    //  local statics, in the exact order they depend on each other: a
    //  function-local static is guaranteed initialized the first (and only,
    //  for SKSEPluginLoad) time control reaches its declaration, so this
    //  ordering is exactly the construction order, without needing a
    //  hand-rolled aggregate to express the same dependency graph.
    static dovahlink::transport::ConnectionSlot connectionSlot;
    static dovahlink::security::TokenStore tokenStore(std::move(tokenRead.bytes));
    static dovahlink::security::FailedTokenThrottle tokenThrottle;

    //  The persistent per-user trust store's backing file cannot be resolved
    //  without LOCALAPPDATA, which every interactive Windows user session sets.
    //  Unlike the optional developer token above, this is fatal: the only
    //  alternative is an in-memory-only store that would silently forget every
    //  paired client on the next restart, which is worse than refusing to load.
    auto trustStorePath = dovahlink::security::ResolveDefaultTrustStorePath();
    if (!trustStorePath.has_value()) {
        SKSE::log::error("Could not resolve the per-user trust-store path "
                         "(LOCALAPPDATA unset).");
        return false;
    }
    static dovahlink::security::WindowsTrustStorePersistence
        trustStorePersistence(*trustStorePath);
    static dovahlink::security::TrustStore trustStore =
        dovahlink::security::TrustStore::Load(trustStorePersistence);
    static dovahlink::security::TrustDeviceStore deviceStore(trustStore);
    static dovahlink::security::TrustResetStore resetStore(trustStore);

    static dovahlink::security::FailedTokenThrottle credentialThrottle;
    static dovahlink::security::PairingSession pairingSession;
    static dovahlink::game_state::CommonLibPairingNotificationSink
        pairingNotificationSink;

    static dovahlink::application::SessionManager sessionManager;

    //  The authoritative, play-context-owned identity and state per
    //  ARCHITECTURE.md's runtime/identity model. `playContextLifecycle` owns
    //  lifecycle state and context publication atomically, while
    //  `activePlayContextReader` is the read-only capability passed to
    //  connection-facing consumers. Declared
    //  before `levelIncreaseHandler` below, which routes its captures into
    //  whichever context is active through `ActivePlayContextLevelSink`.
    static dovahlink::application::PlayContextLifecycle playContextLifecycle;
    static dovahlink::game_state::CommonLibGameLifecycleSink lifecycleSink(
        playContextLifecycle);
    static dovahlink::game_state::ICommonLibGameLifecycleSink&
        lifecycleSinkContract = lifecycleSink;
    static dovahlink::application::ActivePlayContextReader
        activePlayContextReader(playContextLifecycle);
    static dovahlink::application::ActiveSessionSocket activeSessionSocket;
    static dovahlink::application::ActiveSessionController
        activeSessionController(sessionManager, activeSessionSocket,
                                activePlayContextReader, bridgeInstanceId);

    static dovahlink::application::ActivePlayContextLevelSink levelSink(
        playContextLifecycle);
    static dovahlink::game_state::PlayerLevelAccessor levelAccessor;
    static dovahlink::game_state::LevelIncreaseHandler levelIncreaseHandler(
        levelAccessor, levelSink);
    static dovahlink::game_state::CommonLibLevelIncreaseSink levelIncreaseSink(
        levelIncreaseHandler);
    static dovahlink::application::BridgeCallbackRegistry callbackRegistry(
        levelIncreaseSink);

    static dovahlink::application::BridgeTransport bridgeTransport(listenerV4,
                                                                   listenerV6);
    static dovahlink::application::TrustMutationCoordinator
        trustMutationCoordinator(trustStore, pairingSession, sessionManager);
    static dovahlink::application::HandshakeHandler handshakeHandler(
        tokenStore, tokenThrottle, trustStore, credentialThrottle,
        sessionManager, activePlayContextReader, bridgeInstanceId,
        kBridgeVersion);
    static dovahlink::application::PairingHandler pairingHandler(
        pairingSession, trustMutationCoordinator, pairingNotificationSink);
    static dovahlink::application::MessageDispatcher messageDispatcher(
        sessionManager, trustStore, trustMutationCoordinator, pairingHandler,
        activePlayContextReader, bridgeInstanceId);
    static dovahlink::application::ConnectionSession connectionSession(
        handshakeHandler, messageDispatcher, activePlayContextReader,
        pairingSession, bridgeInstanceId);
    static dovahlink::application::BridgeWorkerPool bridgeWorkerPool(
        listenerV4, listenerV6, connectionSlot, activeSessionSocket,
        connectionSession);

    //  Registers the optional trust-administration console adapter
    //  (ai/context/protocol/security.md's "Trust administration surface").
    //  Registration is attempted unconditionally; a failure is logged and remains
    //  isolated to this optional adapter, while the native functions simply go
    //  unused if ConsoleUtil Extended and its Papyrus glue script are not
    //  installed. Constructed after bridgeWorkerPool, which is also wired to
    //  the same active-session controller -- the capability that enforces
    //  "Revocation is immediate" (security.md's "Persistent local trust") against
    //  an already-connected session, not just the persisted trust record.
    static dovahlink::security::FactoryResetChallenge factoryResetChallenge;
    static dovahlink::application::ActiveSessionDisconnector
        trustSessionDisconnector(activeSessionController);
    static dovahlink::application::TrustDeviceAdminService
        trustDeviceAdminService(deviceStore, trustSessionDisconnector,
                                trustMutationCoordinator);
    static dovahlink::application::ITrustDeviceAdminService&
        trustDeviceAdminServiceContract = trustDeviceAdminService;
    static dovahlink::application::TrustResetService trustResetService(
        resetStore, trustSessionDisconnector, trustMutationCoordinator,
        factoryResetChallenge);
    static dovahlink::application::ITrustResetService& trustResetServiceContract =
        trustResetService;
    dovahlink::game_state::InstallTrustAdminPapyrusAdapter(
        trustDeviceAdminServiceContract, trustResetServiceContract);

    static dovahlink::application::Coordinator coordinator(
        callbackRegistry, bridgeWorkerPool, bridgeTransport);

    //  RE::PlayerCharacter is not valid before data loads, so the initial
    //  capture, event registration, worker pool, and transport startup are
    //  deferred to kDataLoaded.
    auto* messaging = static_cast<SKSE::MessagingInterface*>(
        skse->QueryInterface(SKSE::LoadInterface::kMessaging));
    if (!messaging) {
        SKSE::log::error("Unable to obtain SKSE's messaging interface; cannot "
                         "defer startup to kDataLoaded.");
        return false;
    }
    //  SKSE-QUIRK: see
    //  ai/context/skse/runtime-quirks.md#one-messaginginterfaceregisterlistener-call-per-plugin
    //  SKSE allows exactly one MessagingInterface::RegisterListener call per
    //  plugin; a second, separate call fails both registrations (confirmed
    //  empirically: SKSE logs "Failed to register messaging listener for
    //  SKSE" for each). kDataLoaded-gated startup and lifecycle-event
    //  logging must therefore share this single listener rather than each
    //  registering their own. dovahlink_bridge_plugin_registration_test.cpp
    //  enforces this structurally (see ai/context/skse/testing.md); it fails
    //  if a second RegisterListener call is ever added to this file.
    lifecycleSinkContract.Register(
        [](dovahlink::application::ContainedWork work) {
            return coordinator.RunCallbackContained(std::move(work));
        });
    messaging->RegisterListener([](SKSE::MessagingInterface::Message* message) {
        if (message->type == SKSE::MessagingInterface::kDataLoaded) {
            (void)coordinator.RunCallbackContained([] {
                levelIncreaseHandler.HandleLevelIncrease();
                coordinator.Start();
                SKSE::log::info(
                    "DovahLink Bridge listening on loopback port {} (IPv4 and IPv6).",
                    kBridgePort);
            });
            return;
        }
        lifecycleSinkContract.OnMessage(*message);
    });

    auto* serialization = static_cast<SKSE::SerializationInterface*>(
        skse->QueryInterface(SKSE::LoadInterface::kSerialization));
    if (!serialization) {
        SKSE::log::error("Unable to obtain SKSE's serialization interface; "
                         "play-context revert events will not be logged.");
    } else {
        serialization->SetRevertCallback(
            [](SKSE::SerializationInterface*) {
                lifecycleSinkContract.OnRevert();
            });
    }

    return true;
}
