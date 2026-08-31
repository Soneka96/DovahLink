#pragma once

#include <atomic>
#include <cstddef>
#include <cstdint>
#include <optional>
#include <span>

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <winsock2.h>
#include <ws2tcpip.h>

namespace dovahlink::adapter::ipc {

///  A blocking, internally-interruptible TCP client socket to the private
///  host-to-adapter IPC channel's loopback listener. Every blocking call
///  polls a short internal timeout so `RequestStop` (callable from any
///  thread) makes an in-progress or future call return promptly instead of
///  hanging indefinitely, per `ai/context/host/architecture.md`'s "adapter
///  reconnect is bounded and performed outside game-thread work".
class IAdapterIpcSocket {
public:
  virtual ~IAdapterIpcSocket() = default;

  ///  Changes the loopback port used by the next connection attempt.
  virtual void SetPort(std::uint16_t port) = 0;

  ///  Attempts to establish the connection to the configured loopback
  ///  target. Blocking, bounded by short internal polls against a requested
  ///  stop.
  ///  @return `true` once connected; `false` on failure or a requested stop.
  virtual bool Connect() = 0;

  ///  Attempts to read into `buffer`, bounded by exactly one short internal
  ///  poll -- callers own accumulating partial reads across multiple calls
  ///  themselves, so they can interleave other work (such as draining an
  ///  outbound queue) between polls instead of this call swallowing an
  ///  arbitrarily long wait for inbound data.
  ///  @return The number of bytes read this call (zero if none were ready
  ///  within this one poll), or `std::nullopt` on a closed connection, any
  ///  other read failure, or a requested stop.
  virtual std::optional<std::size_t>
  TryReadSome(std::span<std::byte> buffer) = 0;

  ///  Writes every byte in `data`. Blocking, bounded by short internal polls
  ///  against a requested stop.
  ///  @return `true` once every byte is written; `false` on a closed
  ///  connection, any other write failure, or a requested stop.
  virtual bool WriteAll(std::span<const std::byte> data) = 0;

  ///  Closes the connection. Idempotent. Callers must call this only from
  ///  the same thread performing I/O through this instance.
  virtual void Close() = 0;

  ///  Requests that any in-progress or future blocking call return promptly.
  ///  Idempotent and safe to call from any thread, including while another
  ///  thread is blocked inside `Connect`/`ReadExact`/`WriteAll`.
  virtual void RequestStop() = 0;
};

///  @copydoc IAdapterIpcSocket
class WinsockAdapterIpcSocket final : public IAdapterIpcSocket {
public:
  ///  Creates a socket targeting the loopback IPC listener on `port`.
  explicit WinsockAdapterIpcSocket(std::uint16_t port);

  ///  Closes the connection and releases Winsock resources.
  ~WinsockAdapterIpcSocket() override;

  WinsockAdapterIpcSocket(const WinsockAdapterIpcSocket &) = delete;
  WinsockAdapterIpcSocket &operator=(const WinsockAdapterIpcSocket &) = delete;

  ///  @copydoc IAdapterIpcSocket::Connect
  bool Connect() override;

  ///  @copydoc IAdapterIpcSocket::TryReadSome
  std::optional<std::size_t> TryReadSome(std::span<std::byte> buffer) override;

  ///  @copydoc IAdapterIpcSocket::WriteAll
  bool WriteAll(std::span<const std::byte> data) override;

  ///  @copydoc IAdapterIpcSocket::Close
  void Close() override;

  ///  @copydoc IAdapterIpcSocket::RequestStop
  void RequestStop() override;

  ///  Reconfigures the loopback port a subsequent `Connect()` call targets.
  ///  Not part of `IAdapterIpcSocket`: only the composition root, which holds
  ///  the concrete type, needs to redirect the target once the real
  ///  packaged-host endpoint is discovered -- ordinary consumers behind the
  ///  interface never reconfigure a socket's target. Safe to call
  ///  concurrently with `Connect()` from another thread; a call already in
  ///  progress uses whichever value it already read, and only the next
  ///  `Connect()` call is guaranteed to see the update.
  void SetPort(std::uint16_t port) override;

  ///  The loopback port a subsequent `Connect()` call currently targets. Not
  ///  part of `IAdapterIpcSocket`, for the same reason as `SetPort`.
  std::uint16_t Port() const;

private:
  ///  The loopback port to connect to.
  std::atomic<std::uint16_t> port_;
  ///  The underlying Winsock socket handle, or `INVALID_SOCKET` when closed.
  SOCKET socket_ = INVALID_SOCKET;
  ///  Set by `RequestStop`; polled by every blocking call.
  std::atomic<bool> stopRequested_{false};
};

} //  namespace dovahlink::adapter::ipc
