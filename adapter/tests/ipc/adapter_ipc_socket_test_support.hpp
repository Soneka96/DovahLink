#pragma once

#include "ipc/winsock_adapter_ipc_socket.hpp"

#include <algorithm>
#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <deque>
#include <future>
#include <mutex>
#include <optional>
#include <span>
#include <stdexcept>
#include <vector>

//  Test-only helpers shared across adapter/ipc/ test files.
namespace dovahlink::adapter::ipc::test_support {

///  A deterministic, fully controllable `IAdapterIpcSocket` test double.
///  `TryReadSome` blocks on a condition variable rather than a real socket
///  poll, so tests control exactly when data, a disconnect, or a stop
///  becomes visible without relying on timing.
class FakeAdapterIpcSocket final : public IAdapterIpcSocket {
public:
  ///  Configures the sequence of `Connect` results; the last entry repeats
  ///  once exhausted. An empty sequence (the default) always succeeds.
  void SetConnectResults(std::vector<bool> results) {
    std::lock_guard<std::mutex> lock(mutex_);
    connectResults_ = std::move(results);
  }

  ///  Makes the next connect attempt signal `attempted` and then throw.
  void SetThrowOnConnect(std::promise<void> &attempted) {
    std::lock_guard<std::mutex> lock(mutex_);
    throwOnConnect_ = true;
    connectAttemptedPromise_ = &attempted;
  }

  ///  Signals `failed` after the next connect attempt returns false.
  void SetConnectFailureSignal(std::promise<void> &failed) {
    std::lock_guard<std::mutex> lock(mutex_);
    connectFailurePromise_ = &failed;
  }

  ///  Signals `completed` once the next `WriteAll` call succeeds, so a test
  ///  driving `Verify`/`Connect`/`WriteAll` on a background thread can wait
  ///  for the outbound Hello to land before reacting, without a timing
  ///  sleep.
  void SetWriteCompletedSignal(std::promise<void> &completed) {
    std::lock_guard<std::mutex> lock(mutex_);
    writeCompletedPromise_ = &completed;
  }

  ///  Limits each `TryReadSome` call to at most `bytes`, so tests can drive
  ///  one frame through repeated partial reads.
  void SetMaxReadBytes(std::size_t bytes) {
    std::lock_guard<std::mutex> lock(mutex_);
    maxReadBytes_ = bytes;
  }

  ///  The number of times `Connect` has been called so far.
  int ConnectCallCount() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return connectCallCount_;
  }

  ///  Appends bytes for `TryReadSome` to serve, waking any blocked reader.
  void PushReadableBytes(const std::vector<std::byte> &bytes) {
    {
      std::lock_guard<std::mutex> lock(mutex_);
      readableBytes_.insert(readableBytes_.end(), bytes.begin(), bytes.end());
    }
    readCondition_.notify_all();
  }

  ///  Makes the current and every future `TryReadSome` call fail, as if the
  ///  peer disconnected, until the next successful `Connect` clears it.
  void SimulateDisconnect() {
    {
      std::lock_guard<std::mutex> lock(mutex_);
      disconnected_ = true;
    }
    readCondition_.notify_all();
  }

  ///  Every byte handed to `WriteAll` so far, in order.
  std::vector<std::byte> WrittenBytes() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return writtenBytes_;
  }

  ///  Discards every byte recorded by `WrittenBytes` so far, so a test
  ///  driving more than one write/verify round can inspect each round's
  ///  outbound frame in isolation.
  void ClearWrittenBytes() {
    std::lock_guard<std::mutex> lock(mutex_);
    writtenBytes_.clear();
  }

  ///  @copydoc IAdapterIpcSocket::Connect
  bool Connect() override {
    std::lock_guard<std::mutex> lock(mutex_);
    ++connectCallCount_;
    if (throwOnConnect_) {
      connectAttemptedPromise_->set_value();
      throw std::runtime_error("Connect failed unexpectedly");
    }
    bool result = true;
    if (!connectResults_.empty()) {
      std::size_t index = connectCallIndex_ < connectResults_.size()
                              ? connectCallIndex_
                              : connectResults_.size() - 1;
      result = connectResults_[index];
      if (connectCallIndex_ + 1 < connectResults_.size()) {
        ++connectCallIndex_;
      }
    }
    if (result) {
      disconnected_ = false;
    } else if (connectFailurePromise_ != nullptr) {
      connectFailurePromise_->set_value();
      connectFailurePromise_ = nullptr;
    }
    return result;
  }

  ///  @copydoc IAdapterIpcSocket::TryReadSome
  std::optional<std::size_t> TryReadSome(std::span<std::byte> buffer) override {
    std::unique_lock<std::mutex> lock(mutex_);
    //  Mirrors the real socket's bounded single-poll semantics: return
    //  promptly with zero bytes when nothing is ready yet, rather than
    //  blocking forever, so a caller polling in a loop (as
    //  AdapterIpcConnection does) can still interleave other work.
    bool ready =
        readCondition_.wait_for(lock, std::chrono::milliseconds(20), [&] {
          return !readableBytes_.empty() || disconnected_ || stopRequested_;
        });
    if (!ready) {
      return std::size_t{0};
    }
    if (readableBytes_.empty()) {
      return std::nullopt;
    }

    std::size_t count =
        std::min({buffer.size(), readableBytes_.size(), maxReadBytes_});
    for (std::size_t i = 0; i < count; ++i) {
      buffer[i] = readableBytes_.front();
      readableBytes_.pop_front();
    }
    return count;
  }

  ///  @copydoc IAdapterIpcSocket::WriteAll
  bool WriteAll(std::span<const std::byte> data) override {
    std::lock_guard<std::mutex> lock(mutex_);
    if (stopRequested_) {
      return false;
    }
    writtenBytes_.insert(writtenBytes_.end(), data.begin(), data.end());
    if (writeCompletedPromise_ != nullptr) {
      writeCompletedPromise_->set_value();
      writeCompletedPromise_ = nullptr;
    }
    return true;
  }

  ///  @copydoc IAdapterIpcSocket::Close
  void Close() override {
    std::lock_guard<std::mutex> lock(mutex_);
    ++closeCallCount_;
  }

  ///  The number of times `Close` has been called so far.
  int CloseCallCount() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return closeCallCount_;
  }

  ///  @copydoc IAdapterIpcSocket::RequestStop
  void RequestStop() override {
    {
      std::lock_guard<std::mutex> lock(mutex_);
      stopRequested_ = true;
    }
    readCondition_.notify_all();
  }

private:
  ///  Guards every other field.
  mutable std::mutex mutex_;
  ///  Wakes a blocked `TryReadSome` when readable bytes, a disconnect, or a
  ///  stop becomes available.
  std::condition_variable readCondition_;
  ///  Bytes queued by `PushReadableBytes`, consumed in order by
  ///  `TryReadSome`.
  std::deque<std::byte> readableBytes_;
  ///  Set by `SimulateDisconnect`; cleared by the next successful `Connect`.
  bool disconnected_ = false;
  ///  Set by `RequestStop`.
  bool stopRequested_ = false;
  ///  The configured `Connect` result sequence; empty means always succeed.
  std::vector<bool> connectResults_;
  ///  Whether the next `Connect` call must throw instead of returning.
  bool throwOnConnect_ = false;
  ///  Signaled, then cleared, the next time `Connect` throws.
  std::promise<void> *connectAttemptedPromise_ = nullptr;
  ///  Signaled, then cleared, the next time `Connect` returns `false`.
  std::promise<void> *connectFailurePromise_ = nullptr;
  ///  Signaled, then cleared, the next time `WriteAll` succeeds.
  std::promise<void> *writeCompletedPromise_ = nullptr;
  ///  The index into `connectResults_` the next `Connect` call will consume.
  std::size_t connectCallIndex_ = 0;
  ///  The number of times `Connect` has been called so far.
  int connectCallCount_ = 0;
  ///  The number of times `Close` has been called so far.
  int closeCallCount_ = 0;
  ///  Every byte handed to `WriteAll` so far, in order.
  std::vector<std::byte> writtenBytes_;
  ///  Maximum bytes returned by one `TryReadSome` call.
  std::size_t maxReadBytes_ = static_cast<std::size_t>(-1);
};

} //  namespace dovahlink::adapter::ipc::test_support
