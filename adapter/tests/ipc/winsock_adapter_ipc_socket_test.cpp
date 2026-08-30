#include "ipc/winsock_adapter_ipc_socket.hpp"

#include <catch2/catch_test_macros.hpp>

#include <array>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <mutex>
#include <optional>
#include <span>
#include <thread>

#include <winsock2.h>
#include <ws2tcpip.h>

using dovahlink::adapter::ipc::WinsockAdapterIpcSocket;

namespace {

///  A minimal raw TCP loopback listener, standing in for the private IPC
///  channel's real host-side listener so `WinsockAdapterIpcSocket` can be
///  proven against an actual socket, per `ai/context/skse/testing.md`'s
///  "before using real sockets".
class RawLoopbackListener {
public:
  RawLoopbackListener() {
    WSADATA wsaData;
    WSAStartup(MAKEWORD(2, 2), &wsaData);

    try {
      listenSocket_ = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
      REQUIRE(listenSocket_ != INVALID_SOCKET);

      sockaddr_in address{};
      address.sin_family = AF_INET;
      address.sin_port = 0;
      inet_pton(AF_INET, "127.0.0.1", &address.sin_addr);
      REQUIRE(bind(listenSocket_, reinterpret_cast<sockaddr *>(&address),
                   sizeof(address)) == 0);
      REQUIRE(listen(listenSocket_, 1) == 0);

      int addressLength = sizeof(address);
      REQUIRE(getsockname(listenSocket_, reinterpret_cast<sockaddr *>(&address),
                          &addressLength) == 0);
      port_ = ntohs(address.sin_port);
    } catch (...) {
      StopAccepting();
      WSACleanup();
      throw;
    }
  }

  ~RawLoopbackListener() {
    SOCKET acceptedSocket;
    {
      std::lock_guard<std::mutex> lock(mutex_);
      acceptedSocket = acceptedSocket_;
      acceptedSocket_ = INVALID_SOCKET;
    }
    if (acceptedSocket != INVALID_SOCKET) {
      closesocket(acceptedSocket);
    }
    StopAccepting();
    WSACleanup();
  }

  RawLoopbackListener(const RawLoopbackListener &) = delete;
  RawLoopbackListener &operator=(const RawLoopbackListener &) = delete;

  ///  The actual loopback port this listener is bound to.
  std::uint16_t Port() const { return port_; }

  ///  Blocks until one connection is accepted, storing it for `Send`. Safe to
  ///  call from a background thread: it never asserts -- check `Accepted()`
  ///  on the calling test's own thread afterward instead.
  void AcceptOne() {
    SOCKET listenSocket;
    {
      std::lock_guard<std::mutex> lock(mutex_);
      listenSocket = listenSocket_;
    }
    SOCKET acceptedSocket = accept(listenSocket, nullptr, nullptr);
    {
      std::lock_guard<std::mutex> lock(mutex_);
      acceptedSocket_ = acceptedSocket;
    }
  }

  ///  Whether the most recent `AcceptOne` succeeded.
  bool Accepted() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return acceptedSocket_ != INVALID_SOCKET;
  }

  ///  Closes the listening socket so a pending `AcceptOne` returns.
  void StopAccepting() {
    SOCKET listenSocket;
    {
      std::lock_guard<std::mutex> lock(mutex_);
      listenSocket = listenSocket_;
      listenSocket_ = INVALID_SOCKET;
    }
    if (listenSocket != INVALID_SOCKET) {
      closesocket(listenSocket);
    }
  }

  ///  Sends every byte in `data` over the accepted connection.
  ///  @return `true` when every byte was sent.
  bool Send(std::span<const std::byte> data) {
    SOCKET acceptedSocket;
    {
      std::lock_guard<std::mutex> lock(mutex_);
      acceptedSocket = acceptedSocket_;
    }
    return send(acceptedSocket, reinterpret_cast<const char *>(data.data()),
                static_cast<int>(data.size()),
                0) == static_cast<int>(data.size());
  }

private:
  mutable std::mutex mutex_;
  SOCKET listenSocket_ = INVALID_SOCKET;
  SOCKET acceptedSocket_ = INVALID_SOCKET;
  std::uint16_t port_ = 0;
};

///  Repeatedly polls `TryReadSome` until `buffer` is completely filled or a
///  generous bound elapses, mirroring how `AdapterIpcConnection` itself
///  accumulates a read across multiple polls.
bool ReadFullyForTest(WinsockAdapterIpcSocket &socket,
                      std::span<std::byte> buffer) {
  std::size_t totalRead = 0;
  auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
  while (totalRead < buffer.size()) {
    if (std::chrono::steady_clock::now() > deadline) {
      return false;
    }
    std::optional<std::size_t> bytesRead =
        socket.TryReadSome(buffer.subspan(totalRead));
    if (!bytesRead.has_value()) {
      return false;
    }
    totalRead += *bytesRead;
  }
  return true;
}

} //  namespace

TEST_CASE("WinsockAdapterIpcSocket cleanup joins an accept thread after a "
          "failed connect",
          "[ipc][winsock_adapter_ipc_socket]") {
  RawLoopbackListener listener;
  WinsockAdapterIpcSocket socket(1);

  std::thread acceptThread([&] { listener.AcceptOne(); });
  bool connected = socket.Connect();
  if (!connected) {
    listener.StopAccepting();
  }
  acceptThread.join();

  REQUIRE_FALSE(connected);
  CHECK_FALSE(listener.Accepted());
}

TEST_CASE("WinsockAdapterIpcSocket fails to connect when nothing is "
          "listening",
          "[ipc][winsock_adapter_ipc_socket]") {
  //  Port 1 is a reserved, essentially always-closed loopback port.
  WinsockAdapterIpcSocket socket(1);

  CHECK_FALSE(socket.Connect());
}

TEST_CASE("WinsockAdapterIpcSocket connects and round-trips real bytes over "
          "a real loopback socket",
          "[ipc][winsock_adapter_ipc_socket]") {
  RawLoopbackListener listener;
  WinsockAdapterIpcSocket socket(listener.Port());

  std::thread acceptThread([&] { listener.AcceptOne(); });
  bool connected = socket.Connect();
  if (!connected) {
    listener.StopAccepting();
  }
  acceptThread.join();
  REQUIRE(connected);
  REQUIRE(listener.Accepted());

  std::array<std::byte, 4> payload{std::byte{1}, std::byte{2}, std::byte{3},
                                   std::byte{4}};
  REQUIRE(listener.Send(payload));

  std::array<std::byte, 4> received{};
  REQUIRE(ReadFullyForTest(socket, received));
  CHECK(received == payload);

  REQUIRE(socket.WriteAll(payload));
}

TEST_CASE("WinsockAdapterIpcSocket::TryReadSome returns nullopt once "
          "RequestStop is called",
          "[ipc][winsock_adapter_ipc_socket]") {
  RawLoopbackListener listener;
  WinsockAdapterIpcSocket socket(listener.Port());

  std::thread acceptThread([&] { listener.AcceptOne(); });
  bool connected = socket.Connect();
  if (!connected) {
    listener.StopAccepting();
  }
  acceptThread.join();
  REQUIRE(connected);
  REQUIRE(listener.Accepted());

  socket.RequestStop();

  std::array<std::byte, 4> buffer{};
  CHECK_FALSE(socket.TryReadSome(buffer).has_value());
}

TEST_CASE("WinsockAdapterIpcSocket::WriteAll fails once RequestStop is "
          "called",
          "[ipc][winsock_adapter_ipc_socket]") {
  RawLoopbackListener listener;
  WinsockAdapterIpcSocket socket(listener.Port());

  std::thread acceptThread([&] { listener.AcceptOne(); });
  bool connected = socket.Connect();
  if (!connected) {
    listener.StopAccepting();
  }
  acceptThread.join();
  REQUIRE(connected);
  REQUIRE(listener.Accepted());

  socket.RequestStop();

  std::array<std::byte, 4> payload{std::byte{1}, std::byte{2}, std::byte{3},
                                   std::byte{4}};
  CHECK_FALSE(socket.WriteAll(payload));
}

TEST_CASE("WinsockAdapterIpcSocket::WriteAll with an empty span succeeds "
          "trivially",
          "[ipc][winsock_adapter_ipc_socket]") {
  RawLoopbackListener listener;
  WinsockAdapterIpcSocket socket(listener.Port());

  std::thread acceptThread([&] { listener.AcceptOne(); });
  bool connected = socket.Connect();
  if (!connected) {
    listener.StopAccepting();
  }
  acceptThread.join();
  REQUIRE(connected);
  REQUIRE(listener.Accepted());

  CHECK(socket.WriteAll(std::span<const std::byte>{}));
}

TEST_CASE("WinsockAdapterIpcSocket::SetPort redirects the next Connect call "
          "to the new port",
          "[ipc][winsock_adapter_ipc_socket]") {
  RawLoopbackListener listener;
  //  Port 1 is a reserved, essentially always-closed loopback port: the
  //  socket is constructed targeting it, then redirected before connecting.
  WinsockAdapterIpcSocket socket(1);

  socket.SetPort(listener.Port());

  std::thread acceptThread([&] { listener.AcceptOne(); });
  bool connected = socket.Connect();
  if (!connected) {
    listener.StopAccepting();
  }
  acceptThread.join();

  REQUIRE(connected);
  CHECK(listener.Accepted());
}

TEST_CASE("WinsockAdapterIpcSocket::SetPort called more than once before "
          "Connect uses only the most recent value",
          "[ipc][winsock_adapter_ipc_socket]") {
  RawLoopbackListener wrongListener;
  RawLoopbackListener rightListener;
  WinsockAdapterIpcSocket socket(1);

  socket.SetPort(wrongListener.Port());
  socket.SetPort(rightListener.Port());

  std::thread acceptThread([&] { rightListener.AcceptOne(); });
  bool connected = socket.Connect();
  if (!connected) {
    rightListener.StopAccepting();
  }
  acceptThread.join();

  REQUIRE(connected);
  CHECK(rightListener.Accepted());
  CHECK_FALSE(wrongListener.Accepted());
}

TEST_CASE("WinsockAdapterIpcSocket::SetPort redirects a later reconnection "
          "after the first connection closes",
          "[ipc][winsock_adapter_ipc_socket]") {
  RawLoopbackListener firstListener;
  WinsockAdapterIpcSocket socket(firstListener.Port());

  std::thread firstAcceptThread([&] { firstListener.AcceptOne(); });
  REQUIRE(socket.Connect());
  firstAcceptThread.join();
  REQUIRE(firstListener.Accepted());
  socket.Close();

  RawLoopbackListener secondListener;
  socket.SetPort(secondListener.Port());

  std::thread secondAcceptThread([&] { secondListener.AcceptOne(); });
  bool reconnected = socket.Connect();
  if (!reconnected) {
    secondListener.StopAccepting();
  }
  secondAcceptThread.join();

  REQUIRE(reconnected);
  CHECK(secondListener.Accepted());
}

TEST_CASE("WinsockAdapterIpcSocket::Close is idempotent",
          "[ipc][winsock_adapter_ipc_socket]") {
  RawLoopbackListener listener;
  WinsockAdapterIpcSocket socket(listener.Port());

  std::thread acceptThread([&] { listener.AcceptOne(); });
  bool connected = socket.Connect();
  if (!connected) {
    listener.StopAccepting();
  }
  acceptThread.join();
  REQUIRE(connected);
  REQUIRE(listener.Accepted());

  socket.Close();
  socket.Close();
}
