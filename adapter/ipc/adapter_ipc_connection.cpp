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
    std::chrono::milliseconds establishmentTimeout, ClockNow clockNow)
    : socket_(socket), codec_(codec), callbacks_(std::move(callbacks)),
      establishmentTimeout_(establishmentTimeout),
      clockNow_(std::move(clockNow)) {}

AdapterIpcConnection::~AdapterIpcConnection() { Stop(); }

void AdapterIpcConnection::Start() {
  {
    std::lock_guard<std::mutex> stopLock(stopMutex_);
    if (stopping_) {
      return;
    }
  }

  std::lock_guard<std::mutex> lock(lifecycleMutex_);
  if (worker_.joinable() && worker_.get_id() == std::this_thread::get_id()) {
    return;
  }
  {
    std::lock_guard<std::mutex> stopLock(stopMutex_);
    if (stopping_) {
      return;
    }
  }
  if (worker_.joinable()) {
    if (!workerFinished_) {
      return;
    }
    worker_.join();
  }
  //  Each coordinator-requested attempt is a new peer/session generation;
  //  rate-limit history must never carry across a reconnect.
  inboundMessageTimes_.clear();
  workerFinished_ = false;
  worker_ = std::thread([this] { RunLoop(); });
}

void AdapterIpcConnection::ConfigureTarget(AdapterIpcTarget target) {
  std::lock_guard<std::mutex> lock(targetMutex_);
  target_ = std::move(target);
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
  try {
    RunAttempt();
  } catch (...) {
    //  No transport, codec, or callback failure may escape the worker thread.
    try {
      socket_.Close();
    } catch (...) {
    }
    ClearOutbound();
    MarkWorkerFinished();
    InvokeContained(callbacks_.onAttemptFinished,
                    inProgressAttemptTargetGeneration_,
                    AdapterIpcAttemptOutcome::kConnectFailed);
  }
}

void AdapterIpcConnection::RunAttempt() {
  auto finish = [this](std::uint64_t targetGeneration,
                       AdapterIpcAttemptOutcome outcome) {
    MarkWorkerFinished();
    InvokeContained(callbacks_.onAttemptFinished, targetGeneration, outcome);
  };

  bool connected = false;
  bool authenticated = false;

  std::uint64_t targetGeneration = 0;
  std::optional<AdapterIpcTarget> attemptTarget;
  {
    std::lock_guard<std::mutex> lock(targetMutex_);
    //  The generation is recorded into the member first, before the
    //  snapshot copy below that can throw (`std::vector` proof-token
    //  allocation), so `RunLoop`'s outer failure boundary can still report
    //  the correct generation if that copy fails. Both reads share this one
    //  lock scope so the generation and the snapshot it came from can never
    //  diverge under a concurrent `ConfigureTarget` call.
    targetGeneration = target_.has_value() ? target_->targetGeneration : 0;
    inProgressAttemptTargetGeneration_ = targetGeneration;
    attemptTarget = target_;
  }

  {
    std::lock_guard<std::mutex> lock(stopMutex_);
    if (stopping_) {
      finish(targetGeneration, AdapterIpcAttemptOutcome::kStopped);
      return;
    }
  }

  try {
    if (attemptTarget.has_value()) {
      socket_.SetPort(attemptTarget->port);
    }

    bool connectedAttempt = socket_.Connect();
    if (!connectedAttempt) {
      ClearOutbound();
      bool stopped = false;
      {
        std::lock_guard<std::mutex> lock(stopMutex_);
        stopped = stopping_;
      }
      if (!stopped) {
        InvokeContained(callbacks_.onConnectionAttemptFailed);
      }
      finish(targetGeneration, stopped
                                   ? AdapterIpcAttemptOutcome::kStopped
                                   : AdapterIpcAttemptOutcome::kConnectFailed);
      return;
    }

    ClearOutbound();
    connected = true;
    const auto establishmentDeadline =
        std::chrono::steady_clock::now() + establishmentTimeout_;

    if (attemptTarget.has_value() && callbacks_.onTargetConnected) {
      InvokeContained(callbacks_.onTargetConnected, *attemptTarget);
    }
    InvokeContained(callbacks_.onConnected);
    authenticated = ServeConnection(establishmentDeadline);
  } catch (...) {
    try {
      socket_.Close();
    } catch (...) {
    }
    ClearOutbound();
    if (!connected) {
      bool stopped = false;
      {
        std::lock_guard<std::mutex> lock(stopMutex_);
        stopped = stopping_;
      }
      if (!stopped) {
        InvokeContained(callbacks_.onConnectionAttemptFailed);
      }
    }
  }

  if (connected) {
    try {
      //  A handler invoked from ServeConnection may have queued one last
      //  response that no later read poll will drain.
      DrainOutbound();
    } catch (...) {
    }
    try {
      socket_.Close();
    } catch (...) {
    }
    ClearOutbound();
    InvokeContained(callbacks_.onDisconnected);
  }

  bool stopped = false;
  {
    std::lock_guard<std::mutex> lock(stopMutex_);
    stopped = stopping_;
  }
  finish(targetGeneration,
         stopped     ? AdapterIpcAttemptOutcome::kStopped
         : connected ? authenticated
                           ? AdapterIpcAttemptOutcome::kDisconnected
                           : AdapterIpcAttemptOutcome::kAuthenticationFailed
                     : AdapterIpcAttemptOutcome::kConnectFailed);
}

bool AdapterIpcConnection::ServeConnection(
    std::chrono::steady_clock::time_point establishmentDeadline) {
  bool authenticated = false;

  while (true) {
    {
      std::lock_guard<std::mutex> lock(stopMutex_);
      if (stopping_) {
        return authenticated;
      }
    }

    if (!authenticated &&
        std::chrono::steady_clock::now() >= establishmentDeadline) {
      return false;
    }

    bool decodeFailed = false;
    std::optional<IpcMessage> message = ReadOneMessage(
        decodeFailed,
        authenticated ? std::nullopt
                      : std::optional<std::chrono::steady_clock::time_point>{
                            establishmentDeadline});
    if (decodeFailed) {
      InvokeContained(callbacks_.onDecodeFailure);
      return authenticated;
    }
    if (!message.has_value()) {
      //  Disconnected, or a stop was requested while waiting for a frame.
      return authenticated;
    }

    AdapterIpcMessageDisposition disposition = DispatchInboundMessage(*message);
    if (disposition == AdapterIpcMessageDisposition::kClose) {
      return authenticated;
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

  if (!TryAcceptInboundMessage()) {
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

bool AdapterIpcConnection::TryAcceptInboundMessage() {
  const auto now = clockNow_();
  const auto windowStart = now - std::chrono::seconds(1);
  while (!inboundMessageTimes_.empty() &&
         inboundMessageTimes_.front() < windowStart) {
    inboundMessageTimes_.pop_front();
  }

  if (inboundMessageTimes_.size() >= kMaxIpcMessagesPerSecond) {
    return false;
  }
  inboundMessageTimes_.push_back(now);
  return true;
}

void AdapterIpcConnection::MarkWorkerFinished() {
  std::lock_guard<std::mutex> lock(lifecycleMutex_);
  workerFinished_ = true;
  lifecycleCondition_.notify_all();
}

} //  namespace dovahlink::adapter::ipc
