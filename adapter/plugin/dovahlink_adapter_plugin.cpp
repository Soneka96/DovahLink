//  SKSE plugin entry point and plugin-lifetime wiring for the adapter. This
//  file contains the runtime-specific composition layer; the underlying
//  components remain testable without a running Skyrim process.

//  MUST precede every include below: RE/Skyrim.h now transitively pulls in
//  the real <Windows.h> (via CommonLibSSE-NG's DirectXTK-backed rendering
//  headers), which without WIN32_LEAN_AND_MEAN auto-includes the legacy
//  <winsock.h>. That collides with plugin/adapter_runtime.hpp's later,
//  transitive <winsock2.h> (through ipc/winsock_adapter_ipc_socket.hpp),
//  since the two are mutually exclusive in one translation unit.
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif

#include "SKSE/SKSE.h"

#include "RE/Skyrim.h"

#include "capture/adapter_capture_work_item.hpp"
#include "constants.hpp"
#include "identity/adapter_instance_id_generator.hpp"
#include "identity/adapter_play_context_generator.hpp"
#include "ipc/commonlib_adapter_pairing_notification_sink.hpp"
#include "papyrus/commonlib_adapter_status_papyrus_adapter.hpp"
#include "papyrus/commonlib_adapter_trust_admin_papyrus_adapter.hpp"
#include "plugin/adapter_runtime.hpp"
#include "plugin/adapter_startup_context.hpp"
#include "process/adapter_host_rendezvous_reader.hpp"
#include "process/adapter_host_shutdown_requester.hpp"
#include "process/adapter_owner_lifetime_id.hpp"
#include "runtime/adapter_game_behavior_config.hpp"
#include "runtime/adapter_game_behavior_config_file_reader.hpp"
#include "runtime/adapter_runtime_guard.hpp"
#include "runtime/commonlib_adapter_game_behavior_compatibility.hpp"
#include "runtime/commonlib_adapter_native_capture_router.hpp"
#include "runtime/commonlib_adapter_task_marshaller.hpp"

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>

#include <spdlog/async.h>
#include <spdlog/sinks/basic_file_sink.h>

#include <array>
#include <cstddef>
#include <filesystem>
#include <optional>
#include <string>
#include <utility>

namespace {

///  Configures asynchronous file logging to the SKSE log directory so no
///  `SKSE::log::` call inside a raw SKSE callback (the messaging listener)
///  can block on filesystem I/O.
void SetupLogging() {
    auto path = SKSE::log::log_directory();
    if (!path.has_value()) {
        return;
    }
    *path /= "DovahLinkAdapter.log";
    auto logger =
        spdlog::async_factory_nonblock::create<spdlog::sinks::basic_file_sink_mt>(
            "global", path->string(),
            /*truncate=*/true);
    logger->set_level(spdlog::level::info);
    logger->flush_on(spdlog::level::info);
    spdlog::set_default_logger(std::move(logger));
}

//  TODO(stage4-file-extraction): Move MainMenuOpenedSink to its own
//  plugin/commonlib_adapter_main_menu_sink.hpp/.cpp in the post-Stage-4
//  structural cleanup PR. Temporarily colocated here to hold this PR's
//  changed-file count down; extraction only, no behavior change.
///  Detects a return to Skyrim's main menu via the standard CommonLib
///  `RE::MenuOpenCloseEvent` signal: SKSE's own `MessagingInterface` has no
///  dedicated message type for it (`kPreLoadGame` fires only for a save
///  load/new game, not a return to the main menu). Registered once at
///  `SKSEPluginLoad` and never destroyed, matching every other
///  process-lifetime allocation there.
class MainMenuOpenedSink final
    : public RE::BSTEventSink<RE::MenuOpenCloseEvent> {
  public:
    ///  @param session Notified with `SendPlayContextEnded` every time the
    ///  main menu opens.
    explicit MainMenuOpenedSink(dovahlink::adapter::ipc::IAdapterIpcSession& session)
        : session_(session) {}

    ///  @copydoc RE::BSTEventSink::ProcessEvent
    RE::BSEventNotifyControl
    ProcessEvent(const RE::MenuOpenCloseEvent* event,
                 RE::BSTEventSource<RE::MenuOpenCloseEvent>*) override {
        if (event != nullptr && event->opening &&
            event->menuName == RE::MainMenu::MENU_NAME) {
            session_.SendPlayContextEnded();
        }
        return RE::BSEventNotifyControl::kContinue;
    }

  private:
    ///  Notified with `SendPlayContextEnded` every time the main menu opens.
    dovahlink::adapter::ipc::IAdapterIpcSession& session_;
};

///  Resolves the packaged host executable's path relative to this adapter
///  plugin DLL's own installed directory -- only the loaded plugin binary
///  itself can discover where that is.
///  @return The resolved path, or `std::nullopt` if this module's own file
///  path could not be determined.
std::optional<std::filesystem::path> ResolveAdapterHostExecutablePath() {
    HMODULE moduleHandle = nullptr;
    if (!GetModuleHandleExW(
            GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(&ResolveAdapterHostExecutablePath),
            &moduleHandle)) {
        return std::nullopt;
    }

    constexpr DWORD kMaxPathChars = 32768;
    std::wstring buffer(kMaxPathChars, L'\0');
    DWORD written =
        GetModuleFileNameW(moduleHandle, buffer.data(), kMaxPathChars);
    if (written == 0 || written >= kMaxPathChars) {
        return std::nullopt;
    }
    buffer.resize(written);

    std::filesystem::path pluginDirectory =
        std::filesystem::path(buffer).parent_path();
    return pluginDirectory /
           dovahlink::adapter::process::kAdapterHostExecutableRelativePath;
}

///  The one owner-lifetime identity accepted by this plugin instance. It is
///  populated during `SKSEPluginLoad` and reused by DLL-detach shutdown so
///  every lifecycle participant names the same Skyrim process lifetime.
std::optional<
    std::array<std::byte, dovahlink::adapter::ipc::kIpcOwnerLifetimeIdBytes>>
    gOwnerLifetimeId;

} //  namespace

using namespace std::literals;
SKSEPluginInfo(
        .Version = REL::Version{0, 3, 3, 0}, .Name = "DovahLink Adapter"sv,
        .Author = "Soneka96"sv, .SupportEmail = ""sv,
        .StructCompatibility = SKSE::StructCompatibility::Independent,
        .RuntimeCompatibility = SKSE::VersionIndependence::AddressLibrary,
        .MinimumSKSEVersion = REL::Version{2, 2, 6, 0})

    ///  Initializes the adapter plugin and schedules the private IPC connection
    ///  to start after game data loads.
    SKSEPluginLoad(const SKSE::LoadInterface* skse) {
    //  SKSE-QUIRK: see
    //  ai/context/skse/runtime-quirks.md#skseinit-must-run-before-any-interface-registration
    //  Must run before any SKSE::Get*Interface()-based registration below.
    SKSE::Init(skse);

    if (!dovahlink::adapter::runtime::IsCurrentWindowsVersionSupported()) {
        SKSE::log::error("Unsupported Windows runtime. DovahLink Adapter "
                         "requires Windows 10 or later.");
        return false;
    }

    //  Reject unsupported runtime combinations before any version-sensitive
    //  compatibility work (the achievement/always-active patches below) or any
    //  process-lifetime object construction.
    REL::Version skyrimVersionRel = skse->RuntimeVersion();
    REL::Version skseVersionRel = REL::Version::unpack(skse->SKSEVersion());
    dovahlink::adapter::runtime::RuntimeVersion skyrimVersion{
        skyrimVersionRel[0], skyrimVersionRel[1], skyrimVersionRel[2],
        skyrimVersionRel[3]};
    dovahlink::adapter::runtime::RuntimeVersion skseVersion{
        skseVersionRel[0], skseVersionRel[1], skseVersionRel[2],
        skseVersionRel[3]};
    if (!dovahlink::adapter::runtime::IsSupportedSkyrimVersion(skyrimVersion) ||
        !dovahlink::adapter::runtime::IsSupportedSkseVersion(skseVersion)) {
        SKSE::log::error(
            "Unsupported runtime: Skyrim {}.{}.{}.{}, SKSE {}.{}.{}.{}. "
            "DovahLink Adapter requires exactly Skyrim {}.{}.{}, SKSE {}.{}.{}.",
            skyrimVersion.major, skyrimVersion.minor, skyrimVersion.build,
            skyrimVersion.revision, skseVersion.major, skseVersion.minor,
            skseVersion.build, skseVersion.revision,
            dovahlink::adapter::runtime::kSupportedSkyrimVersion.major,
            dovahlink::adapter::runtime::kSupportedSkyrimVersion.minor,
            dovahlink::adapter::runtime::kSupportedSkyrimVersion.build,
            dovahlink::adapter::runtime::kSupportedSkseVersion.major,
            dovahlink::adapter::runtime::kSupportedSkseVersion.minor,
            dovahlink::adapter::runtime::kSupportedSkseVersion.build);
        return false;
    }

    //  Resolve every failure-prone startup dependency before constructing any
    //  process-lifetime worker. If SKSE rejects this load and immediately
    //  unloads the DLL, there must be no thread-owning object whose destructor
    //  could run under the loader lock.
    auto ownerLifetimeId = dovahlink::adapter::process::DeriveOwnerLifetimeId();
    if (!ownerLifetimeId.has_value()) {
        SKSE::log::error("Unable to derive the current Skyrim process lifetime "
                         "identity; refusing to start the adapter.");
        return false;
    }
    gOwnerLifetimeId = *ownerLifetimeId;

    auto rendezvousPath =
        dovahlink::adapter::process::ResolveDefaultRendezvousFilePath(
            *gOwnerLifetimeId);
    if (!rendezvousPath.has_value()) {
        SKSE::log::error("Unable to resolve the host rendezvous file path; the "
                         "current Windows user has no local application-data "
                         "directory.");
        return false;
    }
    auto hostExecutablePath = ResolveAdapterHostExecutablePath();
    if (!hostExecutablePath.has_value()) {
        SKSE::log::error(
            "Unable to resolve this adapter plugin's own installed directory.");
        return false;
    }
    auto* messaging = static_cast<SKSE::MessagingInterface*>(
        skse->QueryInterface(SKSE::LoadInterface::kMessaging));
    if (!messaging) {
        SKSE::log::error("Unable to obtain SKSE's messaging interface; cannot "
                         "defer startup to kDataLoaded.");
        return false;
    }

    //  Configure asynchronous diagnostics only after every fatal startup guard
    //  has passed. A rejected load can then be unloaded without first creating
    //  the logger's background infrastructure.
    SetupLogging();

    //  Runtime compatibility toggles (both default enabled): keeping Skyrim
    //  active while unfocused so the pairing code stays visible while the
    //  player is in the DovahLink app, and patching achievement eligibility
    //  back on for a modded load order. Read once, early, so both outcomes are
    //  logged before any other setup and can be disabled independently through
    //  Data/SKSE/Plugins/DovahLinkAdapter.ini.
    static dovahlink::adapter::runtime::
        FilesystemAdapterGameBehaviorConfigFileReader gameBehaviorConfigReader;
    dovahlink::adapter::runtime::AdapterGameBehaviorConfig behaviorConfig =
        dovahlink::adapter::runtime::ReadAdapterGameBehaviorConfig(
            gameBehaviorConfigReader,
            dovahlink::adapter::runtime::kAdapterGameBehaviorConfigPath);
    SKSE::log::info("Always-active mode: {}",
                    behaviorConfig.alwaysActive ? "enabled" : "disabled");
    SKSE::log::info("Achievement compatibility: {}",
                    behaviorConfig.achievementCompat ? "enabled" : "disabled");
    if (behaviorConfig.alwaysActive) {
        dovahlink::adapter::runtime::ApplyAlwaysActiveSetting();
    }
    if (behaviorConfig.achievementCompat) {
        dovahlink::adapter::runtime::InstallAchievementCompatibilityPatch();
    }

    //  CommonLibAdapterTaskMarshaller and CommonLibAdapterPairingNotificationSink
    //  require CommonLib, so they are constructed here -- like AdapterRuntime
    //  below, as intentional process-lifetime allocations, never deleted --
    //  and passed into AdapterRuntime, which is otherwise CommonLib-free.
    //  CommonLibAdapterNativeCaptureRouter also requires CommonLib, but it
    //  additionally needs AdapterRuntime's own capture queue, which does not
    //  exist yet at this point -- so it is supplied as a factory instead of
    //  an already-constructed instance; see AdapterRuntime's own constructor
    //  doc comment for why.
    static auto* taskMarshaller =
        new dovahlink::adapter::runtime::CommonLibAdapterTaskMarshaller;
    static auto* pairingNotificationSink =
        new dovahlink::adapter::ipc::CommonLibAdapterPairingNotificationSink;
    //  Generates a fresh play-context identity for each real New Game/Load
    //  Game SKSE message below. CommonLib-free, but kept alongside the other
    //  process-lifetime allocations the messaging listener's own lambda
    //  captures.
    static auto* playContextGenerator =
        new dovahlink::adapter::identity::AdapterPlayContextGenerator;

    dovahlink::adapter::identity::AdapterInstanceIdGenerator idGenerator;
    dovahlink::adapter::plugin::AdapterStartupContext startupContext{
        .instanceId = idGenerator.Generate(),
        .ownerLifetimeId = *gOwnerLifetimeId,
        .rendezvousPath = *rendezvousPath,
        .hostExecutablePath = *hostExecutablePath,
    };

    //  The one process-lifetime AdapterRuntime, owning the rest of the
    //  object graph. Never destroyed for the same loader-lock reason
    //  documented on AdapterRuntime itself.
    static auto* runtime = new dovahlink::adapter::plugin::AdapterRuntime(
        startupContext, *taskMarshaller, *pairingNotificationSink,
        [](dovahlink::adapter::capture::IAdapterCaptureHandoffQueue& queue,
           dovahlink::adapter::identity::IAdapterPlayContextState& playContextState) {
            return std::make_unique<
                dovahlink::adapter::runtime::CommonLibAdapterNativeCaptureRouter>(
                queue, playContextState);
        },
        [](const dovahlink::adapter::capture::AdapterCaptureWorkItem& item) {
            SKSE::log::info("Adapter capture drained for intent key {}.",
                            item.intentKey);
        },
        [](const dovahlink::adapter::capture::AdapterCaptureWorkItem& item) {
            SKSE::log::warn("Adapter capture queue rejected intent key {}.",
                            item.intentKey);
        },
        [] {
            SKSE::log::warn("Adapter IPC session rejected a deferred "
                            "game-thread dispatch at capacity.");
        });

    dovahlink::adapter::papyrus::InstallAdapterStatusPapyrusAdapter(
        runtime->Session());
    dovahlink::adapter::papyrus::InstallAdapterTrustAdminPapyrusAdapter(
        runtime->Session(), *taskMarshaller);

    //  Detects a return to the main menu; see MainMenuOpenedSink's own doc
    //  comment for why SKSE's messaging interface cannot signal this itself.
    static auto* mainMenuOpenedSink =
        new MainMenuOpenedSink(runtime->Session());
    RE::UI::GetSingleton()->AddEventSink(mainMenuOpenedSink);

    //  SKSE-QUIRK: see
    //  ai/context/skse/runtime-quirks.md#one-messaginginterfaceregisterlistener-call-per-plugin
    //  SKSE allows exactly one MessagingInterface::RegisterListener call per
    //  plugin. dovahlink_adapter_plugin_test.cpp enforces this structurally;
    //  it fails if a second RegisterListener call is ever added to this file.
    messaging->RegisterListener([](SKSE::MessagingInterface::Message* message) {
        if (message->type == SKSE::MessagingInterface::kDataLoaded) {
            runtime->Start();
            SKSE::log::info(
                "DovahLink Adapter connecting to the private host IPC channel.");
        }
        //  Ends the current context the moment loading starts, before the new
        //  state is actually loaded: no capture taken during the loading
        //  window can be attributed to either the old or the not-yet-existing
        //  new context. This never establishes a context itself -- only
        //  kNewGame/kPostLoadGame below do that.
        if (message->type == SKSE::MessagingInterface::kPreLoadGame) {
            runtime->Session().SendPlayContextEnded();
        }
        //  Every genuinely new game and every load -- including a reload of
        //  the same save file -- gets its own fresh play-context identity
        //  unconditionally: state captured before a load is not guaranteed
        //  continuous with state after it, so there is no case where
        //  deduplicating against the previous identity would be correct here.
        if (message->type == SKSE::MessagingInterface::kNewGame ||
            message->type == SKSE::MessagingInterface::kPostLoadGame) {
            runtime->Session().SendPlayContextChanged(
                playContextGenerator->Generate());
        }
    });

    return true;
}

///  Signals the launched host's shutdown-request event, and nothing else:
///  `DLL_PROCESS_DETACH` runs under the loader lock, where a join or wait
///  can deadlock or hang past the operating system's own patience for
///  process exit. This never calls the full ordered shutdown sequence (see
///  `dovahlink::adapter::process::AdapterShutdownOrchestrator`) -- there is
///  currently no confirmed safe hook to call that sequence from before the
///  process disappears; `ExitProcess`-driven teardown reclaims every
///  adapter-side thread, socket, and handle regardless of whether it ran.
BOOL APIENTRY DllMain(HMODULE, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_DETACH && gOwnerLifetimeId.has_value()) {
        dovahlink::adapter::process::WindowsEventAdapterHostShutdownRequester(
            *gOwnerLifetimeId)
            .RequestShutdown();
    }
    return TRUE;
}
