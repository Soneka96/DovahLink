#pragma once

#include "ipc/adapter_ipc_connection_callbacks.hpp"
#include "ipc/ipc_frame_codec.hpp"

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

  ///  Attempts to enqueue a message for the next write. Never blocks.
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
  ///  Creates a connection over an injected socket and codec, and starts its
  ///  background connect/serve thread immediately.
  ///  @param socket The transport this connection connects and reads/writes
  ///  through, for its own lifetime.
  ///  @param codec Encodes outbound messages and decodes inbound frames.
  ///  @param callbacks The lifecycle event hooks this connection invokes.
  AdapterIpcConnection(IAdapterIpcSocket &socket, const IIpcFrameCodec &codec,
                       AdapterIpcConnectionCallbacks callbacks);

  ///  Calls `Stop()` as a fallback so the background thread is never leaked.
  ~AdapterIpcConnection() override;

  AdapterIpcConnection(const AdapterIpcConnection &) = delete;
  AdapterIpcConnection &operator=(const AdapterIpcConnection &) = delete;

  ///  @copydoc IAdapterIpcConnection::TrySend
  bool TrySend(const IpcMessage &message) override;

  ///  @copydoc IAdapterIpcConnection::Stop
  void Stop() override;

private:
  ///  Runs on the background thread: connects, serves the connection, and
  ///  retries with a bounded delay until stopped.
  void RunLoop();

  ///  Serves one connected session until it ends: reads and dispatches
  ///  inbound frames, and drains queued outbound messages between reads.
  void ServeConnection();

  ///  Reads and decodes exactly one frame.
  ///  @param decodeFailed Set to `true` if a frame was read but could not be
  ///  decoded; otherwise left `false`.
  ///  @return The decoded message, or `std::nullopt` if the connection ended,
  ///  a stop was requested while waiting, or the frame failed to decode.
  std::optional<IpcMessage> ReadOneMessage(bool &decodeFailed);

  ///  Fills `buffer` completely, polling `socket_` in short bounded steps
  ///  and draining the outbound queue between each poll so a long wait for
  ///  inbound data never starves a pending outbound write.
  ///  @return `false` if the connection ended or a stop was requested before
  ///  `buffer` was completely filled.
  bool ReadFully(std::span<std::byte> buffer);

  ///  Writes every message currently queued, oldest first.
  ///  @return `false` if a write failed and the connection should end.
  bool DrainOutbound();

  ///  Waits out `kAdapterIpcReconnectDelay`, or returns immediately once
  ///  `Stop()` is called.
  void WaitBoundedBackoff();

  ///  The transport this connection reads and writes through.
  IAdapterIpcSocket &socket_;
  ///  Encodes outbound messages and decodes inbound frames.
  const IIpcFrameCodec &codec_;
  ///  The lifecycle event hooks this connection invokes.
  AdapterIpcConnectionCallbacks callbacks_;
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
  ///  The background connect/serve thread, started at construction.
  std::thread worker_;
};

} //  namespace dovahlink::adapter::ipc
