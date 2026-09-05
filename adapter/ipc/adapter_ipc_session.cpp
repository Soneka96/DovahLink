#include "ipc/adapter_ipc_session.hpp"

#include "ipc/adapter_ipc_connection.hpp"
#include "ipc/adapter_ipc_hmac.hpp"

#include <algorithm>
#include <type_traits>
#include <variant>

namespace dovahlink::adapter::ipc {

namespace {

///  Decrements a pending-game-thread-dispatch counter when destroyed,
///  regardless of how the owning scope exits, so every admitted dispatch
///  releases its slot exactly once.
class PendingDispatchGuard {
public:
  ///  @param count The counter this guard decrements on destruction.
  explicit PendingDispatchGuard(std::atomic<std::size_t> &count)
      : count_(count) {}
  ///  Decrements the guarded counter.
  ~PendingDispatchGuard() { count_.fetch_sub(1, std::memory_order_relaxed); }

  PendingDispatchGuard(const PendingDispatchGuard &) = delete;
  PendingDispatchGuard &operator=(const PendingDispatchGuard &) = delete;

private:
  ///  The counter decremented on destruction.
  std::atomic<std::size_t> &count_;
};

} //  namespace

AdapterIpcSession::AdapterIpcSession(
    identity::AdapterInstanceId instanceId,
    std::array<std::byte, kIpcOwnerLifetimeIdBytes> ownerLifetimeId,
    runtime::IAdapterTaskMarshaller &taskMarshaller,
    dispatch::IAdapterNativeDispatcher &dispatcher,
    capture::IAdapterCaptureHandoffQueue &captureQueue,
    IAdapterPairingNotificationSink &pairingNotificationSink,
    std::function<void()> onGameThreadDispatchRejected,
    std::chrono::milliseconds trustAdminRequestTimeout)
    : instanceId_(instanceId), ownerLifetimeId_(ownerLifetimeId),
      taskMarshaller_(taskMarshaller), dispatcher_(dispatcher),
      captureQueue_(captureQueue),
      pairingNotificationSink_(pairingNotificationSink),
      onGameThreadDispatchRejected_(std::move(onGameThreadDispatchRejected)),
      trustAdminRequestTimeout_(trustAdminRequestTimeout) {}

AdapterIpcSession::~AdapterIpcSession() {
  {
    std::lock_guard<std::mutex> lock(*callbackMutex_);
    lifetimeToken_->store(false);
  }

  //  Force-abandon every outstanding SendTrustAdminRequest call, the same as
  //  CloseCurrentGenerationLocked already does on disconnect, then wait for
  //  each one to actually observe that and return before this destructor
  //  itself returns: notify_all alone only schedules a waiting thread to wake
  //  up, it does not block until that thread has actually done so, so member
  //  destruction (trustAdminMutex_ and trustAdminCondition_ themselves) could
  //  otherwise begin while a woken thread is still using them.
  std::unique_lock<std::mutex> trustAdminLock(trustAdminMutex_);
  pendingTrustAdminResults_.clear();
  trustAdminCondition_.notify_all();
  trustAdminCondition_.wait(trustAdminLock,
                            [this] { return activeTrustAdminWaiters_ == 0; });
}

void AdapterIpcSession::AttachConnection(IAdapterIpcConnection &connection) {
  connection_ = &connection;
}

IpcMessage AdapterIpcSession::PrepareHello(const AdapterIpcTarget &target) {
  {
    std::lock_guard<std::mutex> lock(availableMutex_);
    activeTarget_ = target;
  }
  pendingHelloCorrelationId_ = NextCorrelationId();
  pendingHelloChallenge_ = GenerateIpcChallenge();
  return IpcMessage{IpcHelloMessage{
      .correlationId = pendingHelloCorrelationId_,
      .adapterInstanceId = instanceId_.value,
      .peerProofToken = target.proofToken,
      .challenge = pendingHelloChallenge_,
      .ownerLifetimeId = ownerLifetimeId_,
  }};
}

void AdapterIpcSession::HandleConnected(const AdapterIpcTarget &target) {
  {
    std::lock_guard<std::mutex> lock(availableMutex_);
    ++connectionGeneration_;
    authenticationState_ = AuthenticationState::kAwaitingHelloAck;
    activeTarget_ = target;
  }
  if (connection_ != nullptr) {
    connection_->TrySend(PrepareHello(target));
  }
}

std::optional<std::string> AdapterIpcSession::SendTrustAdminRequest(
    TrustAdminOperation operation, std::optional<TrustAdminListScope> listScope,
    std::optional<std::string> shortId,
    std::optional<std::string> confirmationCode) {
  bool authenticated;
  {
    std::lock_guard<std::mutex> lock(availableMutex_);
    authenticated = authenticationState_ == AuthenticationState::kAuthenticated;
  }
  if (!authenticated || connection_ == nullptr) {
    return std::nullopt;
  }

  std::uint64_t correlationId = NextCorrelationId();
  {
    std::lock_guard<std::mutex> lock(trustAdminMutex_);
    pendingTrustAdminResults_[correlationId] = std::nullopt;
  }

  bool sent = connection_->TrySend(IpcMessage{IpcTrustAdminRequestMessage{
      .correlationId = correlationId,
      .operation = operation,
      .listScope = listScope,
      .shortId = std::move(shortId),
      .confirmationCode = std::move(confirmationCode)}});
  if (!sent) {
    std::lock_guard<std::mutex> lock(trustAdminMutex_);
    pendingTrustAdminResults_.erase(correlationId);
    return std::nullopt;
  }

  std::unique_lock<std::mutex> lock(trustAdminMutex_);
  ++activeTrustAdminWaiters_;
  bool resolved = trustAdminCondition_.wait_for(
      lock, trustAdminRequestTimeout_, [this, correlationId] {
        auto it = pendingTrustAdminResults_.find(correlationId);
        return it == pendingTrustAdminResults_.end() || it->second.has_value();
      });
  //  Reported (and, if the destructor is waiting on it, observed) before
  //  this method's own remaining logic below, none of which the destructor's
  //  wait cares about -- only that no thread is still blocked in the
  //  condition-variable wait itself.
  --activeTrustAdminWaiters_;
  trustAdminCondition_.notify_all();

  if (!resolved) {
    //  Genuine timeout: nobody else will ever erase this entry.
    pendingTrustAdminResults_.erase(correlationId);
    return std::nullopt;
  }

  auto it = pendingTrustAdminResults_.find(correlationId);
  if (it == pendingTrustAdminResults_.end()) {
    //  The session closed while this request was outstanding;
    //  CloseCurrentGenerationLocked already erased every pending entry.
    return std::nullopt;
  }

  std::string result = std::move(*it->second);
  pendingTrustAdminResults_.erase(it);
  return result;
}

AdapterIpcMessageDisposition
AdapterIpcSession::HandleMessage(const IpcMessage &message) {
  return std::visit(
      [this](const auto &value) -> AdapterIpcMessageDisposition {
        using T = std::decay_t<decltype(value)>;

        AuthenticationState authenticationState;
        {
          std::lock_guard<std::mutex> lock(availableMutex_);
          authenticationState = authenticationState_;
        }

        if (authenticationState == AuthenticationState::kClosed) {
          return AdapterIpcMessageDisposition::kClose;
        }

        if (authenticationState == AuthenticationState::kAwaitingHelloAck &&
            !std::is_same_v<T, IpcHelloAckMessage>) {
          //  No host-directed request is legal until this transport has
          // completed mutual authentication. Close the generation without
          // invoking a game-thread or other message handler.
          return AdapterIpcMessageDisposition::kClose;
        }

        if (authenticationState == AuthenticationState::kAuthenticated &&
            std::is_same_v<T, IpcHelloAckMessage>) {
          //  Authentication is a one-time transition for a transport
          // generation. A second HelloAck is a protocol violation, not a new
          // opportunity to change availability.
          return AdapterIpcMessageDisposition::kClose;
        }

        if constexpr (std::is_same_v<T, IpcHelloAckMessage>) {
          //  accepted = true alone never proves the responder is the
          //  legitimate host -- it only proves the responder checked this
          //  adapter's presented proof. The full conjunction (accepted,
          //  matching correlation id, and a verifying hostProof recomputed
          //  from the exact challenge/instance id/lifetime id this adapter
          //  sent) is what authenticates the connection. Keyed by
          //  hostProofKey, independent of the proofToken this adapter itself
          //  presented in Hello, so observing that Hello alone can never let
          //  an untrusted observer forge this proof.
          std::vector<std::byte> hostProofKey;
          {
            std::lock_guard<std::mutex> lock(availableMutex_);
            if (!activeTarget_.has_value()) {
              return AdapterIpcMessageDisposition::kClose;
            }
            hostProofKey = activeTarget_->hostProofKey;
          }
          auto expectedProof = ComputeIpcHmacSha256(
              hostProofKey,
              BuildHostProofMessage(pendingHelloChallenge_,
                                    pendingHelloCorrelationId_,
                                    instanceId_.value, ownerLifetimeId_));
          bool authenticated =
              value.accepted &&
              value.correlationId == pendingHelloCorrelationId_ &&
              ConstantTimeEqual(value.hostProof, expectedProof);
          {
            std::lock_guard<std::mutex> lock(availableMutex_);
            authenticationState_ = authenticated
                                       ? AuthenticationState::kAuthenticated
                                       : AuthenticationState::kClosed;
          }
          return authenticated ? AdapterIpcMessageDisposition::kAuthenticated
                               : AdapterIpcMessageDisposition::kClose;
        } else if constexpr (std::is_same_v<T,
                                            IpcResynchronizeRequestMessage>) {
          HandleResynchronizeRequest(value);
          return AdapterIpcMessageDisposition::kContinue;
        } else if constexpr (std::is_same_v<T, IpcListenEventMessage>) {
          HandleListenEvent(value);
          return AdapterIpcMessageDisposition::kContinue;
        } else if constexpr (std::is_same_v<T, IpcReadSampleMessage>) {
          HandleReadSample(value);
          return AdapterIpcMessageDisposition::kContinue;
        } else if constexpr (std::is_same_v<T, IpcCloseMessage>) {
          return AdapterIpcMessageDisposition::kClose;
        } else if constexpr (std::is_same_v<T, IpcRejectMessage>) {
          return AdapterIpcMessageDisposition::kContinue;
        } else if constexpr (std::is_same_v<T, IpcCancelMessage>) {
          HandleCancel(value);
          return AdapterIpcMessageDisposition::kContinue;
        } else if constexpr (std::is_same_v<T, IpcPairingDisplayMessage>) {
          HandlePairingDisplay(value);
          return AdapterIpcMessageDisposition::kContinue;
        } else if constexpr (std::is_same_v<
                                 T, IpcPairingAttemptsExhaustedMessage>) {
          HandlePairingAttemptsExhausted(value);
          return AdapterIpcMessageDisposition::kContinue;
        } else if constexpr (std::is_same_v<T, IpcTrustAdminResultMessage>) {
          {
            std::lock_guard<std::mutex> lock(trustAdminMutex_);
            auto it = pendingTrustAdminResults_.find(value.correlationId);
            if (it != pendingTrustAdminResults_.end()) {
              it->second = value.resultText;
            }
            //  An entry that is missing here (never sent, already timed out,
            //  or already force-abandoned by a close) means no waiter cares
            //  about this result; it is simply discarded.
          }
          trustAdminCondition_.notify_all();
          return AdapterIpcMessageDisposition::kContinue;
        } else {
          //  IpcHelloMessage, IpcResynchronizeResultMessage,
          //  IpcPairingDisplayAckMessage, and IpcTrustAdminRequestMessage are
          //  adapter-outbound only; receiving any of them is a protocol
          //  violation from the host.
          if (connection_ != nullptr) {
            connection_->TrySend(IpcMessage{IpcRejectMessage{
                .correlationId = value.correlationId,
                .reason = IpcRejectReason::kUnknownMessageKind}});
          }
          return AdapterIpcMessageDisposition::kClose;
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
  CloseCurrentGenerationLocked();
}

bool AdapterIpcSession::IsHostAvailable() const {
  std::lock_guard<std::mutex> lock(availableMutex_);
  return authenticationState_ == AuthenticationState::kAuthenticated;
}

void AdapterIpcSession::HandleClosing() {
  std::lock_guard<std::mutex> lock(availableMutex_);
  CloseCurrentGenerationLocked();
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
  ScheduleGameThreadDispatch([this, callbackMutex = std::move(callbackMutex),
                              lifetimeToken = std::move(lifetimeToken),
                              correlationId, connectionGeneration] {
    std::lock_guard<std::mutex> lifetimeLock(*callbackMutex);
    if (!lifetimeToken->load()) {
      return;
    }
    try {
      {
        std::lock_guard<std::mutex> lock(availableMutex_);
        //  Re-checked at execution time, not just at enqueue time: logical
        //  closing (AdapterIpcConnectionCallbacks::onClosing) invalidates
        //  authentication for this generation immediately, before the
        //  physical disconnect that would otherwise be the only thing
        //  bumping connectionGeneration_ -- so the generation guard alone is
        //  not enough to reject a task queued just before that happened.
        if (connectionGeneration != connectionGeneration_ ||
            authenticationState_ != AuthenticationState::kAuthenticated ||
            ConsumeCancellationLocked(correlationId)) {
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
  std::uint64_t correlationId = listenEvent.correlationId;
  std::uint64_t connectionGeneration;
  bool authenticated;
  {
    std::lock_guard<std::mutex> lock(availableMutex_);
    connectionGeneration = connectionGeneration_;
    authenticated = authenticationState_ == AuthenticationState::kAuthenticated;
  }
  if (!authenticated) {
    //  The host-authentication result must be accepted before any
    //  host-directed intent reaches game-thread dispatch, per the
    //  mandatory Concept 03 handoff requirement.
    return;
  }
  auto callbackMutex = callbackMutex_;
  auto lifetimeToken = lifetimeToken_;
  ScheduleGameThreadDispatch([this, callbackMutex = std::move(callbackMutex),
                              lifetimeToken = std::move(lifetimeToken),
                              eventKey, correlationId, connectionGeneration] {
    std::lock_guard<std::mutex> lifetimeLock(*callbackMutex);
    if (!lifetimeToken->load()) {
      return;
    }
    try {
      {
        std::lock_guard<std::mutex> lock(availableMutex_);
        //  Re-checked at execution time, not just at enqueue time: a later
        //  authentication failure can close the same connection
        //  generation, so the generation guard alone must not authorize
        //  this deferred dispatch.
        if (connectionGeneration != connectionGeneration_ ||
            authenticationState_ != AuthenticationState::kAuthenticated ||
            ConsumeCancellationLocked(correlationId)) {
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
  std::uint64_t correlationId = readSample.correlationId;
  std::uint64_t connectionGeneration;
  bool authenticated;
  {
    std::lock_guard<std::mutex> lock(availableMutex_);
    connectionGeneration = connectionGeneration_;
    authenticated = authenticationState_ == AuthenticationState::kAuthenticated;
  }
  if (!authenticated) {
    //  The host-authentication result must be accepted before any
    //  host-directed intent reaches game-thread dispatch, per the
    //  mandatory Concept 03 handoff requirement.
    return;
  }
  auto callbackMutex = callbackMutex_;
  auto lifetimeToken = lifetimeToken_;
  ScheduleGameThreadDispatch([this, callbackMutex = std::move(callbackMutex),
                              lifetimeToken = std::move(lifetimeToken),
                              sampleToken, correlationId,
                              connectionGeneration] {
    std::lock_guard<std::mutex> lifetimeLock(*callbackMutex);
    if (!lifetimeToken->load()) {
      return;
    }
    try {
      {
        std::lock_guard<std::mutex> lock(availableMutex_);
        //  Re-checked at execution time, not just at enqueue time: a later
        //  authentication failure can close the same connection
        //  generation, so the generation guard alone must not authorize
        //  this deferred dispatch.
        if (connectionGeneration != connectionGeneration_ ||
            authenticationState_ != AuthenticationState::kAuthenticated ||
            ConsumeCancellationLocked(correlationId)) {
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

void AdapterIpcSession::HandlePairingDisplay(
    const IpcPairingDisplayMessage &pairingDisplay) {
  std::string code = pairingDisplay.code;
  PairingDisplayMode mode = pairingDisplay.mode;
  std::uint64_t correlationId = pairingDisplay.correlationId;
  std::uint64_t connectionGeneration;
  bool authenticated;
  {
    std::lock_guard<std::mutex> lock(availableMutex_);
    connectionGeneration = connectionGeneration_;
    authenticated = authenticationState_ == AuthenticationState::kAuthenticated;
  }
  if (!authenticated) {
    //  The host-authentication result must be accepted before any
    //  host-directed intent reaches game-thread dispatch, per the
    //  mandatory Concept 03 handoff requirement.
    return;
  }
  auto callbackMutex = callbackMutex_;
  auto lifetimeToken = lifetimeToken_;
  ScheduleGameThreadDispatch([this, callbackMutex = std::move(callbackMutex),
                              lifetimeToken = std::move(lifetimeToken),
                              code = std::move(code), mode, correlationId,
                              connectionGeneration] {
    std::lock_guard<std::mutex> lifetimeLock(*callbackMutex);
    if (!lifetimeToken->load()) {
      return;
    }
    try {
      {
        std::lock_guard<std::mutex> lock(availableMutex_);
        //  Re-checked at execution time, not just at enqueue time; see
        //  HandleListenEvent's identical guard for why.
        if (connectionGeneration != connectionGeneration_ ||
            authenticationState_ != AuthenticationState::kAuthenticated ||
            ConsumeCancellationLocked(correlationId)) {
          return;
        }
      }
      bool accepted = pairingNotificationSink_.Display(code, mode);
      if (connection_ != nullptr) {
        connection_->TrySend(IpcMessage{IpcPairingDisplayAckMessage{
            .correlationId = correlationId, .accepted = accepted}});
      }
    } catch (...) {
      //  Contained; see HandleResynchronizeRequest's task for why.
    }
  });
}

void AdapterIpcSession::HandlePairingAttemptsExhausted(
    const IpcPairingAttemptsExhaustedMessage &) {
  std::uint64_t connectionGeneration;
  bool authenticated;
  {
    std::lock_guard<std::mutex> lock(availableMutex_);
    connectionGeneration = connectionGeneration_;
    authenticated = authenticationState_ == AuthenticationState::kAuthenticated;
  }
  if (!authenticated) {
    //  The host-authentication result must be accepted before any
    //  host-directed intent reaches game-thread dispatch, per the
    //  mandatory Concept 03 handoff requirement.
    return;
  }
  auto callbackMutex = callbackMutex_;
  auto lifetimeToken = lifetimeToken_;
  ScheduleGameThreadDispatch([this, callbackMutex = std::move(callbackMutex),
                              lifetimeToken = std::move(lifetimeToken),
                              connectionGeneration] {
    std::lock_guard<std::mutex> lifetimeLock(*callbackMutex);
    if (!lifetimeToken->load()) {
      return;
    }
    try {
      {
        std::lock_guard<std::mutex> lock(availableMutex_);
        //  Re-checked at execution time, not just at enqueue time; see
        //  HandleListenEvent's identical guard for why.
        if (connectionGeneration != connectionGeneration_ ||
            authenticationState_ != AuthenticationState::kAuthenticated) {
          return;
        }
      }
      pairingNotificationSink_.NotifyAttemptsExhausted();
    } catch (...) {
      //  Contained; see HandleResynchronizeRequest's task for why.
    }
  });
}

void AdapterIpcSession::ScheduleGameThreadDispatch(std::function<void()> task) {
  if (pendingGameThreadDispatchCount_->fetch_add(
          1, std::memory_order_relaxed) >= kMaxPendingGameThreadDispatches) {
    pendingGameThreadDispatchCount_->fetch_sub(1, std::memory_order_relaxed);
    ReportGameThreadDispatchRejected();
    return;
  }
  try {
    //  Captured by value, not reached through `this`: this closure can still
    //  be queued and run after this session is destroyed, and nothing here
    //  may touch session memory before `task` itself passes its own lifetime
    //  gate (`callbackMutex_`/`lifetimeToken_`, captured the same way).
    auto pendingCount = pendingGameThreadDispatchCount_;
    taskMarshaller_.RunOnGameThread(
        [pendingCount = std::move(pendingCount), task = std::move(task)] {
          PendingDispatchGuard dispatchGuard(*pendingCount);
          task();
        });
  } catch (...) {
    //  RunOnGameThread failed (or threw while constructing its own closure)
    //  before the task it was given ever ran, so PendingDispatchGuard's
    //  destructor never fires for this admission: release the slot here
    //  instead, or every future scheduling failure would leak one
    //  permanently until the bound rejects all further dispatch.
    pendingGameThreadDispatchCount_->fetch_sub(1, std::memory_order_relaxed);
    ReportGameThreadDispatchRejected();
  }
}

void AdapterIpcSession::ReportGameThreadDispatchRejected() {
  try {
    onGameThreadDispatchRejected_();
  } catch (...) {
    //  A diagnostics callback must never escape into the IPC worker thread.
  }
}

void AdapterIpcSession::HandleCancel(const IpcCancelMessage &cancel) {
  std::lock_guard<std::mutex> lock(availableMutex_);
  if (cancelledCorrelationIds_.size() >= kMaxPendingIpcCancellations) {
    cancelledCorrelationIds_.pop_front();
  }
  cancelledCorrelationIds_.push_back(cancel.correlationId);
}

bool AdapterIpcSession::ConsumeCancellationLocked(std::uint64_t correlationId) {
  auto it = std::find(cancelledCorrelationIds_.begin(),
                      cancelledCorrelationIds_.end(), correlationId);
  if (it == cancelledCorrelationIds_.end()) {
    return false;
  }
  cancelledCorrelationIds_.erase(it);
  return true;
}

std::uint64_t AdapterIpcSession::NextCorrelationId() {
  return nextCorrelationId_.fetch_add(1) + 1;
}

void AdapterIpcSession::CloseCurrentGenerationLocked() {
  if (authenticationState_ == AuthenticationState::kClosed) {
    return;
  }
  authenticationState_ = AuthenticationState::kClosed;
  activeTarget_.reset();
  ++connectionGeneration_;
  //  A cancellation only ever applies to a deferred task from the generation
  //  that received it; every such task already self-rejects once
  //  connectionGeneration_ no longer matches, so a tombstone surviving past
  //  this point could only misfire against an unrelated request that reuses
  //  the same correlation id on a later generation.
  cancelledCorrelationIds_.clear();
  //  Force-abandon every outstanding SendTrustAdminRequest call rather than
  //  leaving it to wait out its full kTrustAdminRequestTimeout after the
  //  connection it was sent on has already ended: erasing an entry (instead
  //  of merely notifying) is itself the "abandoned" signal each waiter's own
  //  predicate checks for.
  {
    std::lock_guard<std::mutex> trustAdminLock(trustAdminMutex_);
    pendingTrustAdminResults_.clear();
  }
  trustAdminCondition_.notify_all();
}

} //  namespace dovahlink::adapter::ipc
