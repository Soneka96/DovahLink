#pragma once

#include "ipc/adapter_ipc_connection_callbacks.hpp"
#include "ipc/adapter_ipc_target.hpp"
#include "ipc/ipc_constants.hpp"
#include "ipc/ipc_frame_codec.hpp"

#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <deque>
#include <mutex>
#include <optional>
#include <span>
#include <thread>

namespace dovahlink::adapter::ipc {

class IAdapterIpcSocket;

///  The adapter-side private IPC transport: connects to the host's loopback
///  listener, serves inbound frames, drains a bounded outbound queue, and
///  retries with a bounded fixed delay on disconnect -- never blocking the
///  Skyrim game thread, per `ai/context/host/architecture.md`'s "adapter
///  reconnect is bounded and performed outside game-thread work". Owns no
///  protocol decisions of its own; every lifecycle event reaches its owner
///  through the injected `AdapterIpcConnectionCallbacks`.
class IAdapterIpcConnection {
public:
  virtual ~IAdapterIpcConnection() = default;

  ///  Replaces the target used by the next connection attempt. The complete
  ///  value is copied as one snapshot and never read piecemeal by callers.
  virtual void ConfigureTarget(AdapterIpcTarget target) = 0;

  ///  Starts the connection's background connect/serve thread. Callers must
  ///  finish wiring every callback consumer (for example attaching this
  ///  connection to the session that will handle its callbacks) before
  ///  calling this, since callbacks may begin firing immediately afterward.
  ///  Idempotent: a call after the thread is already running is a no-op.
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
  ///  Creates a connection over an injected socket and codec. Does not start
  ///  the background thread; call `Start()` once callback wiring is
  ///  complete.
  ///  @param socket The transport this connection connects and reads/writes
  ///  through, for its own lifetime.
  ///  @param codec Encodes outbound messages and decodes inbound frames.
  ///  @param callbacks The lifecycle event hooks this connection invokes.
  ///  @param establishmentTimeout The absolute bound on waiting for the
  ///  connection owner to report authenticated HelloAck completion.
  AdapterIpcConnection(IAdapterIpcSocket &socket, const IIpcFrameCodec &codec,
                       AdapterIpcConnectionCallbacks callbacks,
                       std::chrono::milliseconds establishmentTimeout =
                           kAdapterIpcEstablishmentTimeout);

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
  ///  Runs on the background thread: connects, serves the connection, and
  ///  retries with a bounded delay until stopped.
  void RunLoop();

  ///  Serves one connected session until it ends: reads and dispatches
  ///  inbound frames, requires authentication within the absolute
  ///  establishment deadline, and drains queued outbound messages between
  ///  reads.
  void
  ServeConnection(std::chrono::steady_clock::time_point establishmentDeadline);

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

  ///  Waits out `kAdapterIpcReconnectDelay`, or returns immediately once
  ///  `Stop()` is called.
  void WaitBoundedBackoff();

  ///  The transport this connection reads and writes through.
  IAdapterIpcSocket &socket_;
  ///  Encodes outbound messages and decodes inbound frames.
  const IIpcFrameCodec &codec_;
  ///  The lifecycle event hooks this connection invokes.
  AdapterIpcConnectionCallbacks callbacks_;
  ///  The absolute bound for the pre-authentication establishment phase.
  std::chrono::milliseconds establishmentTimeout_;
  ///  Guards access to the worker thread object and coordinates joins.
  std::mutex lifecycleMutex_;
  ///  Wakes concurrent shutdown callers after the worker join completes.
  std::condition_variable lifecycleCondition_;
  ///  Whether another caller currently owns the worker join operation.
  bool joinInProgress_ = false;
  ///  Guards `stopping_` and backs `stopCondition_`.
  std::mutex stopMutex_;
  ///  Signaled when `Stop()` is called, to interrupt a reconnect wait.
  std::condition_variable stopCondition_;
  ///  Set by `Stop()`; checked between reconnect attempts and read cycles.
  bool stopping_ = false;
  ///  Guards `outbound_`.
  std::mutex outboundMutex_;
  ///  The bounded FIFO of messages queued for the next write.
  std::deque<IpcMessage> outbound_;
  ///  Guards the currently configured target snapshot.
  std::mutex targetMutex_;
  ///  The target snapshot used by the next connection attempt, if configured.
  std::optional<AdapterIpcTarget> target_;
  ///  The background connect/serve thread, started by `Start()`.
  std::thread worker_;
};

} //  namespace dovahlink::adapter::ipc
