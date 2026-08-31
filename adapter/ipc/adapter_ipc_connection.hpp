#pragma once

#include "ipc/adapter_ipc_connection_callbacks.hpp"
#include "ipc/adapter_ipc_target.hpp"
#include "ipc/ipc_constants.hpp"
#include "ipc/ipc_frame_codec.hpp"

#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <deque>
#include <functional>
#include <mutex>
#include <optional>
#include <span>
#include <thread>

namespace dovahlink::adapter::ipc {

class IAdapterIpcSocket;

///  The adapter-side private IPC transport: runs one coordinator-requested
///  connect/serve attempt, drains a bounded outbound queue, and reports its
///  terminal outcome without blocking the Skyrim game thread. Owns no protocol
///  decisions of its own; every lifecycle event reaches its owner through the
///  injected `AdapterIpcConnectionCallbacks`.
class IAdapterIpcConnection {
public:
  virtual ~IAdapterIpcConnection() = default;

  ///  Replaces the target used by the next connection attempt. The complete
  ///  value is copied as one snapshot and never read piecemeal by callers.
  virtual void ConfigureTarget(AdapterIpcTarget target) = 0;

  ///  Starts one background connect/serve attempt. Callers must finish wiring
  ///  every callback consumer before calling this. A call while an attempt is
  ///  active is a no-op; a later call may start a new attempt after the prior
  ///  one has finished.
  virtual void Start() = 0;

  ///  Attempts to enqueue a message for the current transport generation's
  ///  next write. Pending messages may be discarded when that generation ends.
  ///  Never blocks.
  ///  @return `true` when the message was accepted onto the bounded outbound
  ///  queue; `false` at capacity or once `Stop()` has been called.
  virtual bool TrySend(const IpcMessage &message) = 0;

  ///  Requests the socket close, wakes the connection's background thread,
  ///  and waits for it to return. Idempotent.
  virtual void Stop() = 0;
};

///  @copydoc IAdapterIpcConnection
class AdapterIpcConnection final : public IAdapterIpcConnection {
public:
  ///  Supplies the monotonic time used by the per-connection inbound rate
  ///  window. Tests inject a deterministic clock; production uses steady time.
  using ClockNow = std::function<std::chrono::steady_clock::time_point()>;

  ///  Creates a connection over an injected socket and codec. Does not start
  ///  the background thread; call `Start()` once callback wiring is
  ///  complete.
  ///  @param socket The transport this connection connects and reads/writes
  ///  through, for its own lifetime.
  ///  @param codec Encodes outbound messages and decodes inbound frames.
  ///  @param callbacks The lifecycle event hooks this connection invokes.
  ///  @param establishmentTimeout The absolute bound on waiting for the
  ///  connection owner to report authenticated HelloAck completion.
  AdapterIpcConnection(
      IAdapterIpcSocket &socket, const IIpcFrameCodec &codec,
      AdapterIpcConnectionCallbacks callbacks,
      std::chrono::milliseconds establishmentTimeout =
          kAdapterIpcEstablishmentTimeout,
      ClockNow clockNow = [] { return std::chrono::steady_clock::now(); });

  ///  Calls `Stop()` as a fallback so the background thread is never leaked.
  ~AdapterIpcConnection() override;

  AdapterIpcConnection(const AdapterIpcConnection &) = delete;
  AdapterIpcConnection &operator=(const AdapterIpcConnection &) = delete;

  ///  @copydoc IAdapterIpcConnection::Start
  void Start() override;

  ///  @copydoc IAdapterIpcConnection::ConfigureTarget
  void ConfigureTarget(AdapterIpcTarget target) override;

  ///  @copydoc IAdapterIpcConnection::TrySend
  bool TrySend(const IpcMessage &message) override;

  ///  @copydoc IAdapterIpcConnection::Stop
  void Stop() override;

private:
  ///  Contains one worker-thread entry and records its terminal completion.
  void RunLoop();

  ///  Runs one connect/serve attempt on the background thread.
  void RunAttempt();

  ///  Serves one connected session until it ends: reads and dispatches
  ///  inbound frames, requires authentication within the absolute
  ///  establishment deadline, and drains queued outbound messages between
  ///  reads.
  ///  @param establishmentDeadline The absolute bound on completing
  ///  authentication.
  ///  @param authenticated The caller's own authentication flag, set to
  ///  `true` the instant authentication succeeds rather than only on return,
  ///  so it stays correct even if this function later exits via an
  ///  exception.
  void
  ServeConnection(std::chrono::steady_clock::time_point establishmentDeadline,
                  bool &authenticated);

  ///  Dispatches one decoded message to the owner while containing callback
  ///  exceptions at the worker boundary.
  AdapterIpcMessageDisposition
  DispatchInboundMessage(const IpcMessage &message);

  ///  Reads and decodes exactly one frame.
  ///  @param decodeFailed Set to `true` if a frame was read but could not be
  ///  decoded; otherwise left `false`.
  ///  @param deadline The optional absolute deadline for completing the whole
  ///  frame, including its prefix and payload.
  ///  @return The decoded message, or `std::nullopt` if the connection ended,
  ///  a stop was requested, the deadline elapsed, or the frame failed to
  ///  decode.
  std::optional<IpcMessage>
  ReadOneMessage(bool &decodeFailed,
                 std::optional<std::chrono::steady_clock::time_point> deadline);

  ///  Fills `buffer` completely, polling `socket_` in short bounded steps
  ///  and draining the outbound queue between each poll so a long wait for
  ///  inbound data never starves a pending outbound write.
  ///  @param deadline The optional absolute deadline for completing the
  ///  buffer.
  ///  @return `false` if the connection ended, a stop was requested, or the
  ///  deadline elapsed before `buffer` was completely filled.
  bool ReadFully(std::span<std::byte> buffer,
                 std::optional<std::chrono::steady_clock::time_point> deadline);

  ///  Writes every message currently queued, oldest first.
  ///  @return `false` if a write failed and the connection should end.
  bool DrainOutbound();

  ///  Discards outbound work that belongs to a transport generation that has
  ///  ended or has not yet successfully started.
  void ClearOutbound();

  ///  Accepts one inbound frame if the rolling per-connection rate window has
  ///  not reached its shared private-IPC limit.
  bool TryAcceptInboundMessage();

  ///  Marks the one-shot worker finished so a later `Start()` can join and
  ///  replace it.
  void MarkWorkerFinished();

  ///  The transport this connection reads and writes through.
  IAdapterIpcSocket &socket_;
  ///  Encodes outbound messages and decodes inbound frames.
  const IIpcFrameCodec &codec_;
  ///  The lifecycle event hooks this connection invokes.
  AdapterIpcConnectionCallbacks callbacks_;
  ///  The absolute bound for the pre-authentication establishment phase.
  std::chrono::milliseconds establishmentTimeout_;
  ///  The monotonic clock used by the inbound rate limiter.
  ClockNow clockNow_;
  ///  Guards access to the worker thread object and coordinates joins.
  std::mutex lifecycleMutex_;
  ///  Wakes concurrent shutdown callers after the worker join completes.
  std::condition_variable lifecycleCondition_;
  ///  Whether another caller currently owns the worker join operation.
  bool joinInProgress_ = false;
  ///  Whether the current worker has finished and may be joined by `Start()`.
  bool workerFinished_ = false;
  ///  Guards `stopping_`.
  std::mutex stopMutex_;
  ///  Set by `Stop()`; checked before and during the current attempt.
  bool stopping_ = false;
  ///  Guards `outbound_`.
  std::mutex outboundMutex_;
  ///  The bounded FIFO of messages queued for the next write.
  std::deque<IpcMessage> outbound_;
  ///  Timestamps of inbound frames still inside the current attempt's rolling
  ///  rate window.
  std::deque<std::chrono::steady_clock::time_point> inboundMessageTimes_;
  ///  Guards the currently configured target snapshot.
  std::mutex targetMutex_;
  ///  The target snapshot used by the next connection attempt, if configured.
  std::optional<AdapterIpcTarget> target_;
  ///  The background connect/serve thread, started by `Start()`.
  std::thread worker_;
  ///  The target generation of the attempt currently in progress, captured
  ///  before any operation that copies the target snapshot's proof token can
  ///  throw. `RunLoop`'s outer failure boundary reports this generation when
  ///  an exception escapes before `RunAttempt` can otherwise determine it.
  ///  Stays `0` when no target was ever configured.
  std::uint64_t inProgressAttemptTargetGeneration_ = 0;
  ///  The id of the currently running worker thread, or the default
  ///  `std::thread::id{}` when none is running. Guards every self-thread
  ///  comparison in `Start()`/`Stop()` so neither ever calls `worker_.get_id()`
  ///  while another caller's `joinInProgress_` may be joining that same
  ///  `std::thread` object unsynchronized. Written only under
  ///  `lifecycleMutex_`: `Start()` sets it immediately after constructing
  ///  `worker_`, and `Stop()` resets it to the default once the join it owns
  ///  has fully completed.
  std::thread::id workerThreadId_;
};

} //  namespace dovahlink::adapter::ipc
