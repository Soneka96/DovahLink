#include "plugin/adapter_runtime.hpp"

#include "ipc/adapter_ipc_connection_callbacks.hpp"

#include <utility>

namespace dovahlink::adapter::plugin {

AdapterRuntime::AdapterRuntime(
    AdapterStartupContext startupContext,
    runtime::IAdapterTaskMarshaller& taskMarshaller,
    ipc::IAdapterPairingNotificationSink& pairingNotificationSink,
    std::function<void(const capture::AdapterCaptureWorkItem&)>
        onCaptureDrained,
    std::function<void(const capture::AdapterCaptureWorkItem&)>
        onCaptureQueueRejected,
    std::function<void()> onGameThreadDispatchRejected)
    : taskMarshaller_(taskMarshaller),
      pairingNotificationSink_(pairingNotificationSink) {
    captureQueue_ = std::make_unique<capture::AdapterCaptureHandoffQueue>(
        std::move(onCaptureDrained), std::move(onCaptureQueueRejected));
    dispatcher_ = std::make_unique<dispatch::AdapterNativeDispatcher>();

    session_ = std::make_unique<ipc::AdapterIpcSession>(
        startupContext.instanceId, startupContext.ownerLifetimeId,
        taskMarshaller_, *dispatcher_, *captureQueue_, pairingNotificationSink_,
        std::move(onGameThreadDispatchRejected));

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
