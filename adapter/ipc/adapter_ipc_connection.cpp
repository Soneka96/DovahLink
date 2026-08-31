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
    AdapterIpcConnectionCallbacks callbacks,
    std::chrono::milliseconds establishmentTimeout)
    : socket_(socket), codec_(codec), callbacks_(std::move(callbacks)),
      establishmentTimeout_(establishmentTimeout) {}

AdapterIpcConnection::~AdapterIpcConnection() { Stop(); }

void AdapterIpcConnection::Start() {
  std::lock_guard<std::mutex> lock(lifecycleMutex_);
  if (worker_.joinable()) {
    return;
  }
  worker_ = std::thread([this] { RunLoop(); });
}

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
  try {
    socket_.RequestStop();
  } catch (...) {
    //  A transport shutdown failure must not prevent the worker join or
    //  escape a callback that requested shutdown.
  }

  std::unique_lock<std::mutex> lifecycleLock(lifecycleMutex_);
  while (worker_.joinable()) {
    if (worker_.get_id() == std::this_thread::get_id()) {
      //  A lifecycle callback may request shutdown from the connection's own
      //  worker. The worker will return after the callback completes; joining
      //  it here would deadlock or throw a self-join error.
      return;
    }
    if (!joinInProgress_) {
      joinInProgress_ = true;
      break;
    }
    lifecycleCondition_.wait(lifecycleLock);
  }

  lifecycleLock.unlock();

  if (worker_.joinable()) {
    try {
      worker_.join();
    } catch (...) {
      std::lock_guard<std::mutex> lock(lifecycleMutex_);
      joinInProgress_ = false;
      lifecycleCondition_.notify_all();
      throw;
    }
  }

  {
    std::lock_guard<std::mutex> lock(lifecycleMutex_);
    joinInProgress_ = false;
  }
  lifecycleCondition_.notify_all();
}

void AdapterIpcConnection::RunLoop() {
  bool connected = false;
  try {
    while (true) {
      {
        std::lock_guard<std::mutex> lock(stopMutex_);
        if (stopping_) {
          return;
        }
      }

      bool connectedAttempt = false;
      try {
        connectedAttempt = socket_.Connect();
      } catch (...) {
        InvokeContained(callbacks_.onConnectionAttemptFailed);
        throw;
      }
      if (!connectedAttempt) {
        ClearOutbound();
        InvokeContained(callbacks_.onConnectionAttemptFailed);
        WaitBoundedBackoff();
        continue;
      }
      //  A successful connect begins a fresh transport generation. Any work
      //  left from the prior generation, including work queued while no
      //  transport was connected, must be gone before onConnected queues the
      //  fresh Hello.
      ClearOutbound();
      connected = true;
      const auto establishmentDeadline =
          std::chrono::steady_clock::now() + establishmentTimeout_;

      InvokeContained(callbacks_.onConnected);

      ServeConnection(establishmentDeadline);

      //  Best-effort final flush: a handler invoked from ServeConnection (for
      //  example a decode-failure response) may have queued one last message
      //  that no read poll will run again to drain.
      DrainOutbound();

      socket_.Close();
      ClearOutbound();
      InvokeContained(callbacks_.onDisconnected);
      connected = false;

      WaitBoundedBackoff();
    }
  } catch (...) {
    //  No transport, codec, or callback failure may escape the worker thread.
    try {
      socket_.Close();
    } catch (...) {
    }
    ClearOutbound();
    if (connected) {
      InvokeContained(callbacks_.onDisconnected);
    }
  }
}

void AdapterIpcConnection::ServeConnection(
    std::chrono::steady_clock::time_point establishmentDeadline) {
  bool authenticated = false;

  while (true) {
    {
      std::lock_guard<std::mutex> lock(stopMutex_);
      if (stopping_) {
        return;
      }
    }

    if (!authenticated &&
        std::chrono::steady_clock::now() >= establishmentDeadline) {
      return;
    }

    bool decodeFailed = false;
    std::optional<IpcMessage> message = ReadOneMessage(
        decodeFailed,
        authenticated ? std::nullopt
                      : std::optional<std::chrono::steady_clock::time_point>{
                            establishmentDeadline});
    if (decodeFailed) {
      InvokeContained(callbacks_.onDecodeFailure);
      return;
    }
    if (!message.has_value()) {
      //  Disconnected, or a stop was requested while waiting for a frame.
      return;
    }

    AdapterIpcMessageDisposition disposition = DispatchInboundMessage(*message);
    if (disposition == AdapterIpcMessageDisposition::kClose) {
      return;
    }
    if (disposition == AdapterIpcMessageDisposition::kAuthenticated) {
      authenticated = true;
    }
  }
}

AdapterIpcMessageDisposition
AdapterIpcConnection::DispatchInboundMessage(const IpcMessage &message) {
  if (!callbacks_.onMessageReceived) {
    return AdapterIpcMessageDisposition::kContinue;
  }
  try {
    return callbacks_.onMessageReceived(message);
  } catch (...) {
    //  Contained, per ai/context/skse/cpp-style.md's worker-thread boundary
    //  rule; an unexpected handler failure is not itself a reason to end an
    //  otherwise healthy connection.
    return AdapterIpcMessageDisposition::kContinue;
  }
}

std::optional<IpcMessage> AdapterIpcConnection::ReadOneMessage(
    bool &decodeFailed,
    std::optional<std::chrono::steady_clock::time_point> deadline) {
  decodeFailed = false;

  std::array<std::byte, sizeof(std::uint32_t)> lengthPrefix{};
  if (!ReadFully(lengthPrefix, deadline)) {
    return std::nullopt;
  }

  std::optional<std::size_t> frameLength =
      codec_.TryReadFrameLength(lengthPrefix);
  if (!frameLength.has_value()) {
    decodeFailed = true;
    return std::nullopt;
  }

  std::vector<std::byte> frame(*frameLength);
  if (!ReadFully(frame, deadline)) {
    return std::nullopt;
  }

  std::expected<IpcMessage, IpcRejectReason> decoded = codec_.Decode(frame);
  if (!decoded.has_value()) {
    decodeFailed = true;
    return std::nullopt;
  }

  return *decoded;
}

bool AdapterIpcConnection::ReadFully(
    std::span<std::byte> buffer,
    std::optional<std::chrono::steady_clock::time_point> deadline) {
  std::size_t totalRead = 0;
  while (totalRead < buffer.size()) {
    {
      std::lock_guard<std::mutex> lock(stopMutex_);
      if (stopping_) {
        return false;
      }
    }

    if (deadline.has_value() && std::chrono::steady_clock::now() >= *deadline) {
      return false;
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

    if (deadline.has_value() && std::chrono::steady_clock::now() >= *deadline) {
      return false;
    }
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

void AdapterIpcConnection::ClearOutbound() {
  std::lock_guard<std::mutex> lock(outboundMutex_);
  outbound_.clear();
}

void AdapterIpcConnection::WaitBoundedBackoff() {
  std::unique_lock<std::mutex> lock(stopMutex_);
  stopCondition_.wait_for(lock, kAdapterIpcReconnectDelay,
                          [this] { return stopping_; });
}

} //  namespace dovahlink::adapter::ipc
