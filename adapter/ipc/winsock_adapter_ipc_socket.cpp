#include "ipc/winsock_adapter_ipc_socket.hpp"

namespace dovahlink::adapter::ipc {

namespace {

///  How long one internal poll waits before re-checking a requested stop.
constexpr int kPollTimeoutMilliseconds = 100;

} //  namespace

WinsockAdapterIpcSocket::WinsockAdapterIpcSocket(std::uint16_t port)
    : port_(port) {
  WSADATA wsaData;
  WSAStartup(MAKEWORD(2, 2), &wsaData);
}

WinsockAdapterIpcSocket::~WinsockAdapterIpcSocket() {
  Close();
  WSACleanup();
}

bool WinsockAdapterIpcSocket::Connect() {
  socket_ = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
  if (socket_ == INVALID_SOCKET) {
    return false;
  }

  u_long nonBlocking = 1;
  ioctlsocket(socket_, FIONBIO, &nonBlocking);

  sockaddr_in target{};
  target.sin_family = AF_INET;
  target.sin_port = htons(port_.load());
  inet_pton(AF_INET, "127.0.0.1", &target.sin_addr);
  connect(socket_, reinterpret_cast<sockaddr *>(&target), sizeof(target));

  bool connected = false;
  while (!stopRequested_.load()) {
    fd_set writeSet;
    FD_ZERO(&writeSet);
    FD_SET(socket_, &writeSet);
    fd_set errorSet = writeSet;
    timeval pollInterval{0, kPollTimeoutMilliseconds * 1000};

    int selectResult = select(0, nullptr, &writeSet, &errorSet, &pollInterval);
    if (selectResult == SOCKET_ERROR || FD_ISSET(socket_, &errorSet)) {
      break;
    }
    if (FD_ISSET(socket_, &writeSet)) {
      connected = true;
      break;
    }
  }

  if (!connected) {
    Close();
    return false;
  }

  u_long blocking = 0;
  ioctlsocket(socket_, FIONBIO, &blocking);
  DWORD timeoutMilliseconds = kPollTimeoutMilliseconds;
  setsockopt(socket_, SOL_SOCKET, SO_RCVTIMEO,
             reinterpret_cast<const char *>(&timeoutMilliseconds),
             sizeof(timeoutMilliseconds));
  setsockopt(socket_, SOL_SOCKET, SO_SNDTIMEO,
             reinterpret_cast<const char *>(&timeoutMilliseconds),
             sizeof(timeoutMilliseconds));
  return true;
}

std::optional<std::size_t>
WinsockAdapterIpcSocket::TryReadSome(std::span<std::byte> buffer) {
  if (stopRequested_.load()) {
    return std::nullopt;
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
    return std::size_t{0};
  }
  return std::nullopt;
}

bool WinsockAdapterIpcSocket::WriteAll(std::span<const std::byte> data) {
  std::size_t totalWritten = 0;
  while (totalWritten < data.size()) {
    if (stopRequested_.load()) {
      return false;
    }

    int bytesWritten = send(
        socket_, reinterpret_cast<const char *>(data.data() + totalWritten),
        static_cast<int>(data.size() - totalWritten), 0);
    if (bytesWritten > 0) {
      totalWritten += static_cast<std::size_t>(bytesWritten);
      continue;
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

} //  namespace dovahlink::adapter::ipc
