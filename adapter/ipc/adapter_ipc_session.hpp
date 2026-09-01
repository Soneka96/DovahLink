#pragma once

#include "capture/adapter_capture_handoff_queue.hpp"
#include "dispatch/adapter_native_dispatcher.hpp"
#include "identity/adapter_instance_id.hpp"
#include "ipc/adapter_ipc_connection_callbacks.hpp"
#include "ipc/adapter_ipc_target.hpp"
#include "ipc/ipc_constants.hpp"
#include "ipc/ipc_message.hpp"
#include "runtime/adapter_task_marshaller.hpp"

#include <array>
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <deque>
#include <functional>
#include <memory>
#include <mutex>
#include <optional>

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
  ///  @param onGameThreadDispatchRejected Invoked when a resynchronization,
  ///  listen-event, or read-sample request is rejected at the
  ///  `kMaxPendingGameThreadDispatches` bound instead of being marshaled onto
  ///  the game thread. Diagnostics only; must not throw, and may run on the
  ///  connection's own thread.
  AdapterIpcSession(
      identity::AdapterInstanceId instanceId,
      std::array<std::byte, kIpcOwnerLifetimeIdBytes> ownerLifetimeId,
      runtime::IAdapterTaskMarshaller &taskMarshaller,
      dispatch::IAdapterNativeDispatcher &dispatcher,
      capture::IAdapterCaptureHandoffQueue &captureQueue,
      std::function<void()> onGameThreadDispatchRejected = [] {});

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

  ///  Admits `task` against `kMaxPendingGameThreadDispatches` and marshals it
  ///  onto the game thread, wrapped so its slot in
  ///  `pendingGameThreadDispatchCount_` is always released -- whether `task`
  ///  runs, or `RunOnGameThread` itself fails to admit it. Reports a rejected
  ///  admission (bound reached, or a failed `RunOnGameThread` call) through
  ///  `onGameThreadDispatchRejected_`.
  void ScheduleGameThreadDispatch(std::function<void()> task);

  ///  Invokes `onGameThreadDispatchRejected_`, containing any exception it
  ///  throws so diagnostics can never escape into the IPC worker thread.
  void ReportGameThreadDispatchRejected();

  ///  Records `cancel.correlationId` as cancelled, evicting the oldest
  ///  recorded cancellation first if already at `kMaxPendingIpcCancellations`.
  void HandleCancel(const IpcCancelMessage &cancel);

  ///  Returns whether `correlationId` was cancelled, consuming (erasing) the
  ///  entry if so. Must be called while holding `availableMutex_`.
  bool ConsumeCancellationLocked(std::uint64_t correlationId);

  ///  Issues the next monotonic outbound correlation id, starting at 1.
  std::uint64_t NextCorrelationId();

  ///  Invalidates the current generation for deferred work exactly once: a
  ///  no-op if `authenticationState_` is already `kClosed`. Called by both
  ///  `HandleClosing` and `HandleDisconnected` so the generation counter
  ///  advances only once per logical close, regardless of which one runs
  ///  first. Must be called while holding `availableMutex_`.
  void CloseCurrentGenerationLocked();

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
  ///  Guards `authenticationState_`.
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
  ///  Correlation ids of received `IpcCancelMessage`s not yet consumed by a
  ///  matching deferred task, oldest first, bounded by
  ///  `kMaxPendingIpcCancellations`. Scoped to the current connection
  ///  generation: `CloseCurrentGenerationLocked` clears every entry, since a
  ///  cancellation from one generation must never apply to a correlation id
  ///  reused by a later one. Guarded by `availableMutex_`.
  std::deque<std::uint64_t> cancelledCorrelationIds_;
};

} //  namespace dovahlink::adapter::ipc
