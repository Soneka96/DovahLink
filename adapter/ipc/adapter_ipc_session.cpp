#include "ipc/adapter_ipc_session.hpp"

#include "ipc/adapter_ipc_connection.hpp"
#include "ipc/adapter_ipc_hmac.hpp"

#include <type_traits>
#include <variant>

namespace dovahlink::adapter::ipc {

AdapterIpcSession::AdapterIpcSession(
    identity::AdapterInstanceId instanceId,
    std::array<std::byte, kIpcOwnerLifetimeIdBytes> ownerLifetimeId,
    IAdapterIpcPeerProofProvider &peerProofProvider,
    runtime::IAdapterTaskMarshaller &taskMarshaller,
    dispatch::IAdapterNativeDispatcher &dispatcher,
    capture::IAdapterCaptureHandoffQueue &captureQueue)
    : instanceId_(instanceId), ownerLifetimeId_(ownerLifetimeId),
      peerProofProvider_(peerProofProvider), taskMarshaller_(taskMarshaller),
      dispatcher_(dispatcher), captureQueue_(captureQueue) {}

AdapterIpcSession::~AdapterIpcSession() {
  std::lock_guard<std::mutex> lock(*callbackMutex_);
  lifetimeToken_->store(false);
}

void AdapterIpcSession::AttachConnection(IAdapterIpcConnection &connection) {
  connection_ = &connection;
}

IpcMessage AdapterIpcSession::PrepareHello() {
  pendingHelloCorrelationId_ = NextCorrelationId();
  pendingHelloChallenge_ = GenerateIpcChallenge();
  return IpcMessage{IpcHelloMessage{
      .correlationId = pendingHelloCorrelationId_,
      .adapterInstanceId = instanceId_.value,
      .peerProofToken = peerProofProvider_.Token(),
      .challenge = pendingHelloChallenge_,
      .ownerLifetimeId = ownerLifetimeId_,
  }};
}

void AdapterIpcSession::HandleConnected() {
  {
    std::lock_guard<std::mutex> lock(availableMutex_);
    ++connectionGeneration_;
  }
  if (connection_ != nullptr) {
    connection_->TrySend(PrepareHello());
  }
}

bool AdapterIpcSession::HandleMessage(const IpcMessage &message) {
  return std::visit(
      [this](const auto &value) -> bool {
        using T = std::decay_t<decltype(value)>;
        if constexpr (std::is_same_v<T, IpcHelloAckMessage>) {
          //  accepted = true alone never proves the responder is the
          //  legitimate host -- it only proves the responder checked this
          //  adapter's presented proof. The full conjunction (accepted,
          //  matching correlation id, and a verifying hostProof recomputed
          //  from the exact challenge/instance id/lifetime id this adapter
          //  sent) is what authenticates the connection.
          auto expectedProof = ComputeIpcHmacSha256(
              peerProofProvider_.Token(),
              BuildHostProofMessage(pendingHelloChallenge_,
                                    pendingHelloCorrelationId_,
                                    instanceId_.value, ownerLifetimeId_));
          bool authenticated =
              value.accepted &&
              value.correlationId == pendingHelloCorrelationId_ &&
              ConstantTimeEqual(value.hostProof, expectedProof);
          std::lock_guard<std::mutex> lock(availableMutex_);
          available_ = authenticated;
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
  ++connectionGeneration_;
}

bool AdapterIpcSession::IsHostAvailable() const {
  std::lock_guard<std::mutex> lock(availableMutex_);
  return available_;
}

void AdapterIpcSession::HandleResynchronizeRequest(
    const IpcResynchronizeRequestMessage &request) {
  std::uint64_t correlationId = request.correlationId;
  std::uint64_t connectionGeneration;
  {
    std::lock_guard<std::mutex> lock(availableMutex_);
    connectionGeneration = connectionGeneration_;
  }
  auto callbackMutex = callbackMutex_;
  auto lifetimeToken = lifetimeToken_;
  taskMarshaller_.RunOnGameThread([this,
                                   callbackMutex = std::move(callbackMutex),
                                   lifetimeToken = std::move(lifetimeToken),
                                   correlationId, connectionGeneration] {
    std::lock_guard<std::mutex> lifetimeLock(*callbackMutex);
    if (!lifetimeToken->load()) {
      return;
    }
    try {
      {
        std::lock_guard<std::mutex> lock(availableMutex_);
        if (connectionGeneration != connectionGeneration_) {
          return;
        }
      }
      //  No approved baseline domain is registered yet. The game-thread
      //  path is still exercised, but reporting failure prevents the host
      //  from treating an empty capture as a fresh authoritative baseline.
      if (connection_ != nullptr) {
        connection_->TrySend(IpcMessage{IpcResynchronizeResultMessage{
            .correlationId = correlationId, .accepted = false}});
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
  std::uint64_t connectionGeneration;
  bool available;
  {
    std::lock_guard<std::mutex> lock(availableMutex_);
    connectionGeneration = connectionGeneration_;
    available = available_;
  }
  if (!available) {
    //  The host-authentication result must be accepted before any
    //  host-directed intent reaches game-thread dispatch, per the
    //  mandatory Concept 03 handoff requirement.
    return;
  }
  auto callbackMutex = callbackMutex_;
  auto lifetimeToken = lifetimeToken_;
  taskMarshaller_.RunOnGameThread([this,
                                   callbackMutex = std::move(callbackMutex),
                                   lifetimeToken = std::move(lifetimeToken),
                                   eventKey, connectionGeneration] {
    std::lock_guard<std::mutex> lifetimeLock(*callbackMutex);
    if (!lifetimeToken->load()) {
      return;
    }
    try {
      {
        std::lock_guard<std::mutex> lock(availableMutex_);
        //  Re-checked at execution time, not just at enqueue time: a second
        //  HelloAck rejecting the peer can flip `available_` to false on the
        //  same connection generation (no disconnect, so the generation
        //  guard alone would not catch it), and that must still block this
        //  deferred dispatch.
        if (connectionGeneration != connectionGeneration_ || !available_) {
          return;
        }
      }
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
  std::uint64_t connectionGeneration;
  bool available;
  {
    std::lock_guard<std::mutex> lock(availableMutex_);
    connectionGeneration = connectionGeneration_;
    available = available_;
  }
  if (!available) {
    //  The host-authentication result must be accepted before any
    //  host-directed intent reaches game-thread dispatch, per the
    //  mandatory Concept 03 handoff requirement.
    return;
  }
  auto callbackMutex = callbackMutex_;
  auto lifetimeToken = lifetimeToken_;
  taskMarshaller_.RunOnGameThread([this,
                                   callbackMutex = std::move(callbackMutex),
                                   lifetimeToken = std::move(lifetimeToken),
                                   sampleToken, connectionGeneration] {
    std::lock_guard<std::mutex> lifetimeLock(*callbackMutex);
    if (!lifetimeToken->load()) {
      return;
    }
    try {
      {
        std::lock_guard<std::mutex> lock(availableMutex_);
        //  Re-checked at execution time, not just at enqueue time: a second
        //  HelloAck rejecting the peer can flip `available_` to false on the
        //  same connection generation (no disconnect, so the generation
        //  guard alone would not catch it), and that must still block this
        //  deferred dispatch.
        if (connectionGeneration != connectionGeneration_ || !available_) {
          return;
        }
      }
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
