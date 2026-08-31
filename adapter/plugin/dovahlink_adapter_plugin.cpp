//  SKSE plugin entry point and plugin-lifetime wiring for the adapter. This
//  file contains the runtime-specific composition layer; the underlying
//  components remain testable without a running Skyrim process.

#include "SKSE/SKSE.h"

#include "capture/adapter_capture_handoff_queue.hpp"
#include "capture/adapter_capture_work_item.hpp"
#include "dispatch/adapter_native_dispatcher.hpp"
#include "identity/adapter_instance_id.hpp"
#include "identity/adapter_instance_id_generator.hpp"
#include "ipc/adapter_ipc_connection.hpp"
#include "ipc/adapter_ipc_connection_callbacks.hpp"
#include "ipc/adapter_ipc_session.hpp"
#include "ipc/ipc_frame_codec.hpp"
#include "ipc/winsock_adapter_ipc_socket.hpp"
#include "papyrus/commonlib_adapter_status_papyrus_adapter.hpp"
#include "process/adapter_host_constants.hpp"
#include "process/adapter_host_process_launcher.hpp"
#include "process/adapter_host_rendezvous_reader.hpp"
#include "process/adapter_host_shutdown_requester.hpp"
#include "process/adapter_host_supervisor.hpp"
#include "process/adapter_owner_lifetime_id.hpp"
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

///  Minimal file logging to the SKSE log directory, mirroring
///  `bridge/plugin/dovahlink_bridge_plugin.cpp`'s `SetupLogging`: async so no
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

///  Resolves the packaged host executable's path relative to this adapter
///  plugin DLL's own installed directory -- only the loaded plugin binary
///  itself can discover where that is.
///  @return The resolved path, or `std::nullopt` if this module's own file
///  path could not be determined.
std::optional<std::filesystem::path> ResolveAdapterHostExecutablePath() {
  HMODULE moduleHandle = nullptr;
  if (!GetModuleHandleExW(
          GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
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
    SKSEPluginLoad(const SKSE::LoadInterface *skse) {
  //  SKSE-QUIRK: see
  //  ai/context/skse/runtime-quirks.md#skseinit-must-run-before-any-interface-registration
  //  Must run before any SKSE::Get*Interface()-based registration below.
  SKSE::Init(skse);

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
  auto *messaging = static_cast<SKSE::MessagingInterface *>(
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

  //  Plugin-lifetime adapter state, declared as function-local statics in
  //  the exact order they depend on each other. The pointed-to objects are
  //  intentionally process-lifetime allocations: 1B does not support live
  //  DLL unload/reload, and allowing their destructors to join under DLL
  //  detach would violate the loader-lock boundary. Windows reclaims these
  //  objects, threads, sockets, and handles when Skyrim exits.
  static auto *captureQueue =
      new dovahlink::adapter::capture::AdapterCaptureHandoffQueue(
          [](const dovahlink::adapter::capture::AdapterCaptureWorkItem &item) {
            SKSE::log::info("Adapter capture drained for intent key {}.",
                            item.intentKey);
          },
          [](const dovahlink::adapter::capture::AdapterCaptureWorkItem &item) {
            SKSE::log::warn("Adapter capture queue rejected intent key {}.",
                            item.intentKey);
          });
  static auto *taskMarshaller =
      new dovahlink::adapter::runtime::CommonLibAdapterTaskMarshaller;
  static auto *dispatcher =
      new dovahlink::adapter::dispatch::AdapterNativeDispatcher;

  dovahlink::adapter::identity::AdapterInstanceIdGenerator idGenerator;
  static dovahlink::adapter::identity::AdapterInstanceId instanceId =
      idGenerator.Generate();
  const auto &stableOwnerLifetimeId = *gOwnerLifetimeId;
  static auto *session = new dovahlink::adapter::ipc::AdapterIpcSession(
      instanceId, stableOwnerLifetimeId, *taskMarshaller, *dispatcher,
      *captureQueue);

  static auto *socket = new dovahlink::adapter::ipc::WinsockAdapterIpcSocket(0);
  static auto *codec = new dovahlink::adapter::ipc::IpcFrameCodec;

  //  Process-lifecycle discovery: an adopt-from-rendezvous-or-launch-fresh
  //  supervisor keeps the connection's complete target snapshot pointed at a
  //  authenticated target for this plugin's whole lifetime.
  static auto *reader =
      new dovahlink::adapter::process::FileAdapterHostRendezvousReader(
          *rendezvousPath);
  static auto *launcher =
      new dovahlink::adapter::process::Win32AdapterHostProcessLauncher(
          *hostExecutablePath, stableOwnerLifetimeId);
  static auto *supervisor =
      static_cast<dovahlink::adapter::process::AdapterHostSupervisor *>(
          nullptr);
  static auto *connection = new dovahlink::adapter::ipc::AdapterIpcConnection(
      *socket, *codec,
      dovahlink::adapter::ipc::AdapterIpcConnectionCallbacks{
          .onTargetConnected =
              [](const dovahlink::adapter::ipc::AdapterIpcTarget &target) {
                session->HandleConnected(target);
              },
          .onMessageReceived =
              [](const dovahlink::adapter::ipc::IpcMessage &message) {
                return session->HandleMessage(message);
              },
          .onDecodeFailure = [] { session->HandleDecodeFailure(); },
          .onDisconnected = [] { session->HandleDisconnected(); },
          .onAttemptFinished =
              [](std::uint64_t targetGeneration,
                 dovahlink::adapter::ipc::AdapterIpcAttemptOutcome outcome) {
                supervisor->NotifyConnectionLost(targetGeneration, outcome);
              },
      });
  if (supervisor == nullptr) {
    supervisor = new dovahlink::adapter::process::AdapterHostSupervisor(
        *reader, *launcher, *connection);
  }
  session->AttachConnection(*connection);

  dovahlink::adapter::papyrus::InstallAdapterStatusPapyrusAdapter(*session);

  //  SKSE-QUIRK: see
  //  ai/context/skse/runtime-quirks.md#one-messaginginterfaceregisterlistener-call-per-plugin
  //  SKSE allows exactly one MessagingInterface::RegisterListener call per
  //  plugin. dovahlink_adapter_plugin_test.cpp enforces this structurally;
  //  it fails if a second RegisterListener call is ever added to this file.
  messaging->RegisterListener([](SKSE::MessagingInterface::Message *message) {
    if (message->type == SKSE::MessagingInterface::kDataLoaded) {
      supervisor->Start();
      SKSE::log::info(
          "DovahLink Adapter connecting to the private host IPC channel.");
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
