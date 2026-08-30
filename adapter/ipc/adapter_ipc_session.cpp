#include "ipc/adapter_ipc_session.hpp"

#include "ipc/adapter_ipc_connection.hpp"

#include <type_traits>
#include <variant>

namespace dovahlink::adapter::ipc {

AdapterIpcSession::AdapterIpcSession(
    identity::AdapterInstanceId instanceId,
    IAdapterIpcPeerProofProvider &peerProofProvider,
    runtime::IAdapterTaskMarshaller &taskMarshaller,
    dispatch::IAdapterNativeDispatcher &dispatcher,
    capture::IAdapterCaptureHandoffQueue &captureQueue)
    : instanceId_(instanceId), peerProofProvider_(peerProofProvider),
      taskMarshaller_(taskMarshaller), dispatcher_(dispatcher),
      captureQueue_(captureQueue) {}

AdapterIpcSession::~AdapterIpcSession() {
  std::lock_guard<std::mutex> lock(*callbackMutex_);
  lifetimeToken_->store(false);
}

void AdapterIpcSession::AttachConnection(IAdapterIpcConnection &connection) {
  connection_ = &connection;
}

IpcMessage AdapterIpcSession::PrepareHello() {
  return IpcMessage{IpcHelloMessage{
      .correlationId = NextCorrelationId(),
      .adapterInstanceId = instanceId_.value,
      .peerProofToken = peerProofProvider_.Token(),
  }};
}

void AdapterIpcSession::HandleConnected() {
  if (connection_ != nullptr) {
    connection_->TrySend(PrepareHello());
  }
}

bool AdapterIpcSession::HandleMessage(const IpcMessage &message) {
  return std::visit(
      [this](const auto &value) -> bool {
        using T = std::decay_t<decltype(value)>;
        if constexpr (std::is_same_v<T, IpcHelloAckMessage>) {
          std::lock_guard<std::mutex> lock(availableMutex_);
          available_ = value.accepted;
          return true;
        } else if constexpr (std::is_same_v<T,
                                            IpcResynchronizeRequestMessage>) {
          HandleResynchronizeRequest(value);
          return true;
        } else if constexpr (std::is_same_v<T, IpcListenEventMessage>) {
          HandleListenEvent(value);
          return true;
        } else if constexpr (std::is_same_v<T, IpcReadSampleMessage>) {
          HandleReadSample(value);
          return true;
        } else if constexpr (std::is_same_v<T, IpcCloseMessage>) {
          return false;
        } else if constexpr (std::is_same_v<T, IpcRejectMessage>) {
          return true;
        } else if constexpr (std::is_same_v<T, IpcCancelMessage>) {
          return true;
        } else {
          //  IpcHelloMessage and IpcResynchronizeResultMessage are
          //  adapter-outbound only; receiving either is a protocol
          //  violation from the host.
          if (connection_ != nullptr) {
            connection_->TrySend(IpcMessage{IpcRejectMessage{
                .correlationId = value.correlationId,
                .reason = IpcRejectReason::kUnknownMessageKind}});
          }
          return false;
        }
      },
      message);
}

void AdapterIpcSession::HandleDecodeFailure() {
  if (connection_ != nullptr) {
    connection_->TrySend(IpcMessage{
        IpcCloseMessage{.correlationId = 0, .reason = IpcCloseReason::kError}});
  }
}

void AdapterIpcSession::HandleDisconnected() {
  std::lock_guard<std::mutex> lock(availableMutex_);
  available_ = false;
}

bool AdapterIpcSession::IsHostAvailable() const {
  std::lock_guard<std::mutex> lock(availableMutex_);
  return available_;
}

void AdapterIpcSession::HandleResynchronizeRequest(
    const IpcResynchronizeRequestMessage &request) {
  std::uint64_t correlationId = request.correlationId;
  auto callbackMutex = callbackMutex_;
  auto lifetimeToken = lifetimeToken_;
  taskMarshaller_.RunOnGameThread(
      [this, callbackMutex = std::move(callbackMutex),
       lifetimeToken = std::move(lifetimeToken), correlationId] {
        std::lock_guard<std::mutex> lifetimeLock(*callbackMutex);
        if (!lifetimeToken->load()) {
          return;
        }
        try {
          //  No real baseline domain is registered yet; the game-thread path
          //  is proven as a mechanism and always reports success, matching
          //  IpcResynchronizeResultMessage's own documented scope.
          if (connection_ != nullptr) {
            connection_->TrySend(IpcMessage{IpcResynchronizeResultMessage{
                .correlationId = correlationId, .accepted = true}});
          }
        } catch (...) {
          //  Contained, per ai/context/skse/cpp-style.md's worker-thread
          //  boundary rule: this task runs on the Skyrim game thread via
          //  SKSE's own task interface, which must never see an exception
          //  escape.
        }
      });
}

void AdapterIpcSession::HandleListenEvent(
    const IpcListenEventMessage &listenEvent) {
  std::uint32_t eventKey = listenEvent.eventKey;
  auto callbackMutex = callbackMutex_;
  auto lifetimeToken = lifetimeToken_;
  taskMarshaller_.RunOnGameThread(
      [this, callbackMutex = std::move(callbackMutex),
       lifetimeToken = std::move(lifetimeToken), eventKey] {
        std::lock_guard<std::mutex> lifetimeLock(*callbackMutex);
        if (!lifetimeToken->load()) {
          return;
        }
        try {
          std::optional<std::vector<std::byte>> captured =
              dispatcher_.TryDispatch(eventKey);
          if (captured.has_value()) {
            captureQueue_.TryEnqueue(capture::AdapterCaptureWorkItem{
                .intentKey = eventKey, .capturedValue = *captured});
          }
        } catch (...) {
          //  Contained; see HandleResynchronizeRequest's task for why.
        }
      });
}

void AdapterIpcSession::HandleReadSample(
    const IpcReadSampleMessage &readSample) {
  std::uint32_t sampleToken = readSample.sampleToken;
  auto callbackMutex = callbackMutex_;
  auto lifetimeToken = lifetimeToken_;
  taskMarshaller_.RunOnGameThread(
      [this, callbackMutex = std::move(callbackMutex),
       lifetimeToken = std::move(lifetimeToken), sampleToken] {
        std::lock_guard<std::mutex> lifetimeLock(*callbackMutex);
        if (!lifetimeToken->load()) {
          return;
        }
        try {
          std::optional<std::vector<std::byte>> captured =
              dispatcher_.TryDispatch(sampleToken);
          if (captured.has_value()) {
            captureQueue_.TryEnqueue(capture::AdapterCaptureWorkItem{
                .intentKey = sampleToken, .capturedValue = *captured});
          }
        } catch (...) {
          //  Contained; see HandleResynchronizeRequest's task for why.
        }
      });
}

std::uint64_t AdapterIpcSession::NextCorrelationId() {
  return nextCorrelationId_.fetch_add(1) + 1;
}

} //  namespace dovahlink::adapter::ipc
