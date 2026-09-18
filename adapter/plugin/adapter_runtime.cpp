#include "plugin/adapter_runtime.hpp"

#include "ipc/adapter_ipc_connection_callbacks.hpp"

#include <future>
#include <mutex>
#include <utility>

namespace dovahlink::adapter::plugin {

AdapterRuntime::AdapterRuntime(
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
    std::function<void()> onGameThreadDispatchRejected)
    : taskMarshaller_(taskMarshaller),
      pairingNotificationSink_(pairingNotificationSink) {
    //  Captures the diagnostic callback by value into a wrapper that also
    //  reports the drained item to the host over IPC. The wrapper captures
    //  `this` rather than `*session_` directly -- `session_` is constructed
    //  below, after this queue -- but the wrapper only ever runs once the
    //  queue's worker thread actually drains an enqueued item, which cannot
    //  happen before Start() is called, well after this constructor and
    //  `session_` have both finished.
    playContextState_ = std::make_unique<identity::AdapterPlayContextState>();

    captureQueue_ = std::make_unique<capture::AdapterCaptureHandoffQueue>(
        [this, onCaptureDrained = std::move(onCaptureDrained)](
            const capture::AdapterCaptureWorkItem& item) {
            session_->SendCaptureResult(item);
            if (onCaptureDrained) {
                onCaptureDrained(item);
            }
        },
        [this, onCaptureQueueRejected = std::move(onCaptureQueueRejected)](
            const capture::AdapterCaptureWorkItem& item) {
            //  A rejected reliable Event (for example the level-changed
            //  event), unlike a rejected Snapshot sample that the next poll
            //  can simply recapture, can never be silently lost: continuity
            //  is no longer trustworthy once one is dropped, so the
            //  connection is reset and the adapter's normal reconnect drives
            //  a fresh resynchronization. Dispatched via std::async rather
            //  than called inline, both because this callback can run on the
            //  Skyrim game thread (which must never block on Stop()'s own
            //  wait for the connection's background thread to finish) and
            //  ahead of the diagnostic callback below, so an exception from
            //  a caller-supplied diagnostic can never suppress the reset.
            //  The resulting future is kept in captureRejectionResetFutures_
            //  rather than discarded: its destructor blocks until this
            //  dispatched Stop() call actually finishes, and that member is
            //  declared to be destroyed before connection_ is, so a reset
            //  can never still be running against an already-freed
            //  connection_ during teardown.
            if (item.source == capture::CaptureSourceKind::kEvent) {
                std::lock_guard<std::mutex> lock(captureRejectionResetFuturesMutex_);
                captureRejectionResetFutures_.push_back(
                    std::async(std::launch::async, [this] { connection_->Stop(); }));
            }
            if (onCaptureQueueRejected) {
                onCaptureQueueRejected(item);
            }
        });
    captureRouter_ = captureRouterFactory(*captureQueue_, *playContextState_);

    session_ = std::make_unique<ipc::AdapterIpcSession>(
        startupContext.instanceId, startupContext.ownerLifetimeId,
        taskMarshaller_, *captureRouter_, *captureQueue_, pairingNotificationSink_,
        *playContextState_, std::move(onGameThreadDispatchRejected));

    socket_ = std::make_unique<ipc::WinsockAdapterIpcSocket>(0);
    codec_ = std::make_unique<ipc::IpcFrameCodec>();

    reader_ = std::make_unique<process::FileAdapterHostRendezvousReader>(
        startupContext.rendezvousPath);
    launcher_ = std::make_unique<process::Win32AdapterHostProcessLauncher>(
        startupContext.hostExecutablePath, startupContext.ownerLifetimeId);

    //  connection_'s callbacks capture `this` rather than referencing
    //  supervisor_ directly: supervisor_ is constructed after connection_
    //  below (its own constructor requires an already-constructed connection),
    //  but no callback runs until Start() is called, well after both are
    //  fully constructed.
    connection_ = std::make_unique<ipc::AdapterIpcConnection>(
        *socket_, *codec_,
        ipc::AdapterIpcConnectionCallbacks{
            .onTargetConnected =
                [this](const ipc::AdapterIpcTarget& target) {
                    session_->HandleConnected(target);
                },
            .onMessageReceived =
                [this](const ipc::IpcMessage& message) {
                    return session_->HandleMessage(message);
                },
            .onDecodeFailure = [this] { session_->HandleDecodeFailure(); },
            .onDisconnected = [this] { session_->HandleDisconnected(); },
            .onAttemptFinished =
                [this](std::uint64_t targetGeneration,
                       ipc::AdapterIpcAttemptOutcome outcome) {
                    supervisor_->NotifyConnectionLost(targetGeneration, outcome);
                },
            .onClosing = [this] { session_->HandleClosing(); },
        });

    supervisor_ = std::make_unique<process::AdapterHostSupervisor>(
        *reader_, *launcher_, *connection_);

    session_->AttachConnection(*connection_);
}

AdapterRuntime::~AdapterRuntime() {
    //  Stop the supervisor's own thread first: after RequestStop() returns,
    //  it can never start another discovery round or call connection_.Start()
    //  again. Then stop the connection's own thread. Any onAttemptFinished/
    //  onDisconnected callback it fires in between still safely reaches
    //  supervisor_ -- not yet destroyed, only marked stopping -- because
    //  AdapterHostSupervisor::NotifyConnectionLost no-ops once stopping_ is
    //  set.
    //
    //  Both calls can rethrow a std::thread::join failure. A destructor is
    //  implicitly noexcept, so an escaping exception here would terminate the
    //  whole process instead of unwinding one AdapterRuntime -- the same
    //  reason AdapterIpcConnection's own destructor contains Stop()'s.
    try {
        supervisor_->RequestStop();
    } catch (...) {
    }
    try {
        connection_->Stop();
    } catch (...) {
    }
}

void AdapterRuntime::Start() { supervisor_->Start(); }

ipc::IAdapterIpcSession& AdapterRuntime::Session() { return *session_; }

} //  namespace dovahlink::adapter::plugin
