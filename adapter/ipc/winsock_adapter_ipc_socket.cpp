#include "ipc/winsock_adapter_ipc_socket.hpp"

#include <chrono>

namespace dovahlink::adapter::ipc {

namespace {

///  How long one internal poll waits before re-checking a requested stop.
constexpr int kPollTimeoutMilliseconds = 100;
///  Absolute bound for the nonblocking TCP establishment phase.
constexpr auto kConnectTimeout = std::chrono::seconds(2);
///  Absolute bound for one complete blocking write operation.
constexpr auto kWriteTimeout = std::chrono::seconds(2);

} //  namespace

WinsockAdapterIpcSocket::WinsockAdapterIpcSocket(std::uint16_t port)
    : port_(port) {
  WSADATA wsaData;
  winsockInitialized_ = WSAStartup(MAKEWORD(2, 2), &wsaData) == 0;
}

WinsockAdapterIpcSocket::~WinsockAdapterIpcSocket() {
  Close();
  if (winsockInitialized_) {
    WSACleanup();
  }
}

bool WinsockAdapterIpcSocket::Connect() {
  if (!winsockInitialized_ || stopRequested_.load()) {
    return false;
  }

  socket_ = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
  if (socket_ == INVALID_SOCKET) {
    return false;
  }

  u_long nonBlocking = 1;
  if (ioctlsocket(socket_, FIONBIO, &nonBlocking) != 0) {
    Close();
    return false;
  }

  sockaddr_in target{};
  target.sin_family = AF_INET;
  target.sin_port = htons(port_.load());
  if (inet_pton(AF_INET, "127.0.0.1", &target.sin_addr) != 1) {
    Close();
    return false;
  }

  int connectResult =
      connect(socket_, reinterpret_cast<sockaddr *>(&target), sizeof(target));
  if (connectResult == SOCKET_ERROR) {
    int error = WSAGetLastError();
    if (error != WSAEWOULDBLOCK && error != WSAEINPROGRESS &&
        error != WSAEALREADY) {
      Close();
      return false;
    }
  }

  bool connected = false;
  const auto deadline = std::chrono::steady_clock::now() + kConnectTimeout;
  while (!stopRequested_.load()) {
    const auto remaining = deadline - std::chrono::steady_clock::now();
    if (remaining <= std::chrono::steady_clock::duration::zero()) {
      break;
    }
    fd_set writeSet;
    FD_ZERO(&writeSet);
    FD_SET(socket_, &writeSet);
    fd_set errorSet = writeSet;
    const auto remainingMicroseconds =
        std::chrono::duration_cast<std::chrono::microseconds>(remaining)
            .count();
    if (remainingMicroseconds <= 0) {
      break;
    }
    timeval pollInterval{static_cast<long>(remainingMicroseconds / 1000000),
                         static_cast<long>(remainingMicroseconds % 1000000)};

    int selectResult = select(0, nullptr, &writeSet, &errorSet, &pollInterval);
    if (selectResult == SOCKET_ERROR || FD_ISSET(socket_, &errorSet)) {
      break;
    }
    if (FD_ISSET(socket_, &writeSet)) {
      int connectError = 0;
      int connectErrorSize = sizeof(connectError);
      if (getsockopt(socket_, SOL_SOCKET, SO_ERROR,
                     reinterpret_cast<char *>(&connectError),
                     &connectErrorSize) != 0 ||
          connectError != 0) {
        break;
      }
      connected = true;
      break;
    }
  }

  if (!connected) {
    Close();
    return false;
  }

  u_long blocking = 0;
  if (ioctlsocket(socket_, FIONBIO, &blocking) != 0) {
    Close();
    return false;
  }
  DWORD timeoutMilliseconds = kPollTimeoutMilliseconds;
  if (setsockopt(socket_, SOL_SOCKET, SO_RCVTIMEO,
                 reinterpret_cast<const char *>(&timeoutMilliseconds),
                 sizeof(timeoutMilliseconds)) != 0 ||
      setsockopt(socket_, SOL_SOCKET, SO_SNDTIMEO,
                 reinterpret_cast<const char *>(&timeoutMilliseconds),
                 sizeof(timeoutMilliseconds)) != 0) {
    Close();
    return false;
  }
  return true;
}

std::optional<std::size_t>
WinsockAdapterIpcSocket::TryReadSome(std::span<std::byte> buffer) {
  if (stopRequested_.load() || socket_ == INVALID_SOCKET) {
    return std::nullopt;
  }
  if (buffer.empty()) {
    return std::size_t{0};
  }

  int bytesRead = recv(socket_, reinterpret_cast<char *>(buffer.data()),
                       static_cast<int>(buffer.size()), 0);
  if (bytesRead > 0) {
    return static_cast<std::size_t>(bytesRead);
  }
  if (bytesRead == 0) {
    //  The peer closed the connection.
    return std::nullopt;
  }

  int error = WSAGetLastError();
  if (error == WSAETIMEDOUT || error == WSAEWOULDBLOCK) {
    //  No data within this one poll interval; not an error.
    if (stopRequested_.load()) {
      return std::nullopt;
    }
    return std::size_t{0};
  }
  return std::nullopt;
}

bool WinsockAdapterIpcSocket::WriteAll(std::span<const std::byte> data) {
  if (socket_ == INVALID_SOCKET) {
    return false;
  }
  std::size_t totalWritten = 0;
  const auto deadline = std::chrono::steady_clock::now() + kWriteTimeout;
  while (totalWritten < data.size()) {
    const auto remaining = deadline - std::chrono::steady_clock::now();
    if (stopRequested_.load() ||
        remaining <= std::chrono::steady_clock::duration::zero()) {
      return false;
    }

    const auto remainingMilliseconds =
        std::chrono::duration_cast<std::chrono::milliseconds>(remaining)
            .count();
    if (remainingMilliseconds <= 0) {
      return false;
    }
    const DWORD sendTimeout =
        static_cast<DWORD>(remainingMilliseconds < kPollTimeoutMilliseconds
                               ? remainingMilliseconds
                               : kPollTimeoutMilliseconds);
    if (setsockopt(socket_, SOL_SOCKET, SO_SNDTIMEO,
                   reinterpret_cast<const char *>(&sendTimeout),
                   sizeof(sendTimeout)) != 0) {
      return false;
    }

    int bytesWritten = send(
        socket_, reinterpret_cast<const char *>(data.data() + totalWritten),
        static_cast<int>(data.size() - totalWritten), 0);
    if (bytesWritten > 0) {
      totalWritten += static_cast<std::size_t>(bytesWritten);
      continue;
    }
    if (bytesWritten == 0) {
      return false;
    }

    int error = WSAGetLastError();
    if (error == WSAETIMEDOUT || error == WSAEWOULDBLOCK) {
      continue;
    }
    return false;
  }
  return true;
}

void WinsockAdapterIpcSocket::Close() {
  if (socket_ != INVALID_SOCKET) {
    closesocket(socket_);
    socket_ = INVALID_SOCKET;
  }
}

void WinsockAdapterIpcSocket::RequestStop() { stopRequested_.store(true); }

void WinsockAdapterIpcSocket::SetPort(std::uint16_t port) { port_.store(port); }

std::uint16_t WinsockAdapterIpcSocket::Port() const { return port_.load(); }

} //  namespace dovahlink::adapter::ipc
