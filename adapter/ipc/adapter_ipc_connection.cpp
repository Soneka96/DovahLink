#include "ipc/adapter_ipc_connection.hpp"

#include "ipc/ipc_constants.hpp"
#include "ipc/winsock_adapter_ipc_socket.hpp"

#include <array>
#include <cstdint>
#include <expected>
#include <utility>
#include <vector>

namespace dovahlink::adapter::ipc {

namespace {

///  Invokes `callback` if set, containing any exception it throws so it
///  never escapes the connection's own background thread, per
///  `ai/context/skse/cpp-style.md`'s "Catch all exceptions at callback,
///  worker-thread, and transport-completion boundaries".
template <typename Callback, typename... Args>
void InvokeContained(const Callback &callback, Args &&...args) {
  if (!callback) {
    return;
  }
  try {
    callback(std::forward<Args>(args)...);
  } catch (...) {
  }
}

} //  namespace

AdapterIpcConnection::AdapterIpcConnection(
    IAdapterIpcSocket &socket, const IIpcFrameCodec &codec,
    AdapterIpcConnectionCallbacks callbacks)
    : socket_(socket), codec_(codec), callbacks_(std::move(callbacks)) {
  worker_ = std::thread([this] { RunLoop(); });
}

AdapterIpcConnection::~AdapterIpcConnection() { Stop(); }

bool AdapterIpcConnection::TrySend(const IpcMessage &message) {
  std::lock_guard<std::mutex> stopLock(stopMutex_);
  if (stopping_) {
    return false;
  }

  std::lock_guard<std::mutex> outboundLock(outboundMutex_);
  if (outbound_.size() >= kMaxIpcQueuedMessages) {
    return false;
  }
  outbound_.push_back(message);
  return true;
}

void AdapterIpcConnection::Stop() {
  {
    std::lock_guard<std::mutex> lock(stopMutex_);
    stopping_ = true;
  }
  stopCondition_.notify_all();
  socket_.RequestStop();
  if (worker_.joinable()) {
    worker_.join();
  }
}

void AdapterIpcConnection::RunLoop() {
  while (true) {
    {
      std::lock_guard<std::mutex> lock(stopMutex_);
      if (stopping_) {
        return;
      }
    }

    if (!socket_.Connect()) {
      WaitBoundedBackoff();
      continue;
    }

    InvokeContained(callbacks_.onConnected);

    ServeConnection();

    socket_.Close();
    InvokeContained(callbacks_.onDisconnected);

    WaitBoundedBackoff();
  }
}

void AdapterIpcConnection::ServeConnection() {
  while (true) {
    {
      std::lock_guard<std::mutex> lock(stopMutex_);
      if (stopping_) {
        return;
      }
    }

    bool decodeFailed = false;
    std::optional<IpcMessage> message = ReadOneMessage(decodeFailed);
    if (decodeFailed) {
      InvokeContained(callbacks_.onDecodeFailure);
      return;
    }
    if (!message.has_value()) {
      //  Disconnected, or a stop was requested while waiting for a frame.
      return;
    }

    InvokeContained(callbacks_.onMessageReceived, *message);
  }
}

std::optional<IpcMessage>
AdapterIpcConnection::ReadOneMessage(bool &decodeFailed) {
  decodeFailed = false;

  std::array<std::byte, sizeof(std::uint32_t)> lengthPrefix{};
  if (!ReadFully(lengthPrefix)) {
    return std::nullopt;
  }

  std::optional<std::size_t> frameLength =
      codec_.TryReadFrameLength(lengthPrefix);
  if (!frameLength.has_value()) {
    decodeFailed = true;
    return std::nullopt;
  }

  std::vector<std::byte> frame(*frameLength);
  if (!ReadFully(frame)) {
    return std::nullopt;
  }

  std::expected<IpcMessage, IpcRejectReason> decoded = codec_.Decode(frame);
  if (!decoded.has_value()) {
    decodeFailed = true;
    return std::nullopt;
  }

  return *decoded;
}

bool AdapterIpcConnection::ReadFully(std::span<std::byte> buffer) {
  std::size_t totalRead = 0;
  while (totalRead < buffer.size()) {
    {
      std::lock_guard<std::mutex> lock(stopMutex_);
      if (stopping_) {
        return false;
      }
    }

    if (!DrainOutbound()) {
      return false;
    }

    std::optional<std::size_t> bytesRead =
        socket_.TryReadSome(buffer.subspan(totalRead));
    if (!bytesRead.has_value()) {
      return false;
    }
    totalRead += *bytesRead;
  }
  return true;
}

bool AdapterIpcConnection::DrainOutbound() {
  while (true) {
    IpcMessage message;
    {
      std::lock_guard<std::mutex> lock(outboundMutex_);
      if (outbound_.empty()) {
        return true;
      }
      message = std::move(outbound_.front());
      outbound_.pop_front();
    }

    std::vector<std::byte> frame = codec_.Encode(message);
    if (!socket_.WriteAll(frame)) {
      return false;
    }
  }
}

void AdapterIpcConnection::WaitBoundedBackoff() {
  std::unique_lock<std::mutex> lock(stopMutex_);
  stopCondition_.wait_for(lock, kAdapterIpcReconnectDelay,
                          [this] { return stopping_; });
}

} //  namespace dovahlink::adapter::ipc
