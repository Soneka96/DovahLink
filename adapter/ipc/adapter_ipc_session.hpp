#pragma once

#include "capture/adapter_capture_handoff_queue.hpp"
#include "dispatch/adapter_native_dispatcher.hpp"
#include "identity/adapter_instance_id.hpp"
#include "ipc/adapter_ipc_connection_callbacks.hpp"
#include "ipc/adapter_ipc_target.hpp"
#include "ipc/adapter_pairing_notification_sink.hpp"
#include "ipc/ipc_constants.hpp"
#include "ipc/ipc_enums.hpp"
#include "ipc/ipc_message.hpp"
#include "ipc/trust_admin_request_result.hpp"
#include "runtime/adapter_task_marshaller.hpp"

#include <array>
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <functional>
#include <map>
#include <memory>
#include <mutex>
#include <optional>
#include <string>
#include <thread>

namespace dovahlink::adapter::ipc {

class IAdapterIpcConnection;

///  The adapter-side private IPC protocol decisions: builds Hello, tracks
///  handshake acceptance, and routes every host-directed request through the
///  same generic pipe -- marshal onto the game thread, translate via
///  `IAdapterNativeDispatcher`, hand the owned result to
///  `IAdapterCaptureHandoffQueue`. No per-message-kind service exists; a
///  resynchronization request is just another marshaled game-thread task that
///  reports unavailable until an approved baseline domain exists (this
///  phase's non-goal against speculative domain registries -- the fresh
///  baseline data itself is a later concept's contract, per
///  `IpcResynchronizeResultMessage`'s own documentation). Owns no transport
///  I/O of its own; every lifecycle event reaches this session through
///  `AdapterIpcConnection`'s callbacks.
class IAdapterIpcSession {
public:
  virtual ~IAdapterIpcSession() = default;

  ///  Assigns the connection this session sends messages through. A narrow
  ///  lifecycle-inversion exception for the genuine construction cycle
  ///  between session and connection, per `ai/context/common.md`'s
  ///  "Behavioral boundaries and test isolation": the composition root must
  ///  call this exactly once, before starting the connection.
  virtual void AttachConnection(IAdapterIpcConnection &connection) = 0;

  ///  Builds this session's Hello message.
  [[nodiscard]] virtual IpcMessage
  PrepareHello(const AdapterIpcTarget &target) = 0;

  ///  Handles a successful connect: sends Hello through the attached
  ///  connection.
  virtual void HandleConnected(const AdapterIpcTarget &target) = 0;

  ///  Sends one Papyrus-originated trust-administration command to the host
  ///  and invokes `onResult` with a `TrustAdminRequestResult` describing
  ///  exactly one of three outcomes: the host's correlated result arrived
  ///  (`kCompleted`); the request was never successfully handed to the host,
  ///  because no authenticated connection was available, the
  ///  outstanding-request bound was already reached, or the send itself
  ///  failed (`kUnavailable`); or the request was submitted (or its
  ///  submission could not be ruled out -- for example this session closed
  ///  or the connection ended while it was still outstanding) but no
  ///  correlated result arrived within `kTrustAdminRequestTimeout`
  ///  (`kTimedOut`). `kTimedOut` deliberately cannot promise the host's own
  ///  mutation, if any, did not happen: a request that already crossed the
  ///  host's durable commit point stays committed regardless of this call's
  ///  own bound. A request that reaches `kTimedOut` by actually waiting out
  ///  the bound (as opposed to this session closing or the connection ending
  ///  first) also best-effort sends the host a cancellation for its exact
  ///  correlation id, so a host that has not yet started the mutation can
  ///  still stop before it does; a failed or refused send never changes the
  ///  already-decided `kTimedOut` outcome. Never blocks its calling thread for
  ///  any bounded or
  ///  unbounded duration -- including the thread SKSE invokes a registered
  ///  Papyrus native function on -- so it is safe to call directly from a
  ///  latent Papyrus function's initial callback. `onResult` always runs on
  ///  the game thread, exactly once, regardless of which thread resolves the
  ///  request (the calling thread for an immediate outcome, this session's
  ///  own timeout worker, or the connection's inbound-message thread) --
  ///  `DispatchTrustAdminCompletion` marshals delivery there independently of
  ///  this session's own lifetime, so a request resolved as this session is
  ///  being destroyed still resumes its latent Papyrus script afterward. It
  ///  must not block or throw. Admission is
  ///  atomic with `HandleClosing`'s own generation close: a call either
  ///  observes the current generation already closed and sends nothing, or
  ///  is admitted and sent while that generation is still open, in which
  ///  case a `HandleClosing` that closes it afterward still force-abandons
  ///  the now-pending request. There is no outcome where this call observes
  ///  an authenticated generation and is then admitted or sent after that
  ///  same generation has already closed. Outstanding requests are bounded by
  ///  `kMaxPendingTrustAdminRequests`, counted from admission until the
  ///  request's own timeout worker (or, for an immediately-failed send, the
  ///  original caller) has actually finished handling it -- not merely until
  ///  a result, timeout, or close resolves it -- so a request beyond that
  ///  capacity is rejected immediately, the same as an unauthenticated
  ///  connection: nothing is sent and no timeout worker is created for it,
  ///  without disturbing any already-admitted request's own correlation or
  ///  result.
  ///  @param operation Which trust-administration command to send.
  ///  @param listScope The device scope for `TrustAdminOperation::kList`;
  ///  otherwise unset.
  ///  @param shortId The five-digit device identity for `kRevoke`, `kBlock`,
  ///  `kUnblock`, or `kForget`; otherwise unset.
  ///  @param confirmationCode The six-digit Factory Reset confirmation code
  ///  for `kConfirmReset`; otherwise unset.
  ///  @param onResult Invoked exactly once with this call's outcome; see
  ///  above.
  virtual void SendTrustAdminRequest(
      TrustAdminOperation operation,
      std::optional<TrustAdminListScope> listScope,
      std::optional<std::string> shortId,
      std::optional<std::string> confirmationCode,
      std::function<void(TrustAdminRequestResult)> onResult) = 0;

  ///  Handles one successfully decoded inbound message.
  ///  @return The disposition for the current transport generation. A valid
  ///  HelloAck returns `kAuthenticated`; every non-HelloAck received before
  ///  authentication, a rejected or invalid HelloAck, a duplicate HelloAck,
  ///  received close, or unexpected message kind returns `kClose`.
  virtual AdapterIpcMessageDisposition
  HandleMessage(const IpcMessage &message) = 0;

  ///  Handles an inbound frame that could not be decoded: sends a
  ///  best-effort `IpcCloseMessage` (reason `kError`) through the attached
  ///  connection before the connection ends.
  virtual void HandleDecodeFailure() = 0;

  ///  Handles the connection ending, for any reason.
  virtual void HandleDisconnected() = 0;

  ///  Whether the host is currently available: the handshake has been
  ///  accepted and the connection has not since ended.
  [[nodiscard]] virtual bool IsHostAvailable() const = 0;

  ///  Handles serving having irreversibly ended for the current generation,
  ///  reached strictly before `HandleDisconnected` (see
  ///  `AdapterIpcConnectionCallbacks::onClosing`). Invalidates this
  ///  generation's authentication and eligibility for deferred game-thread
  ///  work immediately, rather than waiting for the later physical
  ///  disconnect: a `ListenEvent`, `ReadSample`, or `ResynchronizeRequest`
  ///  already marshaled onto the game thread before this call must reject
  ///  itself once it runs. Idempotent with `HandleDisconnected` for the same
  ///  generation; whichever of the two is called first performs the
  ///  invalidation, and the other is then a no-op.
  virtual void HandleClosing() = 0;
};

///  @copydoc IAdapterIpcSession
class AdapterIpcSession final : public IAdapterIpcSession {
public:
  ///  Creates a session for the adapter's single, process-lifetime private
  ///  IPC connection.
  ///  @param instanceId This adapter process's own instance identity.
  ///  @param ownerLifetimeId The owning Skyrim process's lifetime identity,
  ///  scoping this handshake to the intended Skyrim lifetime. Not itself a
  ///  cryptographic ownership proof.
  ///  @param taskMarshaller Marshals capture work onto the Skyrim game
  ///  thread.
  ///  @param dispatcher Performs the one generic key-to-Skyrim translation.
  ///  @param captureQueue Receives owned captured values for handoff.
  ///  @param pairingNotificationSink Presents host-decided pairing-display
  ///  and attempts-exhausted notifications at the Skyrim-facing display seam.
  ///  @param onGameThreadDispatchRejected Invoked when a resynchronization,
  ///  listen-event, or read-sample request is rejected at the
  ///  `kMaxPendingGameThreadDispatches` bound instead of being marshaled onto
  ///  the game thread. Diagnostics only; must not throw, and may run on the
  ///  connection's own thread.
  ///  @param trustAdminRequestTimeout The absolute bound
  ///  `SendTrustAdminRequest` waits for its correlated result. Overridable
  ///  only so a test can exercise the timeout path without a real multi-second
  ///  wait; production composition always uses the default.
  AdapterIpcSession(
      identity::AdapterInstanceId instanceId,
      std::array<std::byte, kIpcOwnerLifetimeIdBytes> ownerLifetimeId,
      runtime::IAdapterTaskMarshaller &taskMarshaller,
      dispatch::IAdapterNativeDispatcher &dispatcher,
      capture::IAdapterCaptureHandoffQueue &captureQueue,
      IAdapterPairingNotificationSink &pairingNotificationSink,
      std::function<void()> onGameThreadDispatchRejected = [] {},
      std::chrono::milliseconds trustAdminRequestTimeout =
          kTrustAdminRequestTimeout);

  ///  Invalidates deferred game-thread tasks and waits for any task already
  ///  inside the session lifetime gate before the session is destroyed.
  ~AdapterIpcSession() override;

  ///  @copydoc IAdapterIpcSession::AttachConnection
  void AttachConnection(IAdapterIpcConnection &connection) override;

  ///  @copydoc IAdapterIpcSession::PrepareHello
  [[nodiscard]] IpcMessage
  PrepareHello(const AdapterIpcTarget &target) override;

  ///  @copydoc IAdapterIpcSession::HandleConnected
  void HandleConnected(const AdapterIpcTarget &target) override;

  ///  @copydoc IAdapterIpcSession::SendTrustAdminRequest
  void SendTrustAdminRequest(
      TrustAdminOperation operation,
      std::optional<TrustAdminListScope> listScope,
      std::optional<std::string> shortId,
      std::optional<std::string> confirmationCode,
      std::function<void(TrustAdminRequestResult)> onResult) override;

  ///  @copydoc IAdapterIpcSession::HandleMessage
  AdapterIpcMessageDisposition
  HandleMessage(const IpcMessage &message) override;

  ///  @copydoc IAdapterIpcSession::HandleDecodeFailure
  void HandleDecodeFailure() override;

  ///  @copydoc IAdapterIpcSession::HandleDisconnected
  void HandleDisconnected() override;

  ///  @copydoc IAdapterIpcSession::IsHostAvailable
  [[nodiscard]] bool IsHostAvailable() const override;

  ///  @copydoc IAdapterIpcSession::HandleClosing
  void HandleClosing() override;

private:
  ///  The lifecycle phase that controls which inbound messages are legal.
  enum class AuthenticationState {
    ///  A transport is waiting for its matching HelloAck.
    kAwaitingHelloAck,
    ///  The transport completed mutual authentication and may serve requests.
    kAuthenticated,
    ///  No transport is active, or the current transport must close and cannot
    ///  accept further messages.
    kClosed,
  };

  ///  Marshals the resynchronization decision onto the game thread and replies
  ///  that no baseline is available until an approved domain is registered.
  void
  HandleResynchronizeRequest(const IpcResynchronizeRequestMessage &request);

  ///  Marshals the dispatcher's translation for `listenEvent.eventKey` onto
  ///  the game thread and hands any captured value to the capture queue.
  void HandleListenEvent(const IpcListenEventMessage &listenEvent);

  ///  Marshals the dispatcher's translation for `readSample.sampleToken`
  ///  onto the game thread and hands any captured value to the capture
  ///  queue.
  void HandleReadSample(const IpcReadSampleMessage &readSample);

  ///  Marshals a pairing-display request onto the game thread, presents it
  ///  through `pairingNotificationSink_`, and replies with an
  ///  `IpcPairingDisplayAckMessage` carrying the sink's accepted value.
  void HandlePairingDisplay(const IpcPairingDisplayMessage &pairingDisplay);

  ///  Marshals a no-code attempts-exhausted notification onto the game
  ///  thread and presents it through `pairingNotificationSink_`. Best
  ///  effort; sends no reply.
  void HandlePairingAttemptsExhausted(
      const IpcPairingAttemptsExhaustedMessage &pairingAttemptsExhausted);

  ///  Admits `task` against `kMaxPendingGameThreadDispatches` and marshals it
  ///  onto the game thread, wrapped so its slot in
  ///  `pendingGameThreadDispatchCount_` is always released -- whether `task`
  ///  runs, or `RunOnGameThread` itself fails to admit it. Reports a rejected
  ///  admission (bound reached, or a failed `RunOnGameThread` call) through
  ///  `onGameThreadDispatchRejected_`.
  ///  @param cancellableCorrelationId When present, registers this id in
  ///  `gameThreadDispatchCancellation_` for the duration this dispatch stays
  ///  admitted -- from this call succeeding until `task` actually runs (via
  ///  `ConsumeCancellationLocked`) -- or removes the registration immediately
  ///  if admission itself is rejected, so a registration only ever exists for
  ///  a dispatch this bound has actually admitted. `task` itself is
  ///  responsible for calling
  ///  `ConsumeCancellationLocked(*cancellableCorrelationId)` on every path
  ///  through its own body, including one that returns early for an unrelated
  ///  reason, so the registration is always released exactly once `task` runs.
  ///  @return Whether `task` was admitted (queued via `RunOnGameThread`);
  ///  `false` if the bound was already reached or `RunOnGameThread` itself
  ///  failed to accept it.
  bool ScheduleGameThreadDispatch(
      std::function<void()> task,
      std::optional<std::uint64_t> cancellableCorrelationId = std::nullopt);

  ///  Invokes `onGameThreadDispatchRejected_`, containing any exception it
  ///  throws so diagnostics can never escape into the IPC worker thread.
  void ReportGameThreadDispatchRejected();

  ///  Marks `cancel.correlationId` as cancelled if a currently-admitted
  ///  deferred dispatch is registered under it in
  ///  `gameThreadDispatchCancellation_`; otherwise a no-op. An unknown, stale,
  ///  or duplicate cancellation therefore never displaces cancellation state
  ///  for a genuinely pending dispatch: nothing is evicted, since this method
  ///  never inserts, only marks an existing registration.
  void HandleCancel(const IpcCancelMessage &cancel);

  ///  Returns whether `correlationId` was cancelled, consuming (erasing) the
  ///  registration if one exists; a missing registration (never admitted,
  ///  already consumed, or from a since-closed generation) returns `false`
  ///  without effect. Must be called while holding `availableMutex_`, and
  ///  exactly once per admitted dispatch that registered a
  ///  `cancellableCorrelationId`, on every path through that dispatch's own
  ///  task body -- see `ScheduleGameThreadDispatch`.
  bool ConsumeCancellationLocked(std::uint64_t correlationId);

  ///  Issues the next monotonic outbound correlation id, starting at 1.
  std::uint64_t NextCorrelationId();

  ///  Invalidates the current generation for deferred work exactly once: a
  ///  no-op (returning an empty vector) if `authenticationState_` is already
  ///  `kClosed`, so the generation counter advances only once per logical
  ///  close no matter how many times this is reached for the same generation.
  ///  Must be called while holding `availableMutex_`. Also detaches every
  ///  outstanding `SendTrustAdminRequest` call's callback via
  ///  `DetachPendingTrustAdminCallbacksLocked`, so none of them wait out
  ///  their full timeout after the connection they were sent on has already
  ///  ended -- but, unlike `ResolveTrustAdminRequest`, never invokes one
  ///  itself: the returned callbacks must be invoked only once every
  ///  lifecycle lock this call was reached under is released, so external
  ///  callback code never runs while this session's own lifecycle mutex is
  ///  held.
  ///  @return Every abandoned request's callback, to invoke with
  ///  `TrustAdminRequestOutcome::kTimedOut` once those locks are released:
  ///  an abandoned request was already registered (and, in every case but a
  ///  vanishingly narrow admission race, already sent), so its submission to
  ///  the host cannot be ruled out.
  [[nodiscard]] std::vector<std::function<void(TrustAdminRequestResult)>>
  CloseCurrentGenerationLocked();

  ///  Erases every entry in `pendingTrustAdminResults_` and notifies
  ///  `trustAdminCondition_` so each request's own timeout worker can wake
  ///  and observe its slot already resolved, but returns the erased
  ///  callbacks rather than invoking them. Must be called while holding
  ///  `availableMutex_`; safe to call with `pendingTrustAdminResults_` empty.
  ///  @return Every detached callback, in no particular order.
  [[nodiscard]] std::vector<std::function<void(TrustAdminRequestResult)>>
  DetachPendingTrustAdminCallbacksLocked();

  ///  Delivers every callback in `callbacks`
  ///  `TrustAdminRequestOutcome::kTimedOut` (see `CloseCurrentGenerationLocked`
  ///  for why) through `DispatchTrustAdminCompletion`, so each one still
  ///  resumes its latent Papyrus script on the game thread exactly once,
  ///  regardless of which thread this connection-lifecycle callback runs on.
  ///  Must be called with neither `availableMutex_` nor `trustAdminMutex_`
  ///  held.
  void InvokeAbandonedTrustAdminCallbacks(
      std::vector<std::function<void(TrustAdminRequestResult)>> callbacks);

  ///  Resolves one trust-administration request: if `correlationId` still
  ///  has a pending entry, erases it and delivers `result` to its callback
  ///  through `DispatchTrustAdminCompletion`; otherwise a no-op (the request
  ///  was already resolved by another path). Idempotent by construction,
  ///  since exactly one caller ever observes the entry present, and safe to
  ///  call from any thread without holding `availableMutex_` -- the request
  ///  may be resolved by whichever thread reaches this call first, but its
  ///  callback itself always runs on the game thread, exactly once, per
  ///  `DispatchTrustAdminCompletion`'s own contract. A force-abandonment
  ///  sweep instead detaches its callbacks via
  ///  `DetachPendingTrustAdminCallbacksLocked` and delivers them through
  ///  `InvokeAbandonedTrustAdminCallbacks`, so that sweep's own callback
  ///  delivery never runs while `availableMutex_` is held.
  void ResolveTrustAdminRequest(std::uint64_t correlationId,
                                TrustAdminRequestResult result);

  ///  This adapter process's own instance identity.
  identity::AdapterInstanceId instanceId_;
  ///  The owning Skyrim process's lifetime identity. Not itself a
  ///  cryptographic ownership proof.
  std::array<std::byte, kIpcOwnerLifetimeIdBytes> ownerLifetimeId_;
  ///  Marshals capture work onto the Skyrim game thread.
  runtime::IAdapterTaskMarshaller &taskMarshaller_;
  ///  Performs the one generic key-to-Skyrim translation.
  dispatch::IAdapterNativeDispatcher &dispatcher_;
  ///  Receives owned captured values for handoff.
  capture::IAdapterCaptureHandoffQueue &captureQueue_;
  ///  Presents host-decided pairing-display and attempts-exhausted
  ///  notifications at the Skyrim-facing display seam.
  IAdapterPairingNotificationSink &pairingNotificationSink_;
  ///  Invoked when a deferred game-thread dispatch is rejected at the
  ///  `kMaxPendingGameThreadDispatches` bound.
  std::function<void()> onGameThreadDispatchRejected_;
  ///  The connection this session sends messages through, set once by
  ///  `AttachConnection`. Non-owning: the composition root owns both this
  ///  session and the connection it attaches, for the same plugin lifetime.
  IAdapterIpcConnection *connection_ = nullptr;
  ///  The most recently issued outbound correlation id.
  std::atomic<std::uint64_t> nextCorrelationId_{0};
  ///  Identifies the currently connected transport generation.
  std::uint64_t connectionGeneration_ = 0;
  ///  The complete target snapshot authenticated by the current Hello.
  std::optional<AdapterIpcTarget> activeTarget_;
  ///  The correlation id of the most recently prepared Hello, verified
  ///  against a received HelloAck's own correlation id. Touched only from
  ///  the connection's single callback-invoking thread (`PrepareHello` is
  ///  called from `HandleConnected`, and compared against in `HandleMessage`,
  ///  both reached only through that thread in production), so it needs no
  ///  additional synchronization beyond `availableMutex_`'s existing coverage
  ///  of the authentication state.
  std::uint64_t pendingHelloCorrelationId_ = 0;
  ///  The fresh random challenge sent with the most recently prepared Hello,
  ///  bound into the expected `hostProof` recomputation. Same single-thread
  ///  access pattern as `pendingHelloCorrelationId_`.
  std::array<std::byte, kIpcChallengeBytes> pendingHelloChallenge_{};
  ///  Serializes deferred task execution with session destruction.
  std::shared_ptr<std::mutex> callbackMutex_ = std::make_shared<std::mutex>();
  ///  Lets deferred tasks reject themselves after session destruction begins.
  std::shared_ptr<std::atomic_bool> lifetimeToken_ =
      std::make_shared<std::atomic_bool>(true);
  ///  Guards `authenticationState_`. Also held for the full duration of a
  ///  `SendTrustAdminRequest` call's admission (authentication check,
  ///  `pendingTrustAdminResults_` registration, and `TrySend`), so that
  ///  admission and `CloseCurrentGenerationLocked`'s own generation close can
  ///  never interleave: whichever of the two acquires this mutex first
  ///  linearizes before the other. Acquired before `trustAdminMutex_`
  ///  whenever both are held together, the same order
  ///  `CloseCurrentGenerationLocked` already uses.
  mutable std::mutex availableMutex_;
  ///  The current transport's authentication lifecycle phase.
  AuthenticationState authenticationState_ = AuthenticationState::kClosed;
  ///  The number of deferred game-thread dispatches currently admitted but
  ///  not yet run, bounded by `kMaxPendingGameThreadDispatches`. Incremented
  ///  when a request is admitted and decremented when its marshaled task
  ///  finishes, regardless of outcome. Independently reference-counted, the
  ///  same technique `callbackMutex_` and `lifetimeToken_` already use:
  ///  `ScheduleGameThreadDispatch`'s task-marshaling closure must never
  ///  dereference `this` before the task it wraps passes the lifetime gate,
  ///  since that closure can still be queued and run after this session is
  ///  destroyed.
  std::shared_ptr<std::atomic<std::size_t>> pendingGameThreadDispatchCount_ =
      std::make_shared<std::atomic<std::size_t>>(0);
  ///  Cancellation state for currently-admitted, not-yet-run deferred
  ///  dispatches (resynchronization, listen-event, read-sample, and
  ///  pairing-display requests), keyed by their own correlation id, value
  ///  `true` once an `IpcCancelMessage` has marked that id cancelled.
  ///  `ScheduleGameThreadDispatch` inserts an entry (`false`) exactly when it
  ///  admits a dispatch with a `cancellableCorrelationId` and removes it
  ///  immediately if admission is then rejected; `ConsumeCancellationLocked`
  ///  removes it exactly once, when the admitted dispatch's own task runs.
  ///  This ties the map's size to `kMaxPendingGameThreadDispatches` -- the
  ///  same bound that already limits how many such dispatches can be
  ///  admitted at once -- rather than an independent bound: an unknown,
  ///  stale, or duplicate `IpcCancelMessage` can never insert an entry, so it
  ///  can never displace cancellation state for a dispatch that is actually
  ///  still pending. Scoped to the current connection generation:
  ///  `CloseCurrentGenerationLocked` clears every entry defensively, since a
  ///  cancellation from one generation must never apply to a correlation id
  ///  reused by a later one -- correlation ids are otherwise monotonic for
  ///  this session's lifetime (see `NextCorrelationId`), so this guards only
  ///  the theoretical wraparound case, not ordinary reuse. Guarded by
  ///  `availableMutex_`.
  std::map<std::uint64_t, bool> gameThreadDispatchCancellation_;
  ///  Guards `pendingTrustAdminResults_` and `activeTrustAdminWaiters_`, and
  ///  pairs with `trustAdminCondition_`. Deliberately separate from
  ///  `availableMutex_`: a request's timeout worker sleeps on this mutex
  ///  alone for up to `kTrustAdminRequestTimeout`, and must never hold
  ///  `availableMutex_` while doing so, since that would block every other
  ///  message this session processes for the same duration.
  ///  `SendTrustAdminRequest` briefly nests this mutex inside an already-held
  ///  `availableMutex_` at admission time only -- registering the pending
  ///  entry, never waiting on `trustAdminCondition_` -- so this bounded nesting
  ///  does not reintroduce that same blocking risk.
  std::mutex trustAdminMutex_;
  ///  Wakes a request's timeout worker early once its correlated result
  ///  arrives or the session closes, and wakes the destructor once
  ///  `activeTrustAdminWaiters_` reaches zero. Always notified while holding
  ///  `trustAdminMutex_`.
  std::condition_variable trustAdminCondition_;
  ///  One entry per trust-admin request not yet resolved, keyed by its
  ///  correlation id, holding the callback `ResolveTrustAdminRequest` invokes
  ///  once a result, timeout, or abandonment resolves it. An entry's absence
  ///  means the request was never sent, or has already been resolved by
  ///  `HandleMessage`, its own timeout worker, or
  ///  `CloseCurrentGenerationLocked`. Guarded by `trustAdminMutex_`.
  std::map<std::uint64_t, std::function<void(TrustAdminRequestResult)>>
      pendingTrustAdminResults_;
  ///  The number of trust-admin requests whose registering
  ///  `SendTrustAdminRequest` call has not yet finished touching
  ///  `trustAdminMutex_`-guarded state: incremented when a request is
  ///  registered, before it is ever sent, and decremented either by that same
  ///  call (a send that failed, resolved synchronously) or by the timeout
  ///  worker it hands off to (a send that succeeded). Registering before
  ///  sending, rather than only once a wait begins, closes the gap where a
  ///  concurrent destructor could otherwise observe zero in-flight requests
  ///  while a call was still between sending and registering its own timeout
  ///  worker. The destructor waits for this to reach zero before returning, so
  ///  member destruction (in particular `trustAdminMutex_` and
  ///  `trustAdminCondition_` themselves) can never run while another thread
  ///  still holds or is waiting on them -- the same class of hazard
  ///  `callbackMutex_`/`lifetimeToken_` closes for deferred game-thread tasks,
  ///  but for a request's timeout worker instead of fire-and-forget dispatch.
  ///  Guarded by `trustAdminMutex_`.
  std::size_t activeTrustAdminWaiters_ = 0;
  ///  The absolute bound a trust-admin request's timeout worker waits for
  ///  its correlated result before resolving it with
  ///  `TrustAdminRequestOutcome::kTimedOut`.
  std::chrono::milliseconds trustAdminRequestTimeout_;
};

} //  namespace dovahlink::adapter::ipc
