#include "ipc/adapter_ipc_session.hpp"

#include "ipc/adapter_ipc_connection.hpp"
#include "ipc/adapter_ipc_hmac.hpp"

#include <algorithm>
#include <type_traits>
#include <variant>
#include <vector>

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

///  Decrements a trust-admin request's `activeTrustAdminWaiters_` slot and
///  notifies its condition variable when destroyed, regardless of how the
///  owning scope exits -- including an exception escaping the timeout
///  worker's own wait -- so the request's slot is always released exactly
///  once and the destructor's own wait for it is never left hanging.
class TrustAdminWaiterGuard {
public:
  ///  @param mutex Guards `waiters` and pairs with `condition`.
  ///  @param condition Notified after `waiters` is decremented.
  ///  @param waiters The counter this guard decrements on destruction.
  TrustAdminWaiterGuard(std::mutex &mutex, std::condition_variable &condition,
                        std::size_t &waiters)
      : mutex_(mutex), condition_(condition), waiters_(waiters) {}
  ///  Decrements the guarded counter and notifies the guarded condition
  ///  variable.
  ~TrustAdminWaiterGuard() {
    std::lock_guard<std::mutex> lock(mutex_);
    --waiters_;
    condition_.notify_all();
  }

  TrustAdminWaiterGuard(const TrustAdminWaiterGuard &) = delete;
  TrustAdminWaiterGuard &operator=(const TrustAdminWaiterGuard &) = delete;

private:
  ///  Guards `waiters_` and pairs with `condition_`.
  std::mutex &mutex_;
  ///  Notified after `waiters_` is decremented.
  std::condition_variable &condition_;
  ///  The counter decremented on destruction.
  std::size_t &waiters_;
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

  //  Force-abandon every outstanding trust-admin request the same way
  //  CloseCurrentGenerationLocked does, then wait for activeTrustAdminWaiters_
  //  to actually reach zero -- not merely be notified -- before this
  //  destructor returns: a request resolved here still has its own timeout
  //  worker (or, for an immediately-failed send, the original calling thread)
  //  yet to finish touching trustAdminMutex_-guarded state, so member
  //  destruction (trustAdminMutex_ and trustAdminCondition_ themselves) could
  //  otherwise begin while that thread is still using them.
  std::vector<std::uint64_t> outstandingCorrelationIds;
  {
    std::lock_guard<std::mutex> trustAdminLock(trustAdminMutex_);
    outstandingCorrelationIds.reserve(pendingTrustAdminResults_.size());
    for (const auto &[correlationId, onResult] : pendingTrustAdminResults_) {
      outstandingCorrelationIds.push_back(correlationId);
    }
  }
  for (std::uint64_t correlationId : outstandingCorrelationIds) {
    ResolveTrustAdminRequest(correlationId, std::nullopt);
  }

  std::unique_lock<std::mutex> trustAdminLock(trustAdminMutex_);
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

void AdapterIpcSession::SendTrustAdminRequest(
    TrustAdminOperation operation, std::optional<TrustAdminListScope> listScope,
    std::optional<std::string> shortId,
    std::optional<std::string> confirmationCode,
    std::function<void(std::optional<std::string>)> onResult) {
  //  Held for the entire admission critical section below (authentication
  //  check, pending-entry registration, and TrySend), not just the
  //  authentication check: CloseCurrentGenerationLocked also runs under this
  //  same lock, so whichever of the two acquires it first linearizes before
  //  the other, and no request can observe generation N authenticated only
  //  to be registered or sent after HandleClosing has already closed N.
  //  TrySend is a bounded, non-blocking try-enqueue (try_to_lock on its own
  //  outbound queue, no callback invoked), so calling it while this lock is
  //  held cannot block the calling thread or invert lock order.
  std::unique_lock<std::mutex> availableLock(availableMutex_);
  if (authenticationState_ != AuthenticationState::kAuthenticated ||
      connection_ == nullptr) {
    availableLock.unlock();
    try {
      onResult(std::nullopt);
    } catch (...) {
      //  Contained: onResult may run directly on a Papyrus-invoking thread
      //  here, per ai/context/skse/cpp-style.md's callback boundary rule.
    }
    return;
  }

  std::uint64_t correlationId = NextCorrelationId();
  //  Admission against kMaxPendingTrustAdminRequests and registration happen
  //  in the same trustAdminMutex_ critical section so no concurrent
  //  SendTrustAdminRequest call (serialized by availableLock, still held
  //  here) can observe stale capacity between the check and the insert.
  //  onResult is invoked only after this block ends, never while either
  //  mutex is held. Checked against activeTrustAdminWaiters_, not
  //  pendingTrustAdminResults_.size(): a request resolved by HandleMessage or
  //  a timeout still occupies its slot until its own timeout worker (or, for
  //  an immediately-failed send, the original caller) actually finishes
  //  touching trustAdminMutex_-guarded state, so counting the map alone would
  //  let a rapid sequence of fast host responses admit more concurrently
  //  outstanding timeout workers than this bound allows.
  bool atCapacity;
  {
    std::lock_guard<std::mutex> lock(trustAdminMutex_);
    atCapacity = activeTrustAdminWaiters_ >= kMaxPendingTrustAdminRequests;
    if (!atCapacity) {
      pendingTrustAdminResults_[correlationId] = std::move(onResult);
      //  Counted here, before TrySend is ever called: this closes the gap a
      //  concurrent destructor could otherwise exploit by observing zero
      //  in-flight requests while this call was still between sending and
      //  registering the timeout worker it hands off to below.
      ++activeTrustAdminWaiters_;
    }
  }
  if (atCapacity) {
    availableLock.unlock();
    try {
      onResult(std::nullopt);
    } catch (...) {
      //  Contained: see the unauthenticated-connection case above.
    }
    return;
  }

  bool sent = connection_->TrySend(IpcMessage{IpcTrustAdminRequestMessage{
      .correlationId = correlationId,
      .operation = operation,
      .listScope = listScope,
      .shortId = std::move(shortId),
      .confirmationCode = std::move(confirmationCode)}});
  availableLock.unlock();
  if (!sent) {
    TrustAdminWaiterGuard waiterGuard(trustAdminMutex_, trustAdminCondition_,
                                      activeTrustAdminWaiters_);
    ResolveTrustAdminRequest(correlationId, std::nullopt);
    return;
  }

  //  Bounded, non-blocking timeout: this worker, not the calling thread,
  //  owns waiting out trustAdminRequestTimeout_. It wakes early once
  //  HandleMessage or a close resolves this correlation id (both notify
  //  trustAdminCondition_), or at the deadline otherwise, and is itself the
  //  one that times the request out if nothing else resolved it first.
  //  Counted by activeTrustAdminWaiters_ above, so the destructor always
  //  waits for it to finish before returning.
  try {
    std::thread([this, correlationId] {
      //  Releases this worker's activeTrustAdminWaiters_ slot on every exit
      //  path, including the catch block immediately below: constructed
      //  first so it is guaranteed to run even if wait_for itself throws.
      TrustAdminWaiterGuard waiterGuard(trustAdminMutex_, trustAdminCondition_,
                                        activeTrustAdminWaiters_);
      try {
        {
          std::unique_lock<std::mutex> lock(trustAdminMutex_);
          trustAdminCondition_.wait_for(
              lock, trustAdminRequestTimeout_, [this, correlationId] {
                return pendingTrustAdminResults_.find(correlationId) ==
                       pendingTrustAdminResults_.end();
              });
        }
        //  Already resolved by HandleMessage or a close: a safe no-op.
        //  Still pending past the deadline: this worker is the one that
        //  times it out.
        ResolveTrustAdminRequest(correlationId, std::nullopt);
      } catch (...) {
        //  Contained: this runs on a detached worker thread, which must
        //  never let an exception escape, per
        //  ai/context/skse/cpp-style.md's worker-thread boundary rule -- an
        //  uncaught exception here would call std::terminate rather than
        //  merely fail this one request.
      }
    }).detach();
  } catch (...) {
    //  The timeout worker never started (for example std::thread failed to
    //  acquire OS resources): this call is still the sole owner of
    //  activeTrustAdminWaiters_'s increment above and must release it
    //  itself, the same as the send-failure path, or the destructor would
    //  wait for a worker that will never exist.
    TrustAdminWaiterGuard waiterGuard(trustAdminMutex_, trustAdminCondition_,
                                      activeTrustAdminWaiters_);
    ResolveTrustAdminRequest(correlationId, std::nullopt);
  }
}

void AdapterIpcSession::ResolveTrustAdminRequest(
    std::uint64_t correlationId, std::optional<std::string> result) {
  std::function<void(std::optional<std::string>)> onResult;
  {
    std::lock_guard<std::mutex> lock(trustAdminMutex_);
    auto it = pendingTrustAdminResults_.find(correlationId);
    if (it == pendingTrustAdminResults_.end()) {
      return;
    }
    onResult = std::move(it->second);
    pendingTrustAdminResults_.erase(it);
  }
  trustAdminCondition_.notify_all();
  try {
    onResult(std::move(result));
  } catch (...) {
    //  Contained: onResult may run on this request's timeout worker, the
    //  connection's inbound-message thread, or a Papyrus-invoking thread,
    //  none of which may ever see an exception escape, per
    //  ai/context/skse/cpp-style.md's callback/worker-thread boundary rule.
  }
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
          //  ResolveTrustAdminRequest is a no-op if this correlation id was
          //  never sent, already timed out, or was already force-abandoned
          //  by a close: no pending callback cares about this result, so it
          //  is simply discarded.
          ResolveTrustAdminRequest(value.correlationId, value.resultText);
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
  std::vector<std::function<void(std::optional<std::string>)>>
      abandonedCallbacks;
  {
    std::lock_guard<std::mutex> lock(availableMutex_);
    abandonedCallbacks = CloseCurrentGenerationLocked();
  }
  InvokeAbandonedTrustAdminCallbacks(std::move(abandonedCallbacks));
}

bool AdapterIpcSession::IsHostAvailable() const {
  std::lock_guard<std::mutex> lock(availableMutex_);
  return authenticationState_ == AuthenticationState::kAuthenticated;
}

void AdapterIpcSession::HandleClosing() {
  std::vector<std::function<void(std::optional<std::string>)>>
      abandonedCallbacks;
  {
    std::lock_guard<std::mutex> lock(availableMutex_);
    abandonedCallbacks = CloseCurrentGenerationLocked();
  }
  InvokeAbandonedTrustAdminCallbacks(std::move(abandonedCallbacks));
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

std::vector<std::function<void(std::optional<std::string>)>>
AdapterIpcSession::CloseCurrentGenerationLocked() {
  if (authenticationState_ == AuthenticationState::kClosed) {
    return {};
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
  //  Force-abandon every outstanding trust-admin request rather than leaving
  //  it to wait out its full kTrustAdminRequestTimeout after the connection
  //  it was sent on has already ended. Each request's own timeout worker (or
  //  send-failure caller) still owns releasing activeTrustAdminWaiters_ for
  //  it; detaching it here only wakes that worker early instead of leaving it
  //  to sleep out the rest of its bound. The caller invokes the returned
  //  callbacks only after releasing availableMutex_.
  return DetachPendingTrustAdminCallbacksLocked();
}

std::vector<std::function<void(std::optional<std::string>)>>
AdapterIpcSession::DetachPendingTrustAdminCallbacksLocked() {
  std::vector<std::function<void(std::optional<std::string>)>> callbacks;
  {
    std::lock_guard<std::mutex> trustAdminLock(trustAdminMutex_);
    callbacks.reserve(pendingTrustAdminResults_.size());
    for (auto &[correlationId, onResult] : pendingTrustAdminResults_) {
      callbacks.push_back(std::move(onResult));
    }
    pendingTrustAdminResults_.clear();
  }
  trustAdminCondition_.notify_all();
  return callbacks;
}

void AdapterIpcSession::InvokeAbandonedTrustAdminCallbacks(
    std::vector<std::function<void(std::optional<std::string>)>> callbacks) {
  for (auto &onResult : callbacks) {
    try {
      onResult(std::nullopt);
    } catch (...) {
      //  Contained: see ResolveTrustAdminRequest's identical rule for why.
    }
  }
}

} //  namespace dovahlink::adapter::ipc
