#pragma once

#include "capture/adapter_capture_handoff_queue.hpp"
#include "capture/adapter_capture_work_item.hpp"
#include "dispatch/adapter_native_capture_router.hpp"
#include "identity/adapter_play_context_state.hpp"
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
#include <future>
#include <memory>
#include <mutex>
#include <vector>

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
    ///  `dovahlink_adapter_core` -- does not depend on. `captureRouterFactory`
    ///  is a factory rather than an already-constructed collaborator like the
    ///  other two: its only real implementation also requires CommonLib, but
    ///  it needs `captureQueue_`, which does not exist until this
    ///  constructor's body runs, so the caller cannot construct it in
    ///  advance -- this constructor calls the factory itself, immediately
    ///  after `captureQueue_` is built. For the same CommonLib-boundary
    ///  reason, `onCaptureDrained`, `onCaptureQueueRejected`, and
    ///  `onGameThreadDispatchRejected` are diagnostic callbacks the caller
    ///  supplies rather than `SKSE::log` calls made directly here.
    ///  @param startupContext The resolved startup values this graph is built
    ///  from.
    ///  @param taskMarshaller Marshals work onto the Skyrim game thread.
    ///  @param pairingNotificationSink Presents pairing codes at the
    ///  Skyrim-facing display seam.
    ///  @param captureRouterFactory Builds the native capture router given
    ///  the capture queue it enqueues spontaneous native-event captures onto
    ///  and the shared play-context state it stamps those captures with.
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
        std::function<std::unique_ptr<dispatch::IAdapterNativeCaptureRouter>(
            capture::IAdapterCaptureHandoffQueue&,
            identity::IAdapterPlayContextState&)>
            captureRouterFactory,
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
    std::unique_ptr<identity::AdapterPlayContextState> playContextState_;
    std::unique_ptr<capture::AdapterCaptureHandoffQueue> captureQueue_;
    std::unique_ptr<dispatch::IAdapterNativeCaptureRouter> captureRouter_;
    std::unique_ptr<ipc::AdapterIpcSession> session_;
    std::unique_ptr<ipc::WinsockAdapterIpcSocket> socket_;
    std::unique_ptr<ipc::IpcFrameCodec> codec_;
    std::unique_ptr<process::FileAdapterHostRendezvousReader> reader_;
    std::unique_ptr<process::Win32AdapterHostProcessLauncher> launcher_;
    std::unique_ptr<ipc::AdapterIpcConnection> connection_;
    std::unique_ptr<process::AdapterHostSupervisor> supervisor_;

    ///  Guards `captureRejectionResetFutures_` against concurrent pushes: a
    ///  rejected reliable Event can be reported from the Skyrim game thread
    ///  at any time, including while another rejection's own reset is still
    ///  being dispatched.
    std::mutex captureRejectionResetFuturesMutex_;
    ///  One entry per connection reset dispatched for a rejected reliable
    ///  Event, via `std::async(std::launch::async, ...)` rather than a
    ///  detached `std::thread`: a `std::future` obtained this way blocks in
    ///  its own destructor until the dispatched `connection_->Stop()` call
    ///  actually finishes. Declared after `connection_` so it is destroyed
    ///  first (member destruction runs in reverse declaration order),
    ///  guaranteeing every in-flight reset completes -- and can never touch
    ///  a freed `connection_` -- before `connection_` itself is destroyed.
    std::vector<std::future<void>> captureRejectionResetFutures_;
};

} //  namespace dovahlink::adapter::plugin
