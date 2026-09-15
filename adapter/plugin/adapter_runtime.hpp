#pragma once

#include "capture/adapter_capture_handoff_queue.hpp"
#include "capture/adapter_capture_work_item.hpp"
#include "dispatch/adapter_native_dispatcher.hpp"
#include "ipc/adapter_ipc_connection.hpp"
#include "ipc/adapter_ipc_session.hpp"
#include "ipc/adapter_pairing_notification_sink.hpp"
#include "ipc/ipc_frame_codec.hpp"
#include "ipc/winsock_adapter_ipc_socket.hpp"
#include "plugin/adapter_startup_context.hpp"
#include "process/adapter_host_process_launcher.hpp"
#include "process/adapter_host_rendezvous_reader.hpp"
#include "process/adapter_host_supervisor.hpp"
#include "runtime/adapter_task_marshaller.hpp"

#include <functional>
#include <memory>

namespace dovahlink::adapter::plugin {

///  Owns the adapter's process-lifetime object graph: the capture handoff
///  queue, native dispatcher, private IPC session/connection/transport, and
///  the host-discovery supervisor.
///
///  `SKSEPluginLoad` constructs exactly one instance and never destroys it,
///  because destroying these worker-owning collaborators during DLL detach
///  could block under the Windows loader lock.
class AdapterRuntime final {
  public:
    ///  Constructs the complete graph from already-resolved startup values.
    ///  `taskMarshaller` and `pairingNotificationSink` are supplied by the
    ///  caller rather than constructed here because their only implementations
    ///  require CommonLib, which this class -- like the rest of
    ///  `dovahlink_adapter_core` -- does not depend on. For the same reason,
    ///  `onCaptureDrained`, `onCaptureQueueRejected`, and
    ///  `onGameThreadDispatchRejected` are diagnostic callbacks the caller
    ///  supplies rather than `SKSE::log` calls made directly here.
    ///  @param startupContext The resolved startup values this graph is built
    ///  from.
    ///  @param taskMarshaller Marshals work onto the Skyrim game thread.
    ///  @param pairingNotificationSink Presents pairing codes at the
    ///  Skyrim-facing display seam.
    ///  @param onCaptureDrained Invoked for each capture item the handoff
    ///  queue's worker thread drains.
    ///  @param onCaptureQueueRejected Invoked when the handoff queue rejects a
    ///  capture item at capacity.
    ///  @param onGameThreadDispatchRejected Invoked when the private IPC
    ///  session's deferred game-thread dispatch is rejected at capacity.
    AdapterRuntime(
        AdapterStartupContext startupContext,
        runtime::IAdapterTaskMarshaller& taskMarshaller,
        ipc::IAdapterPairingNotificationSink& pairingNotificationSink,
        std::function<void(const capture::AdapterCaptureWorkItem&)>
            onCaptureDrained,
        std::function<void(const capture::AdapterCaptureWorkItem&)>
            onCaptureQueueRejected,
        std::function<void()> onGameThreadDispatchRejected);

    ///  Stops the supervisor and the connection before either's automatic
    ///  member destructor runs. Declaration order alone is not enough here:
    ///  `connection_` owns its own background thread, independent of
    ///  `supervisor_`'s, and that thread's own completion callbacks
    ///  (`onAttemptFinished`, ...) call into `supervisor_`. Stopping only
    ///  `supervisor_` first would leave a window where its automatic
    ///  destructor has already run while `connection_`'s thread is still
    ///  live and can call a now-destroyed `supervisor_`.
    ~AdapterRuntime();

    AdapterRuntime(const AdapterRuntime&) = delete;
    AdapterRuntime& operator=(const AdapterRuntime&) = delete;
    AdapterRuntime(AdapterRuntime&&) = delete;
    AdapterRuntime& operator=(AdapterRuntime&&) = delete;

    ///  Starts host discovery. Idempotent: a call while discovery is already
    ///  running has no effect.
    void Start();

    ///  The private IPC session, for the Skyrim-facing Papyrus adapters
    ///  `SKSEPluginLoad` installs against it.
    ipc::IAdapterIpcSession& Session();

  private:
    ///  Marshals work onto the Skyrim game thread. Not owned: the caller's
    ///  concrete implementation requires CommonLib.
    runtime::IAdapterTaskMarshaller& taskMarshaller_;
    ///  Presents pairing codes at the Skyrim-facing display seam. Not owned:
    ///  the caller's concrete implementation requires CommonLib.
    ipc::IAdapterPairingNotificationSink& pairingNotificationSink_;

    ///  Constructed in this exact declaration order: each collaborator below
    ///  depends only on ones declared above it. The destructor above stops
    ///  `supervisor_` and `connection_` explicitly before any automatic
    ///  member destructor runs; declaration order alone does not make their
    ///  teardown safe, since `connection_` owns a background thread
    ///  independent of `supervisor_`'s own.
    std::unique_ptr<capture::AdapterCaptureHandoffQueue> captureQueue_;
    std::unique_ptr<dispatch::AdapterNativeDispatcher> dispatcher_;
    std::unique_ptr<ipc::AdapterIpcSession> session_;
    std::unique_ptr<ipc::WinsockAdapterIpcSocket> socket_;
    std::unique_ptr<ipc::IpcFrameCodec> codec_;
    std::unique_ptr<process::FileAdapterHostRendezvousReader> reader_;
    std::unique_ptr<process::Win32AdapterHostProcessLauncher> launcher_;
    std::unique_ptr<ipc::AdapterIpcConnection> connection_;
    std::unique_ptr<process::AdapterHostSupervisor> supervisor_;
};

} //  namespace dovahlink::adapter::plugin
