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
#include "ipc/adapter_ipc_peer_proof_provider.hpp"
#include "ipc/adapter_ipc_session.hpp"
#include "ipc/ipc_frame_codec.hpp"
#include "ipc/winsock_adapter_ipc_socket.hpp"
#include "papyrus/commonlib_adapter_status_papyrus_adapter.hpp"
#include "runtime/commonlib_adapter_task_marshaller.hpp"

#include <spdlog/async.h>
#include <spdlog/sinks/basic_file_sink.h>

#include <cstddef>
#include <cstdint>
#include <utility>
#include <vector>

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

///  Provisional private-IPC target port and peer-proof token. The host
///  binds an OS-assigned ephemeral port and generates its own random proof
///  value at startup; handing the adapter the real values it needs to reach
///  that specific host process is process-launch work this concept's
///  non-goals explicitly defer to Concept 04 ("Host process launch...
///  belong to concept 04"). These placeholders let the connect/reconnect
///  mechanism itself be exercised safely today -- proven never to block or
///  crash Skyrim capture when nothing is listening -- without pretending to
///  solve port/token discovery here.
constexpr std::uint16_t kProvisionalHostIpcPort = 0;
const std::vector<std::byte> kProvisionalPeerProofToken{};

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
  SetupLogging();

  //  SKSE-QUIRK: see
  //  ai/context/skse/runtime-quirks.md#skseinit-must-run-before-any-interface-registration
  //  Must run before any SKSE::Get*Interface()-based registration below.
  SKSE::Init(skse);

  //  Plugin-lifetime adapter state, declared as function-local statics in
  //  the exact order they depend on each other, mirroring
  //  bridge/plugin/dovahlink_bridge_plugin.cpp's composition-root pattern.
  static dovahlink::adapter::capture::AdapterCaptureHandoffQueue captureQueue(
      [](const dovahlink::adapter::capture::AdapterCaptureWorkItem &item) {
        SKSE::log::info("Adapter capture drained for intent key {}.",
                        item.intentKey);
      },
      [](const dovahlink::adapter::capture::AdapterCaptureWorkItem &item) {
        SKSE::log::warn("Adapter capture queue rejected intent key {}.",
                        item.intentKey);
      });
  static dovahlink::adapter::runtime::CommonLibAdapterTaskMarshaller
      taskMarshaller;
  static dovahlink::adapter::dispatch::AdapterNativeDispatcher dispatcher;

  dovahlink::adapter::identity::AdapterInstanceIdGenerator idGenerator;
  static dovahlink::adapter::identity::AdapterInstanceId instanceId =
      idGenerator.Generate();
  static dovahlink::adapter::ipc::FixedAdapterIpcPeerProofProvider
      peerProofProvider(kProvisionalPeerProofToken);
  static dovahlink::adapter::ipc::AdapterIpcSession session(
      instanceId, peerProofProvider, taskMarshaller, dispatcher, captureQueue);

  static dovahlink::adapter::ipc::WinsockAdapterIpcSocket socket(
      kProvisionalHostIpcPort);
  static dovahlink::adapter::ipc::IpcFrameCodec codec;
  static dovahlink::adapter::ipc::AdapterIpcConnection connection(
      socket, codec,
      dovahlink::adapter::ipc::AdapterIpcConnectionCallbacks{
          .onConnected = [] { session.HandleConnected(); },
          .onMessageReceived =
              [](const dovahlink::adapter::ipc::IpcMessage &message) {
                return session.HandleMessage(message);
              },
          .onDecodeFailure = [] { session.HandleDecodeFailure(); },
          .onDisconnected = [] { session.HandleDisconnected(); },
      });
  session.AttachConnection(connection);

  dovahlink::adapter::papyrus::InstallAdapterStatusPapyrusAdapter(session);

  auto *messaging = static_cast<SKSE::MessagingInterface *>(
      skse->QueryInterface(SKSE::LoadInterface::kMessaging));
  if (!messaging) {
    SKSE::log::error("Unable to obtain SKSE's messaging interface; cannot "
                     "defer startup to kDataLoaded.");
    return false;
  }
  //  SKSE-QUIRK: see
  //  ai/context/skse/runtime-quirks.md#one-messaginginterfaceregisterlistener-call-per-plugin
  //  SKSE allows exactly one MessagingInterface::RegisterListener call per
  //  plugin. dovahlink_adapter_plugin_test.cpp enforces this structurally;
  //  it fails if a second RegisterListener call is ever added to this file.
  messaging->RegisterListener([](SKSE::MessagingInterface::Message *message) {
    if (message->type == SKSE::MessagingInterface::kDataLoaded) {
      connection.Start();
      SKSE::log::info(
          "DovahLink Adapter connecting to the private host IPC channel.");
    }
  });

  return true;
}
