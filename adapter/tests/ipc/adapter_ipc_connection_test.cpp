#include "ipc/adapter_ipc_connection.hpp"

#include "ipc/adapter_ipc_socket_test_support.hpp"
#include "ipc/ipc_constants.hpp"
#include "ipc/ipc_frame_codec.hpp"
#include "ipc/winsock_adapter_ipc_socket.hpp"
#include "test_support/source_text_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <algorithm>
#include <atomic>
#include <charconv>
#include <chrono>
#include <cstdint>
#include <cstdlib>
#include <future>
#include <new>
#include <optional>
#include <stdexcept>
#include <string>
#include <string_view>
#include <thread>
#include <vector>

using dovahlink::adapter::ipc::AdapterIpcAttemptOutcome;
using dovahlink::adapter::ipc::AdapterIpcConnection;
using dovahlink::adapter::ipc::AdapterIpcConnectionCallbacks;
using dovahlink::adapter::ipc::AdapterIpcMessageDisposition;
using dovahlink::adapter::ipc::AdapterIpcTarget;
using dovahlink::adapter::ipc::IAdapterIpcSocket;
using dovahlink::adapter::ipc::IpcCancelMessage;
using dovahlink::adapter::ipc::IpcCloseMessage;
using dovahlink::adapter::ipc::IpcCloseReason;
using dovahlink::adapter::ipc::IpcFrameCodec;
using dovahlink::adapter::ipc::IpcHelloAckMessage;
using dovahlink::adapter::ipc::IpcHelloMessage;
using dovahlink::adapter::ipc::IpcHelloRejectReason;
using dovahlink::adapter::ipc::IpcMessage;
using dovahlink::adapter::ipc::IpcResynchronizeRequestMessage;
using dovahlink::adapter::ipc::kMaxIpcMessagesPerSecond;
using dovahlink::adapter::ipc::kMaxIpcQueuedMessages;
using dovahlink::adapter::ipc::test_support::FakeAdapterIpcSocket;
using dovahlink::adapter::test_support::ReadSource;

namespace {

///  Waits for `future` up to a generous bound and returns whether it became
///  ready, instead of hanging the test suite if production code regresses.
template <typename T> bool WaitReady(std::future<T> &future) {
  return future.wait_for(std::chrono::seconds(5)) == std::future_status::ready;
}

///  Waits for an asynchronous test condition without allowing a regression to
///  hang the test process indefinitely.
template <typename Predicate>
bool WaitUntil(Predicate predicate,
               std::chrono::milliseconds timeout = std::chrono::seconds(5)) {
  const auto deadline = std::chrono::steady_clock::now() + timeout;
  while (std::chrono::steady_clock::now() < deadline) {
    if (predicate()) {
      return true;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(10));
  }
  return predicate();
}

///  Reads one integer field from the checked-in host/adapter private-IPC
///  contract fixture.
std::size_t ReadPrivateIpcLimit(std::string_view key) {
  const std::string source = ReadSource(DOVAHLINK_PRIVATE_IPC_LIMIT_FIXTURE);
  const std::string marker = "\"" + std::string(key) + "\"";
  const std::size_t keyPosition = source.find(marker);
  REQUIRE(keyPosition != std::string::npos);
  const std::size_t colon = source.find(':', keyPosition + marker.size());
  REQUIRE(colon != std::string::npos);
  const std::size_t valuePosition =
      source.find_first_not_of(" \t\r\n", colon + 1);
  REQUIRE(valuePosition != std::string::npos);

  std::size_t value = 0;
  const auto [end, error] = std::from_chars(
      source.data() + valuePosition, source.data() + source.size(), value);
  REQUIRE(error == std::errc{});
  REQUIRE(end != source.data() + valuePosition);
  return value;
}

///  Arms the global allocator override below to fail exactly one allocation
///  of a chosen size with `std::bad_alloc`, deterministically exercising an
///  out-of-memory path without any production-code seam. Every allocation of
///  any other size, on any thread, is unaffected; only one
///  `ScopedAllocationFailure` instance is armed at a time in this test
///  binary.
class ScopedAllocationFailure {
public:
  ///  Arms the trap for the next allocation of exactly `failingSize` bytes.
  explicit ScopedAllocationFailure(std::size_t failingSize) {
    TargetSize() = failingSize;
    Armed() = true;
  }

  ///  Disarms the trap, whether or not it was ever consumed.
  ~ScopedAllocationFailure() { Armed() = false; }

  ScopedAllocationFailure(const ScopedAllocationFailure &) = delete;
  ScopedAllocationFailure &operator=(const ScopedAllocationFailure &) = delete;

  ///  Called from the overridden `operator new`. Consumes the trap the first
  ///  time `size` matches while armed; every other call is unaffected.
  static bool ShouldFail(std::size_t size) {
    if (Armed() && size == TargetSize()) {
      Armed() = false;
      return true;
    }
    return false;
  }

private:
  ///  Whether a matching allocation should currently fail.
  static std::atomic<bool> &Armed() {
    static std::atomic<bool> armed{false};
    return armed;
  }
  ///  The allocation size, in bytes, currently armed to fail.
  static std::atomic<std::size_t> &TargetSize() {
    static std::atomic<std::size_t> targetSize{0};
    return targetSize;
  }
};

} //  namespace

///  Replaces the global allocator for this entire test binary so
///  `ScopedAllocationFailure` can inject a deterministic `std::bad_alloc` at
///  an exact allocation size. Every allocation not matched by an armed
///  `ScopedAllocationFailure` behaves exactly like the default allocator.
void *operator new(std::size_t size) {
  if (ScopedAllocationFailure::ShouldFail(size)) {
    throw std::bad_alloc();
  }
  void *pointer = std::malloc(size == 0 ? 1 : size);
  if (pointer == nullptr) {
    throw std::bad_alloc();
  }
  return pointer;
}

///  Frees memory obtained from the `operator new` override above.
void operator delete(void *pointer) noexcept { std::free(pointer); }

///  @copydoc operator delete(void*)
void operator delete(void *pointer, std::size_t) noexcept {
  std::free(pointer);
}

TEST_CASE("AdapterIpcConnection enforces the shared inbound message-rate "
          "limit before dispatch") {
  const std::size_t maxMessages = ReadPrivateIpcLimit("maxMessagesPerSecond");
  CHECK(maxMessages == kMaxIpcMessagesPerSecond);

  FakeAdapterIpcSocket socket;
  IpcFrameCodec codec;
  std::atomic<std::size_t> delivered = 0;
  std::promise<AdapterIpcAttemptOutcome> outcomePromise;
  AdapterIpcConnectionCallbacks callbacks{
      .onMessageReceived =
          [&](const IpcMessage &) {
            const std::size_t messageNumber = delivered.fetch_add(1) + 1;
            return messageNumber == 1
                       ? AdapterIpcMessageDisposition::kAuthenticated
                       : AdapterIpcMessageDisposition::kContinue;
          },
      .onAttemptFinished =
          [&](std::uint64_t, AdapterIpcAttemptOutcome outcome) {
            outcomePromise.set_value(outcome);
          },
  };
  std::atomic<std::int64_t> nowMilliseconds = 0;
  AdapterIpcConnection connection(
      socket, codec, std::move(callbacks), std::chrono::seconds(5), [&] {
        return std::chrono::steady_clock::time_point{
            std::chrono::milliseconds(nowMilliseconds.load())};
      });
  connection.Start();

  const std::vector<std::byte> helloAck = codec.Encode(IpcMessage{
      IpcHelloAckMessage{.correlationId = 1,
                         .accepted = true,
                         .rejectReason = IpcHelloRejectReason::kNone}});
  const std::vector<std::byte> message = codec.Encode(IpcMessage{
      IpcCloseMessage{.correlationId = 0, .reason = IpcCloseReason::kNormal}});
  std::vector<std::byte> inbound = helloAck;
  for (std::size_t index = 1; index < maxMessages + 1; ++index) {
    inbound.insert(inbound.end(), message.begin(), message.end());
  }
  socket.PushReadableBytes(inbound);

  auto outcomeFuture = outcomePromise.get_future();
  REQUIRE(WaitReady(outcomeFuture));
  CHECK(outcomeFuture.get() == AdapterIpcAttemptOutcome::kDisconnected);
  CHECK(delivered.load() == maxMessages);

  connection.Stop();
}

TEST_CASE("AdapterIpcConnection resets inbound rate history for a reconnect") {
  const std::size_t maxMessages = ReadPrivateIpcLimit("maxMessagesPerSecond");

  FakeAdapterIpcSocket socket;
  IpcFrameCodec codec;
  std::atomic<std::size_t> delivered = 0;
  std::atomic<int> finishedAttempts = 0;
  std::promise<void> firstFinishedPromise;
  std::promise<void> secondFinishedPromise;
  AdapterIpcConnectionCallbacks callbacks{
      .onMessageReceived =
          [&](const IpcMessage &message) {
            ++delivered;
            return std::holds_alternative<IpcHelloAckMessage>(message)
                       ? AdapterIpcMessageDisposition::kAuthenticated
                       : AdapterIpcMessageDisposition::kContinue;
          },
      .onAttemptFinished =
          [&](std::uint64_t, AdapterIpcAttemptOutcome) {
            if (finishedAttempts.fetch_add(1) == 0) {
              firstFinishedPromise.set_value();
            } else {
              secondFinishedPromise.set_value();
            }
          },
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks));
  connection.Start();

  const std::vector<std::byte> helloAck = codec.Encode(IpcMessage{
      IpcHelloAckMessage{.correlationId = 1,
                         .accepted = true,
                         .rejectReason = IpcHelloRejectReason::kNone}});
  const std::vector<std::byte> message = codec.Encode(IpcMessage{
      IpcCloseMessage{.correlationId = 0, .reason = IpcCloseReason::kNormal}});
  std::vector<std::byte> firstInbound = helloAck;
  for (std::size_t index = 1; index < maxMessages; ++index) {
    firstInbound.insert(firstInbound.end(), message.begin(), message.end());
  }
  socket.PushReadableBytes(firstInbound);
  REQUIRE(WaitUntil([&] { return delivered.load() == maxMessages; }));

  socket.SimulateDisconnect();
  auto firstFinished = firstFinishedPromise.get_future();
  REQUIRE(WaitReady(firstFinished));

  connection.Start();
  socket.PushReadableBytes(helloAck);
  REQUIRE(WaitUntil([&] { return delivered.load() == maxMessages + 1; }));

  socket.SimulateDisconnect();
  auto secondFinished = secondFinishedPromise.get_future();
  REQUIRE(WaitReady(secondFinished));
  connection.Stop();
}

TEST_CASE("AdapterIpcConnection allows inbound messages after the shared rate "
          "window expires") {
  const std::size_t maxMessages = ReadPrivateIpcLimit("maxMessagesPerSecond");
  const std::size_t windowMilliseconds =
      ReadPrivateIpcLimit("messageRateWindowMilliseconds");

  FakeAdapterIpcSocket socket;
  IpcFrameCodec codec;
  std::atomic<std::size_t> delivered = 0;
  std::promise<void> limitReachedPromise;
  std::promise<AdapterIpcAttemptOutcome> outcomePromise;
  AdapterIpcConnectionCallbacks callbacks{
      .onMessageReceived =
          [&](const IpcMessage &) {
            const std::size_t messageNumber = delivered.fetch_add(1) + 1;
            if (messageNumber == maxMessages) {
              limitReachedPromise.set_value();
            }
            return messageNumber == 1
                       ? AdapterIpcMessageDisposition::kAuthenticated
                       : AdapterIpcMessageDisposition::kContinue;
          },
      .onAttemptFinished =
          [&](std::uint64_t, AdapterIpcAttemptOutcome outcome) {
            outcomePromise.set_value(outcome);
          },
  };
  std::atomic<std::int64_t> nowMilliseconds = 0;
  AdapterIpcConnection connection(
      socket, codec, std::move(callbacks), std::chrono::seconds(5), [&] {
        return std::chrono::steady_clock::time_point{
            std::chrono::milliseconds(nowMilliseconds.load())};
      });
  connection.Start();

  const std::vector<std::byte> helloAck = codec.Encode(IpcMessage{
      IpcHelloAckMessage{.correlationId = 1,
                         .accepted = true,
                         .rejectReason = IpcHelloRejectReason::kNone}});
  const std::vector<std::byte> message = codec.Encode(IpcMessage{
      IpcCloseMessage{.correlationId = 0, .reason = IpcCloseReason::kNormal}});
  std::vector<std::byte> inbound = helloAck;
  for (std::size_t index = 1; index < maxMessages; ++index) {
    inbound.insert(inbound.end(), message.begin(), message.end());
  }
  socket.PushReadableBytes(inbound);
  auto limitReachedFuture = limitReachedPromise.get_future();
  REQUIRE(WaitReady(limitReachedFuture));

  nowMilliseconds.store(static_cast<std::int64_t>(windowMilliseconds + 1));
  socket.PushReadableBytes(message);
  REQUIRE(WaitUntil([&] { return delivered.load() == maxMessages + 1; }));

  socket.SimulateDisconnect();
  auto outcomeFuture = outcomePromise.get_future();
  REQUIRE(WaitReady(outcomeFuture));
  CHECK(outcomeFuture.get() == AdapterIpcAttemptOutcome::kDisconnected);
  connection.Stop();
}

TEST_CASE("AdapterIpcConnection keeps the exact shared rate-window boundary "
          "limited") {
  const std::size_t maxMessages = ReadPrivateIpcLimit("maxMessagesPerSecond");
  const std::size_t windowMilliseconds =
      ReadPrivateIpcLimit("messageRateWindowMilliseconds");

  FakeAdapterIpcSocket socket;
  IpcFrameCodec codec;
  std::atomic<std::size_t> delivered = 0;
  std::promise<AdapterIpcAttemptOutcome> outcomePromise;
  AdapterIpcConnectionCallbacks callbacks{
      .onMessageReceived =
          [&](const IpcMessage &) {
            const std::size_t messageNumber = delivered.fetch_add(1) + 1;
            return messageNumber == 1
                       ? AdapterIpcMessageDisposition::kAuthenticated
                       : AdapterIpcMessageDisposition::kContinue;
          },
      .onAttemptFinished =
          [&](std::uint64_t, AdapterIpcAttemptOutcome outcome) {
            outcomePromise.set_value(outcome);
          },
  };
  std::atomic<std::int64_t> nowMilliseconds = 0;
  AdapterIpcConnection connection(
      socket, codec, std::move(callbacks), std::chrono::seconds(5), [&] {
        return std::chrono::steady_clock::time_point{
            std::chrono::milliseconds(nowMilliseconds.load())};
      });
  connection.Start();

  const std::vector<std::byte> helloAck = codec.Encode(IpcMessage{
      IpcHelloAckMessage{.correlationId = 1,
                         .accepted = true,
                         .rejectReason = IpcHelloRejectReason::kNone}});
  const std::vector<std::byte> message = codec.Encode(IpcMessage{
      IpcCloseMessage{.correlationId = 0, .reason = IpcCloseReason::kNormal}});
  std::vector<std::byte> inbound = helloAck;
  for (std::size_t index = 1; index < maxMessages; ++index) {
    inbound.insert(inbound.end(), message.begin(), message.end());
  }
  socket.PushReadableBytes(inbound);
  REQUIRE(WaitUntil([&] { return delivered.load() == maxMessages; }));

  nowMilliseconds.store(static_cast<std::int64_t>(windowMilliseconds));
  socket.PushReadableBytes(message);
  auto outcomeFuture = outcomePromise.get_future();
  REQUIRE(WaitReady(outcomeFuture));
  CHECK(outcomeFuture.get() == AdapterIpcAttemptOutcome::kDisconnected);
  CHECK(delivered.load() == maxMessages);
  connection.Stop();
}

TEST_CASE("AdapterIpcConnection passes the configured target snapshot to the "
          "target-connected callback and socket") {
  FakeAdapterIpcSocket socket;
  IpcFrameCodec codec;
  AdapterIpcTarget expected{
      .port = 12345,
      .proofToken = {std::byte{9}, std::byte{8}},
      .targetGeneration = 4,
  };
  std::promise<AdapterIpcTarget> targetPromise;
  std::promise<void> connectedPromise;

  AdapterIpcConnectionCallbacks callbacks{
      .onTargetConnected =
          [&](const AdapterIpcTarget &target) {
            targetPromise.set_value(target);
          },
      .onConnected = [&] { connectedPromise.set_value(); },
      .onMessageReceived =
          [](const IpcMessage &) {
            return AdapterIpcMessageDisposition::kContinue;
          },
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks));
  connection.ConfigureTarget(expected);
  connection.Start();

  auto targetFuture = targetPromise.get_future();
  REQUIRE(WaitReady(targetFuture));
  CHECK(targetFuture.get() == expected);
  auto connectedFuture = connectedPromise.get_future();
  REQUIRE(WaitReady(connectedFuture));
  CHECK(socket.Port() == expected.port);

  connection.Stop();
}

TEST_CASE("AdapterIpcConnection uses the target snapshot captured at attempt "
          "start") {
  FakeAdapterIpcSocket socket;
  IpcFrameCodec codec;
  AdapterIpcTarget firstTarget{
      .port = 12345,
      .proofToken = {std::byte{1}},
      .targetGeneration = 1,
  };
  AdapterIpcTarget secondTarget{
      .port = 23456,
      .proofToken = {std::byte{2}},
      .targetGeneration = 2,
  };
  std::promise<void> connectEnteredPromise;
  std::shared_future<void> connectEnteredFuture =
      connectEnteredPromise.get_future().share();
  std::promise<void> connectReleasePromise;
  std::shared_future<void> connectReleaseFuture =
      connectReleasePromise.get_future().share();
  socket.BlockNextConnectUntilReleased(connectEnteredPromise,
                                       connectReleaseFuture);

  std::promise<AdapterIpcTarget> firstTargetPromise;
  std::promise<AdapterIpcTarget> secondTargetPromise;
  std::promise<void> firstAttemptFinishedPromise;
  std::atomic<int> targetConnectedCount = 0;
  AdapterIpcConnectionCallbacks callbacks{
      .onTargetConnected =
          [&](const AdapterIpcTarget &target) {
            if (targetConnectedCount.fetch_add(1) == 0) {
              firstTargetPromise.set_value(target);
            } else {
              secondTargetPromise.set_value(target);
            }
          },
      .onMessageReceived =
          [](const IpcMessage &) {
            return AdapterIpcMessageDisposition::kContinue;
          },
      .onAttemptFinished =
          [&](std::uint64_t generation, AdapterIpcAttemptOutcome outcome) {
            if (generation == firstTarget.targetGeneration &&
                outcome != AdapterIpcAttemptOutcome::kStopped) {
              firstAttemptFinishedPromise.set_value();
            }
          },
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks));
  connection.ConfigureTarget(firstTarget);
  connection.Start();

  REQUIRE(connectEnteredFuture.wait_for(std::chrono::seconds(5)) ==
          std::future_status::ready);
  connection.ConfigureTarget(secondTarget);
  connectReleasePromise.set_value();

  auto firstTargetFuture = firstTargetPromise.get_future();
  REQUIRE(WaitReady(firstTargetFuture));
  CHECK(firstTargetFuture.get() == firstTarget);
  CHECK(socket.Port() == firstTarget.port);

  socket.SimulateDisconnect();
  auto firstAttemptFinishedFuture = firstAttemptFinishedPromise.get_future();
  REQUIRE(WaitReady(firstAttemptFinishedFuture));
  connection.Start();
  auto secondTargetFuture = secondTargetPromise.get_future();
  REQUIRE(WaitReady(secondTargetFuture));
  CHECK(secondTargetFuture.get() == secondTarget);
  CHECK(socket.Port() == secondTarget.port);

  connection.Stop();
}

TEST_CASE("AdapterIpcConnection connects, reports onConnected, and delivers "
          "a decoded inbound message") {
  FakeAdapterIpcSocket socket;
  IpcFrameCodec codec;
  std::promise<void> connectedPromise;
  std::promise<IpcMessage> receivedPromise;
  std::promise<void> disconnectedPromise;

  AdapterIpcConnectionCallbacks callbacks{
      .onConnected = [&] { connectedPromise.set_value(); },
      .onMessageReceived =
          [&](const IpcMessage &message) {
            receivedPromise.set_value(message);
            return AdapterIpcMessageDisposition::kAuthenticated;
          },
      .onDecodeFailure = [] {},
      .onDisconnected = [&] { disconnectedPromise.set_value(); },
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks));
  connection.Start();

  auto connectedFuture = connectedPromise.get_future();
  REQUIRE(WaitReady(connectedFuture));

  IpcMessage sent{IpcResynchronizeRequestMessage{.correlationId = 5}};
  socket.PushReadableBytes(codec.Encode(sent));

  auto receivedFuture = receivedPromise.get_future();
  REQUIRE(WaitReady(receivedFuture));
  CHECK(receivedFuture.get() == sent);

  socket.SimulateDisconnect();
  auto disconnectedFuture = disconnectedPromise.get_future();
  REQUIRE(WaitReady(disconnectedFuture));

  connection.Stop();
}

TEST_CASE("AdapterIpcConnection disconnects when HelloAck establishment times "
          "out") {
  FakeAdapterIpcSocket socket;
  socket.SetConnectResults({true, false});
  IpcFrameCodec codec;
  std::promise<void> connectedPromise;
  std::promise<void> disconnectedPromise;
  std::atomic<int> disconnectedCount{0};

  AdapterIpcConnectionCallbacks callbacks{
      .onConnected = [&] { connectedPromise.set_value(); },
      .onMessageReceived =
          [](const IpcMessage &) {
            return AdapterIpcMessageDisposition::kContinue;
          },
      .onDisconnected =
          [&] {
            ++disconnectedCount;
            disconnectedPromise.set_value();
          },
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks),
                                  std::chrono::milliseconds(40));
  connection.Start();

  auto connectedFuture = connectedPromise.get_future();
  REQUIRE(WaitReady(connectedFuture));
  auto timeoutStarted = std::chrono::steady_clock::now();
  auto disconnectedFuture = disconnectedPromise.get_future();
  REQUIRE(WaitReady(disconnectedFuture));
  CHECK(std::chrono::steady_clock::now() - timeoutStarted <
        std::chrono::seconds(1));
  CHECK(disconnectedCount.load() == 1);

  connection.Stop();
}

TEST_CASE("AdapterIpcConnection remains established after an authenticated "
          "HelloAck") {
  FakeAdapterIpcSocket socket;
  IpcFrameCodec codec;
  std::promise<void> connectedPromise;
  std::promise<void> authenticatedPromise;
  std::atomic<int> disconnectedCount{0};

  AdapterIpcConnectionCallbacks callbacks{
      .onConnected = [&] { connectedPromise.set_value(); },
      .onMessageReceived =
          [&](const IpcMessage &message) {
            if (std::holds_alternative<IpcHelloAckMessage>(message)) {
              authenticatedPromise.set_value();
              return AdapterIpcMessageDisposition::kAuthenticated;
            }
            return AdapterIpcMessageDisposition::kContinue;
          },
      .onDisconnected = [&] { ++disconnectedCount; },
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks),
                                  std::chrono::milliseconds(100));
  connection.Start();

  auto connectedFuture = connectedPromise.get_future();
  REQUIRE(WaitReady(connectedFuture));
  socket.PushReadableBytes(codec.Encode(IpcMessage{
      IpcHelloAckMessage{.correlationId = 1,
                         .accepted = true,
                         .rejectReason = IpcHelloRejectReason::kNone}}));

  auto authenticatedFuture = authenticatedPromise.get_future();
  REQUIRE(WaitReady(authenticatedFuture));
  std::this_thread::sleep_for(std::chrono::milliseconds(150));
  CHECK(disconnectedCount.load() == 0);

  connection.Stop();
}

TEST_CASE("AdapterIpcConnection closes after a rejected HelloAck") {
  FakeAdapterIpcSocket socket;
  socket.SetConnectResults({true, false});
  IpcFrameCodec codec;
  std::promise<void> connectedPromise;
  std::promise<void> disconnectedPromise;

  AdapterIpcConnectionCallbacks callbacks{
      .onConnected = [&] { connectedPromise.set_value(); },
      .onMessageReceived =
          [](const IpcMessage &message) {
            if (std::holds_alternative<IpcHelloAckMessage>(message)) {
              return AdapterIpcMessageDisposition::kClose;
            }
            return AdapterIpcMessageDisposition::kContinue;
          },
      .onDisconnected = [&] { disconnectedPromise.set_value(); },
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks),
                                  std::chrono::milliseconds(100));
  connection.Start();

  auto connectedFuture = connectedPromise.get_future();
  REQUIRE(WaitReady(connectedFuture));
  socket.PushReadableBytes(codec.Encode(IpcMessage{IpcHelloAckMessage{
      .correlationId = 1,
      .accepted = false,
      .rejectReason = IpcHelloRejectReason::kInvalidProof}}));

  auto disconnectedFuture = disconnectedPromise.get_future();
  REQUIRE(WaitReady(disconnectedFuture));
  connection.Stop();
}

TEST_CASE("AdapterIpcConnection does not extend establishment deadline for "
          "partial inbound reads") {
  FakeAdapterIpcSocket socket;
  socket.SetConnectResults({true, false});
  socket.SetMaxReadBytes(1);
  IpcFrameCodec codec;
  std::promise<void> connectedPromise;
  std::promise<void> disconnectedPromise;
  std::atomic<int> receivedCount{0};

  AdapterIpcConnectionCallbacks callbacks{
      .onConnected = [&] { connectedPromise.set_value(); },
      .onMessageReceived =
          [&](const IpcMessage &) {
            ++receivedCount;
            return AdapterIpcMessageDisposition::kContinue;
          },
      .onDisconnected = [&] { disconnectedPromise.set_value(); },
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks),
                                  std::chrono::milliseconds(75));
  connection.Start();

  auto connectedFuture = connectedPromise.get_future();
  REQUIRE(WaitReady(connectedFuture));
  std::vector<std::byte> frame =
      codec.Encode(IpcMessage{IpcCancelMessage{.correlationId = 1}});
  std::thread partialWriter([&socket, frame = std::move(frame)] {
    for (std::byte byte : frame) {
      socket.PushReadableBytes({byte});
      std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }
  });

  auto disconnectedFuture = disconnectedPromise.get_future();
  auto timeoutStarted = std::chrono::steady_clock::now();
  REQUIRE(WaitReady(disconnectedFuture));
  partialWriter.join();
  CHECK(std::chrono::steady_clock::now() - timeoutStarted <
        std::chrono::seconds(1));
  CHECK(receivedCount.load() == 0);

  connection.Stop();
}

TEST_CASE("AdapterIpcConnection Stop exits promptly during establishment") {
  FakeAdapterIpcSocket socket;
  IpcFrameCodec codec;
  std::promise<void> connectedPromise;

  AdapterIpcConnectionCallbacks callbacks{
      .onConnected = [&] { connectedPromise.set_value(); },
      .onMessageReceived =
          [](const IpcMessage &) {
            return AdapterIpcMessageDisposition::kContinue;
          },
      .onDisconnected = [] {},
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks),
                                  std::chrono::seconds(10));
  connection.Start();

  auto connectedFuture = connectedPromise.get_future();
  REQUIRE(WaitReady(connectedFuture));
  auto started = std::chrono::steady_clock::now();
  connection.Stop();
  CHECK(std::chrono::steady_clock::now() - started < std::chrono::seconds(1));
}

TEST_CASE("AdapterIpcConnection starts establishment deadline at connect") {
  FakeAdapterIpcSocket socket;
  socket.SetConnectResults({true, false});
  IpcFrameCodec codec;
  std::promise<void> connectedEnteredPromise;
  std::promise<void> releaseConnectedPromise;
  std::shared_future<void> releaseConnectedFuture =
      releaseConnectedPromise.get_future().share();
  std::promise<void> disconnectedPromise;

  AdapterIpcConnectionCallbacks callbacks{
      .onConnected =
          [&] {
            connectedEnteredPromise.set_value();
            releaseConnectedFuture.wait();
          },
      .onMessageReceived =
          [](const IpcMessage &) {
            return AdapterIpcMessageDisposition::kContinue;
          },
      .onDisconnected = [&] { disconnectedPromise.set_value(); },
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks),
                                  std::chrono::milliseconds(50));
  connection.Start();

  auto connectedEnteredFuture = connectedEnteredPromise.get_future();
  REQUIRE(WaitReady(connectedEnteredFuture));
  std::this_thread::sleep_for(std::chrono::milliseconds(100));
  releaseConnectedPromise.set_value();

  auto disconnectedFuture = disconnectedPromise.get_future();
  REQUIRE(WaitReady(disconnectedFuture));
  connection.Stop();
}

TEST_CASE("AdapterIpcConnection::TrySend is written even while no inbound "
          "message ever arrives") {
  FakeAdapterIpcSocket socket;
  IpcFrameCodec codec;
  std::promise<void> connectedPromise;

  AdapterIpcConnectionCallbacks callbacks{
      .onConnected = [&] { connectedPromise.set_value(); },
      .onMessageReceived =
          [](const IpcMessage &) {
            return AdapterIpcMessageDisposition::kAuthenticated;
          },
      .onDecodeFailure = [] {},
      .onDisconnected = [] {},
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks));
  connection.Start();

  auto connectedFuture = connectedPromise.get_future();
  REQUIRE(WaitReady(connectedFuture));

  IpcMessage toSend{
      IpcCloseMessage{.correlationId = 0, .reason = IpcCloseReason::kShutdown}};
  REQUIRE(connection.TrySend(toSend));

  std::vector<std::byte> expectedBytes = codec.Encode(toSend);
  auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
  while (socket.WrittenBytes() != expectedBytes &&
         std::chrono::steady_clock::now() < deadline) {
    std::this_thread::sleep_for(std::chrono::milliseconds(10));
  }
  CHECK(socket.WrittenBytes() == expectedBytes);

  connection.Stop();
}

TEST_CASE("AdapterIpcConnection reports a failed connect attempt without "
          "retrying") {
  FakeAdapterIpcSocket socket;
  socket.SetConnectResults({false, true});
  IpcFrameCodec codec;
  std::promise<void> outcomePromise;
  AdapterIpcAttemptOutcome outcome = AdapterIpcAttemptOutcome::kStopped;
  std::promise<void> secondConnectedPromise;
  std::promise<void> secondOutcomePromise;
  std::atomic<int> outcomeCount{0};

  AdapterIpcConnectionCallbacks callbacks{
      .onConnected = [&] { secondConnectedPromise.set_value(); },
      .onAttemptFinished =
          [&](std::uint64_t generation, AdapterIpcAttemptOutcome result) {
            CHECK(generation == 0);
            if (outcomeCount.fetch_add(1) == 0) {
              outcome = result;
              outcomePromise.set_value();
            } else {
              secondOutcomePromise.set_value();
            }
          },
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks));
  connection.Start();

  auto outcomeFuture = outcomePromise.get_future();
  REQUIRE(WaitReady(outcomeFuture));
  CHECK(outcome == AdapterIpcAttemptOutcome::kConnectFailed);
  CHECK(socket.ConnectCallCount() == 1);

  connection.Start();
  auto secondConnectedFuture = secondConnectedPromise.get_future();
  REQUIRE(WaitReady(secondConnectedFuture));
  socket.SimulateDisconnect();
  auto secondOutcomeFuture = secondOutcomePromise.get_future();
  REQUIRE(WaitReady(secondOutcomeFuture));
  CHECK(socket.ConnectCallCount() == 2);

  connection.Stop();
}

TEST_CASE("AdapterIpcConnection reports one failed connect attempt with its "
          "terminal outcome") {
  FakeAdapterIpcSocket socket;
  socket.SetConnectResults({false, true});
  IpcFrameCodec codec;
  std::promise<void> outcomePromise;
  std::atomic<int> failedAttemptCount{0};
  std::uint64_t generation = 0;
  AdapterIpcAttemptOutcome outcome = AdapterIpcAttemptOutcome::kStopped;

  AdapterIpcConnectionCallbacks callbacks{
      .onConnectionAttemptFailed = [&] { ++failedAttemptCount; },
      .onAttemptFinished =
          [&](std::uint64_t attemptGeneration,
              AdapterIpcAttemptOutcome result) {
            generation = attemptGeneration;
            outcome = result;
            outcomePromise.set_value();
          },
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks));
  connection.Start();

  auto outcomeFuture = outcomePromise.get_future();
  REQUIRE(WaitReady(outcomeFuture));
  CHECK(generation == 0);
  CHECK(outcome == AdapterIpcAttemptOutcome::kConnectFailed);
  CHECK(failedAttemptCount.load() == 1);
  CHECK(socket.ConnectCallCount() == 1);

  connection.Stop();
}

TEST_CASE("AdapterIpcConnection reports a throwing connect attempt as a "
          "failed one-shot attempt") {
  FakeAdapterIpcSocket socket;
  std::promise<void> connectAttemptedPromise;
  socket.SetThrowOnConnect(connectAttemptedPromise);
  IpcFrameCodec codec;
  std::promise<void> failedAttemptPromise;
  std::promise<void> outcomePromise;
  std::atomic<int> failedAttemptCount{0};
  AdapterIpcAttemptOutcome outcome = AdapterIpcAttemptOutcome::kStopped;

  AdapterIpcConnectionCallbacks callbacks{
      .onConnectionAttemptFailed =
          [&] {
            ++failedAttemptCount;
            failedAttemptPromise.set_value();
            throw std::runtime_error("failed-connect callback failed");
          },
      .onAttemptFinished =
          [&](std::uint64_t generation, AdapterIpcAttemptOutcome result) {
            CHECK(generation == 0);
            outcome = result;
            outcomePromise.set_value();
          },
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks));
  connection.Start();

  auto connectAttemptedFuture = connectAttemptedPromise.get_future();
  REQUIRE(WaitReady(connectAttemptedFuture));
  auto failedAttemptFuture = failedAttemptPromise.get_future();
  REQUIRE(WaitReady(failedAttemptFuture));
  auto outcomeFuture = outcomePromise.get_future();
  REQUIRE(WaitReady(outcomeFuture));
  CHECK(outcome == AdapterIpcAttemptOutcome::kConnectFailed);
  CHECK(failedAttemptCount.load() == 1);
  CHECK(socket.ConnectCallCount() == 1);
  CHECK(socket.CloseCallCount() >= 1);

  connection.Stop();
}

TEST_CASE("AdapterIpcConnection contains an exception from the failed-connect "
          "callback without retrying") {
  FakeAdapterIpcSocket socket;
  socket.SetConnectResults({false, true});
  IpcFrameCodec codec;
  std::promise<void> outcomePromise;
  std::atomic<int> failedAttemptCount{0};
  AdapterIpcAttemptOutcome outcome = AdapterIpcAttemptOutcome::kStopped;

  AdapterIpcConnectionCallbacks callbacks{
      .onConnectionAttemptFailed =
          [&] {
            ++failedAttemptCount;
            throw std::runtime_error("failed-connect callback failed");
          },
      .onAttemptFinished =
          [&](std::uint64_t generation, AdapterIpcAttemptOutcome result) {
            CHECK(generation == 0);
            outcome = result;
            outcomePromise.set_value();
          },
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks));
  connection.Start();

  auto outcomeFuture = outcomePromise.get_future();
  REQUIRE(WaitReady(outcomeFuture));
  CHECK(outcome == AdapterIpcAttemptOutcome::kConnectFailed);
  CHECK(failedAttemptCount.load() == 1);
  CHECK(socket.ConnectCallCount() == 1);

  connection.Stop();
}

TEST_CASE("AdapterIpcConnection::Stop completes after a failed connect "
          "attempt") {
  FakeAdapterIpcSocket socket;
  socket.SetConnectResults({false});
  std::promise<void> connectFailurePromise;
  std::future<void> connectFailureFuture = connectFailurePromise.get_future();
  socket.SetConnectFailureSignal(connectFailurePromise);
  IpcFrameCodec codec;

  AdapterIpcConnectionCallbacks callbacks{
      .onConnected = [] {},
      .onMessageReceived =
          [](const IpcMessage &) {
            return AdapterIpcMessageDisposition::kAuthenticated;
          },
      .onDecodeFailure = [] {},
      .onDisconnected = [] {},
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks));
  connection.Start();

  REQUIRE(WaitReady(connectFailureFuture));

  std::promise<void> stopReturnedPromise;
  std::future<void> stopReturnedFuture = stopReturnedPromise.get_future();
  std::thread stopper([&] {
    connection.Stop();
    stopReturnedPromise.set_value();
  });

  bool stopReturned = WaitReady(stopReturnedFuture);
  if (!stopReturned) {
    connection.Stop();
  }
  stopper.join();
  REQUIRE(stopReturned);
}

TEST_CASE("AdapterIpcConnection::Stop can be requested from an inbound "
          "message callback without self-joining") {
  FakeAdapterIpcSocket socket;
  IpcFrameCodec codec;
  std::promise<void> stopReturnedPromise;
  std::atomic<bool> stopThrew{false};
  AdapterIpcConnection *connectionPointer = nullptr;

  AdapterIpcConnectionCallbacks callbacks{
      .onConnected = [] {},
      .onMessageReceived =
          [&](const IpcMessage &) {
            try {
              connectionPointer->Stop();
            } catch (...) {
              stopThrew = true;
            }
            stopReturnedPromise.set_value();
            return AdapterIpcMessageDisposition::kContinue;
          },
      .onDecodeFailure = [] {},
      .onDisconnected = [] {},
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks));
  connectionPointer = &connection;
  connection.Start();

  socket.PushReadableBytes(codec.Encode(
      IpcMessage{IpcResynchronizeRequestMessage{.correlationId = 1}}));

  std::future<void> stopReturnedFuture = stopReturnedPromise.get_future();
  REQUIRE(WaitReady(stopReturnedFuture));
  connection.Stop();
  CHECK_FALSE(stopThrew.load());
}

TEST_CASE("AdapterIpcConnection::Start can be requested from its own worker "
          "without deadlocking against a concurrent external Stop") {
  //  Recognizing the connection's own worker thread must not depend on
  //  reading the shared worker_ object itself: an external Stop() is made
  //  to become the join owner and genuinely block waiting for this worker
  //  to return before the worker's own callback calls Start() reentrantly,
  //  which is exactly the shape that would deadlock -- the external Stop()
  //  waiting for the worker to return, the reentrant Start() waiting on
  //  joinInProgress_, which only the external Stop() could ever clear --
  //  if the self-thread check were missing or itself raced worker_.
  FakeAdapterIpcSocket socket;
  IpcFrameCodec codec;
  std::promise<void> callbackEnteredPromise;
  std::future<void> callbackEnteredFuture = callbackEnteredPromise.get_future();
  std::promise<void> releasePromise;
  std::shared_future<void> releaseFuture = releasePromise.get_future().share();
  std::atomic<bool> startThrew{false};
  std::promise<void> reentrantStartReturnedPromise;
  std::future<void> reentrantStartReturnedFuture =
      reentrantStartReturnedPromise.get_future();
  AdapterIpcConnection *connectionPointer = nullptr;

  AdapterIpcConnectionCallbacks callbacks{
      .onConnected = [] {},
      .onMessageReceived =
          [&](const IpcMessage &) {
            callbackEnteredPromise.set_value();
            releaseFuture.wait();
            try {
              connectionPointer->Start();
            } catch (...) {
              startThrew = true;
            }
            reentrantStartReturnedPromise.set_value();
            return AdapterIpcMessageDisposition::kContinue;
          },
      .onDecodeFailure = [] {},
      .onDisconnected = [] {},
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks));
  connectionPointer = &connection;
  connection.Start();
  socket.PushReadableBytes(codec.Encode(
      IpcMessage{IpcResynchronizeRequestMessage{.correlationId = 1}}));
  REQUIRE(WaitReady(callbackEnteredFuture));

  std::promise<void> stopReturnedPromise;
  std::future<void> stopReturnedFuture = stopReturnedPromise.get_future();
  std::thread stopper([&] {
    connection.Stop();
    stopReturnedPromise.set_value();
  });
  //  Proves the external Stop() has become the join owner and is genuinely
  //  blocked waiting for this worker to return, before letting the worker's
  //  own callback call Start() reentrantly below.
  REQUIRE(stopReturnedFuture.wait_for(std::chrono::milliseconds(50)) ==
          std::future_status::timeout);

  releasePromise.set_value();
  REQUIRE(WaitReady(reentrantStartReturnedFuture));
  CHECK_FALSE(startThrew.load());
  REQUIRE(WaitReady(stopReturnedFuture));
  stopper.join();
}

TEST_CASE("AdapterIpcConnection contains an exception from the transport "
          "connect loop") {
  FakeAdapterIpcSocket socket;
  std::promise<void> connectAttemptedPromise;
  std::future<void> connectAttemptedFuture =
      connectAttemptedPromise.get_future();
  socket.SetThrowOnConnect(connectAttemptedPromise);
  IpcFrameCodec codec;

  AdapterIpcConnection connection(socket, codec,
                                  AdapterIpcConnectionCallbacks{});
  connection.Start();

  REQUIRE(WaitReady(connectAttemptedFuture));
  REQUIRE_NOTHROW(connection.Stop());
}

TEST_CASE("AdapterIpcConnection coordinates concurrent external Stop calls "
          "without letting the second caller inspect the worker thread "
          "while the first is still joining it") {
  //  The worker is deliberately kept genuinely running (blocked inside a
  //  message callback, released only at the end of this test) rather than
  //  merely started, so the first Stop() below is guaranteed to still be
  //  joining it when the second Stop() begins -- this puts the second call
  //  inside the exact join-in-progress window this fix protects, rather
  //  than hoping the scheduler happens to interleave the two calls that way.
  FakeAdapterIpcSocket socket;
  IpcFrameCodec codec;
  std::promise<void> callbackEnteredPromise;
  std::future<void> callbackEnteredFuture = callbackEnteredPromise.get_future();
  std::promise<void> releasePromise;
  std::shared_future<void> releaseFuture = releasePromise.get_future().share();
  AdapterIpcConnectionCallbacks callbacks{
      .onConnected = [] {},
      .onMessageReceived =
          [&](const IpcMessage &) {
            callbackEnteredPromise.set_value();
            releaseFuture.wait();
            return AdapterIpcMessageDisposition::kContinue;
          },
      .onDecodeFailure = [] {},
      .onDisconnected = [] {},
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks));
  connection.Start();
  socket.PushReadableBytes(codec.Encode(
      IpcMessage{IpcResynchronizeRequestMessage{.correlationId = 1}}));
  REQUIRE(WaitReady(callbackEnteredFuture));
  REQUIRE(socket.ConnectCallCount() == 1);

  std::atomic<bool> firstStopThrew{false};
  std::promise<void> firstStopReturnedPromise;
  std::future<void> firstStopReturnedFuture =
      firstStopReturnedPromise.get_future();
  std::thread firstStopper([&] {
    try {
      connection.Stop();
    } catch (...) {
      firstStopThrew = true;
    }
    firstStopReturnedPromise.set_value();
  });
  //  Proves the first Stop() is still blocked joining the worker -- the same
  //  still-blocked proof the "Stop waits for an in-flight message callback"
  //  test above uses -- before racing the second Stop() against it.
  REQUIRE(firstStopReturnedFuture.wait_for(std::chrono::milliseconds(50)) ==
          std::future_status::timeout);

  std::atomic<bool> secondStopThrew{false};
  std::promise<void> secondStopReturnedPromise;
  std::future<void> secondStopReturnedFuture =
      secondStopReturnedPromise.get_future();
  std::thread secondStopper([&] {
    try {
      connection.Stop();
    } catch (...) {
      secondStopThrew = true;
    }
    secondStopReturnedPromise.set_value();
  });
  //  The worker is still demonstrably blocked in its callback, so the
  //  second Stop() must still be waiting on the first Stop()'s join rather
  //  than having raced ahead to inspect or join the worker thread itself.
  CHECK(secondStopReturnedFuture.wait_for(std::chrono::milliseconds(50)) ==
        std::future_status::timeout);

  releasePromise.set_value();
  REQUIRE(WaitReady(firstStopReturnedFuture));
  REQUIRE(WaitReady(secondStopReturnedFuture));
  firstStopper.join();
  secondStopper.join();
  CHECK_FALSE(firstStopThrew.load());
  CHECK_FALSE(secondStopThrew.load());
  CHECK(socket.ConnectCallCount() == 1);
  //  Stop() is a terminal, one-way operation (`stopping_` never resets), so
  //  the reusability property worth proving here is idempotence, not a
  //  fresh reconnect: a third call, after two overlapping ones already
  //  joined the worker, must not hang, throw, or attempt a second join on
  //  the now-empty worker thread object.
  REQUIRE_NOTHROW(connection.Stop());
}

TEST_CASE("AdapterIpcConnection Stop waits for an in-flight message callback") {
  FakeAdapterIpcSocket socket;
  IpcFrameCodec codec;
  std::promise<void> callbackEnteredPromise;
  std::future<void> callbackEnteredFuture = callbackEnteredPromise.get_future();
  std::promise<void> releasePromise;
  std::shared_future<void> releaseFuture = releasePromise.get_future().share();

  AdapterIpcConnectionCallbacks callbacks{
      .onConnected = [] {},
      .onMessageReceived =
          [&](const IpcMessage &) {
            callbackEnteredPromise.set_value();
            releaseFuture.wait();
            return AdapterIpcMessageDisposition::kContinue;
          },
      .onDecodeFailure = [] {},
      .onDisconnected = [] {},
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks));
  connection.Start();
  socket.PushReadableBytes(codec.Encode(
      IpcMessage{IpcResynchronizeRequestMessage{.correlationId = 1}}));
  REQUIRE(WaitReady(callbackEnteredFuture));

  std::promise<void> stopReturnedPromise;
  std::future<void> stopReturnedFuture = stopReturnedPromise.get_future();
  std::thread stopper([&] {
    connection.Stop();
    stopReturnedPromise.set_value();
  });

  bool stopWaited =
      stopReturnedFuture.wait_for(std::chrono::milliseconds(50)) ==
      std::future_status::timeout;
  releasePromise.set_value();
  bool stopCompleted = WaitReady(stopReturnedFuture);
  stopper.join();

  CHECK(stopWaited);
  CHECK(stopCompleted);
}

TEST_CASE("AdapterIpcConnection::TrySend rejects once the outbound queue is "
          "full") {
  FakeAdapterIpcSocket socket;
  //  Never connects, so nothing ever drains the outbound queue.
  socket.SetConnectResults({false});
  IpcFrameCodec codec;

  AdapterIpcConnectionCallbacks callbacks{
      .onConnected = [] {},
      .onMessageReceived =
          [](const IpcMessage &) {
            return AdapterIpcMessageDisposition::kContinue;
          },
      .onDecodeFailure = [] {},
      .onDisconnected = [] {},
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks));

  IpcMessage message{
      IpcCloseMessage{.correlationId = 0, .reason = IpcCloseReason::kNormal}};
  for (std::size_t i = 0; i < kMaxIpcQueuedMessages; ++i) {
    REQUIRE(connection.TrySend(message));
  }
  CHECK_FALSE(connection.TrySend(message));

  connection.Start();
  connection.Stop();
}

TEST_CASE("AdapterIpcConnection reports a decode failure and disconnects "
          "rather than delivering a malformed message") {
  FakeAdapterIpcSocket socket;
  IpcFrameCodec codec;
  std::promise<void> connectedPromise;
  std::promise<void> decodeFailurePromise;
  std::atomic<bool> unexpectedMessageReceived{false};

  AdapterIpcConnectionCallbacks callbacks{
      .onConnected = [&] { connectedPromise.set_value(); },
      .onMessageReceived =
          [&](const IpcMessage &) {
            unexpectedMessageReceived = true;
            return AdapterIpcMessageDisposition::kContinue;
          },
      .onDecodeFailure = [&] { decodeFailurePromise.set_value(); },
      .onDisconnected = [] {},
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks));
  connection.Start();

  auto connectedFuture = connectedPromise.get_future();
  REQUIRE(WaitReady(connectedFuture));

  //  A declared frame length below the header size is malformed.
  socket.PushReadableBytes(
      {std::byte{0}, std::byte{0}, std::byte{0}, std::byte{0}});

  auto decodeFailureFuture = decodeFailurePromise.get_future();
  REQUIRE(WaitReady(decodeFailureFuture));

  connection.Stop();
  CHECK_FALSE(unexpectedMessageReceived.load());
}

TEST_CASE("AdapterIpcConnection::TrySend rejects once Stop has been called") {
  FakeAdapterIpcSocket socket;
  IpcFrameCodec codec;
  AdapterIpcConnectionCallbacks callbacks{
      .onConnected = [] {},
      .onMessageReceived =
          [](const IpcMessage &) {
            return AdapterIpcMessageDisposition::kContinue;
          },
      .onDecodeFailure = [] {},
      .onDisconnected = [] {},
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks));
  connection.Start();

  connection.Stop();

  IpcMessage message{
      IpcCloseMessage{.correlationId = 0, .reason = IpcCloseReason::kNormal}};
  CHECK_FALSE(connection.TrySend(message));
}

TEST_CASE("AdapterIpcConnection::Stop is idempotent") {
  FakeAdapterIpcSocket socket;
  IpcFrameCodec codec;
  AdapterIpcConnectionCallbacks callbacks{
      .onConnected = [] {},
      .onMessageReceived =
          [](const IpcMessage &) {
            return AdapterIpcMessageDisposition::kContinue;
          },
      .onDecodeFailure = [] {},
      .onDisconnected = [] {},
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks));
  connection.Start();

  connection.Stop();
  connection.Stop();
}

TEST_CASE("AdapterIpcConnection contains an exception thrown by "
          "onMessageReceived without crashing the background thread") {
  FakeAdapterIpcSocket socket;
  IpcFrameCodec codec;
  std::promise<void> connectedPromise;
  std::promise<void> receivedPromise;

  AdapterIpcConnectionCallbacks callbacks{
      .onConnected = [&] { connectedPromise.set_value(); },
      .onMessageReceived =
          [&](const IpcMessage &) -> AdapterIpcMessageDisposition {
        receivedPromise.set_value();
        throw std::runtime_error("onMessageReceived failed");
      },
      .onDecodeFailure = [] {},
      .onDisconnected = [] {},
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks));
  connection.Start();

  auto connectedFuture = connectedPromise.get_future();
  REQUIRE(WaitReady(connectedFuture));

  IpcMessage sent{IpcResynchronizeRequestMessage{.correlationId = 9}};
  socket.PushReadableBytes(codec.Encode(sent));

  auto receivedFuture = receivedPromise.get_future();
  REQUIRE(WaitReady(receivedFuture));

  //  If the exception escaped, the background thread would have terminated
  //  the process; reaching a clean Stop() proves it was contained.
  connection.Stop();
}

TEST_CASE("AdapterIpcConnection tolerates every callback being empty") {
  FakeAdapterIpcSocket socket;
  IpcFrameCodec codec;
  AdapterIpcConnection connection(socket, codec,
                                  AdapterIpcConnectionCallbacks{});
  connection.Start();

  IpcMessage sent{IpcResynchronizeRequestMessage{.correlationId = 1}};
  socket.PushReadableBytes(codec.Encode(sent));

  //  No promise to wait on since every callback is empty; give the
  //  background thread a bounded window to process the pushed frame before
  //  proving shutdown still completes cleanly.
  std::this_thread::sleep_for(std::chrono::milliseconds(100));

  connection.Stop();
}

TEST_CASE("AdapterIpcConnection drains a burst of queued messages in order") {
  FakeAdapterIpcSocket socket;
  IpcFrameCodec codec;
  std::promise<void> connectedPromise;

  AdapterIpcConnectionCallbacks callbacks{
      .onConnected = [&] { connectedPromise.set_value(); },
      .onMessageReceived =
          [](const IpcMessage &) {
            return AdapterIpcMessageDisposition::kContinue;
          },
      .onDecodeFailure = [] {},
      .onDisconnected = [] {},
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks));
  connection.Start();

  auto connectedFuture = connectedPromise.get_future();
  REQUIRE(WaitReady(connectedFuture));

  std::vector<IpcMessage> toSend;
  std::vector<std::byte> expectedBytes;
  for (std::uint64_t correlationId = 1; correlationId <= 3; ++correlationId) {
    IpcMessage message{IpcCancelMessage{.correlationId = correlationId}};
    toSend.push_back(message);
    std::vector<std::byte> encoded = codec.Encode(message);
    expectedBytes.insert(expectedBytes.end(), encoded.begin(), encoded.end());
  }
  for (const IpcMessage &message : toSend) {
    REQUIRE(connection.TrySend(message));
  }

  auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
  while (socket.WrittenBytes() != expectedBytes &&
         std::chrono::steady_clock::now() < deadline) {
    std::this_thread::sleep_for(std::chrono::milliseconds(10));
  }
  CHECK(socket.WrittenBytes() == expectedBytes);

  connection.Stop();
}

TEST_CASE("AdapterIpcConnection clears queued work between explicit attempts") {
  FakeAdapterIpcSocket socket;
  socket.SetConnectResults({true, true});
  socket.SetWriteResults({false, true});
  IpcFrameCodec codec;
  IpcMessage staleMessage{IpcCancelMessage{.correlationId = 1}};
  IpcMessage freshMessage{IpcCancelMessage{.correlationId = 2}};
  std::promise<void> firstOutcomePromise;
  std::promise<void> secondConnectedPromise;
  std::atomic<int> connectedCount{0};
  std::atomic<int> outcomeCount{0};
  AdapterIpcConnection *connectionPointer = nullptr;

  AdapterIpcConnectionCallbacks callbacks{
      .onConnected =
          [&] {
            if (connectedCount.fetch_add(1) == 0) {
              REQUIRE(connectionPointer->TrySend(staleMessage));
            } else {
              REQUIRE(connectionPointer->TrySend(freshMessage));
              secondConnectedPromise.set_value();
            }
          },
      .onMessageReceived =
          [](const IpcMessage &) {
            return AdapterIpcMessageDisposition::kContinue;
          },
      .onAttemptFinished =
          [&](std::uint64_t, AdapterIpcAttemptOutcome outcome) {
            if (outcome != AdapterIpcAttemptOutcome::kStopped &&
                outcomeCount.fetch_add(1) == 0) {
              firstOutcomePromise.set_value();
            }
          },
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks));
  connectionPointer = &connection;
  connection.Start();

  auto firstOutcomeFuture = firstOutcomePromise.get_future();
  REQUIRE(WaitReady(firstOutcomeFuture));
  connection.Start();
  auto secondConnectedFuture = secondConnectedPromise.get_future();
  REQUIRE(WaitReady(secondConnectedFuture));

  socket.SimulateDisconnect();
  auto writeDeadline =
      std::chrono::steady_clock::now() + std::chrono::seconds(5);
  std::vector<std::byte> expectedBytes = codec.Encode(freshMessage);
  while (socket.WrittenBytes() != expectedBytes &&
         std::chrono::steady_clock::now() < writeDeadline) {
    std::this_thread::sleep_for(std::chrono::milliseconds(10));
  }
  CHECK(socket.WrittenBytes() == expectedBytes);
  CHECK(socket.AttemptedWrites() ==
        std::vector<std::vector<std::byte>>{codec.Encode(staleMessage),
                                            expectedBytes});

  connection.Stop();
}

TEST_CASE("AdapterIpcConnection clears outbound work after a failed one-shot "
          "attempt") {
  FakeAdapterIpcSocket socket;
  socket.SetConnectResults({false});
  IpcFrameCodec codec;
  std::promise<void> outcomePromise;
  IpcMessage staleMessage{IpcCancelMessage{.correlationId = 1}};
  AdapterIpcAttemptOutcome outcome = AdapterIpcAttemptOutcome::kStopped;
  AdapterIpcConnectionCallbacks callbacks{
      .onAttemptFinished =
          [&](std::uint64_t generation, AdapterIpcAttemptOutcome result) {
            CHECK(generation == 0);
            outcome = result;
            outcomePromise.set_value();
          },
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks));
  REQUIRE(connection.TrySend(staleMessage));
  connection.Start();

  auto outcomeFuture = outcomePromise.get_future();
  REQUIRE(WaitReady(outcomeFuture));
  CHECK(outcome == AdapterIpcAttemptOutcome::kConnectFailed);
  CHECK(socket.ConnectCallCount() == 1);
  CHECK(socket.WrittenBytes().empty());
  connection.Stop();
}

TEST_CASE("AdapterIpcConnection ends serving without an autonomous retry when "
          "onMessageReceived requests close") {
  FakeAdapterIpcSocket socket;
  IpcFrameCodec codec;
  std::promise<void> connectedPromise;
  std::promise<void> disconnectedPromise;
  std::promise<void> outcomePromise;
  AdapterIpcAttemptOutcome outcome = AdapterIpcAttemptOutcome::kStopped;

  AdapterIpcConnectionCallbacks callbacks{
      .onConnected = [&] { connectedPromise.set_value(); },
      .onMessageReceived =
          [](const IpcMessage &) {
            return AdapterIpcMessageDisposition::kClose;
          },
      .onDecodeFailure = [] {},
      .onDisconnected = [&] { disconnectedPromise.set_value(); },
      .onAttemptFinished =
          [&](std::uint64_t, AdapterIpcAttemptOutcome result) {
            outcome = result;
            outcomePromise.set_value();
          },
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks));
  connection.Start();

  auto connectedFuture = connectedPromise.get_future();
  REQUIRE(WaitReady(connectedFuture));

  IpcMessage sent{IpcResynchronizeRequestMessage{.correlationId = 1}};
  socket.PushReadableBytes(codec.Encode(sent));

  auto disconnectedFuture = disconnectedPromise.get_future();
  REQUIRE(WaitReady(disconnectedFuture));

  auto outcomeFuture = outcomePromise.get_future();
  REQUIRE(WaitReady(outcomeFuture));
  CHECK(outcome == AdapterIpcAttemptOutcome::kAuthenticationFailed);
  CHECK(socket.ConnectCallCount() == 1);

  connection.Stop();
}

TEST_CASE("AdapterIpcConnection reports the configured target generation "
          "when acquiring the target snapshot throws") {
  //  A distinctive, unlikely-to-collide allocation size: the proof token
  //  copy inside AdapterIpcConnection::RunAttempt is the only allocation of
  //  exactly this many bytes this test performs.
  constexpr std::size_t kFailingAllocationSize = 257;

  FakeAdapterIpcSocket socket;
  IpcFrameCodec codec;
  std::promise<void> outcomePromise;
  std::uint64_t generation = 0;
  AdapterIpcAttemptOutcome outcome = AdapterIpcAttemptOutcome::kStopped;

  AdapterIpcConnectionCallbacks callbacks{
      .onAttemptFinished =
          [&](std::uint64_t attemptGeneration,
              AdapterIpcAttemptOutcome result) {
            generation = attemptGeneration;
            outcome = result;
            outcomePromise.set_value();
          },
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks));
  connection.ConfigureTarget(AdapterIpcTarget{
      .port = 4242,
      .proofToken = std::vector<std::byte>(kFailingAllocationSize),
      .targetGeneration = 99,
  });

  {
    ScopedAllocationFailure failNextMatchingAllocation(kFailingAllocationSize);
    connection.Start();

    auto outcomeFuture = outcomePromise.get_future();
    REQUIRE(WaitReady(outcomeFuture));
  }

  //  The generation must be the one actually configured, not the `0`
  //  fallback RunLoop's outer failure boundary used before it captured the
  //  generation ahead of the throwing snapshot copy.
  CHECK(generation == 99);
  CHECK(outcome == AdapterIpcAttemptOutcome::kConnectFailed);
  //  The exception happened before the connection attempt reached the
  //  transport at all -- proving this is `RunLoop`'s outer boundary, not the
  //  inner one that already wraps `Connect()`.
  CHECK(socket.ConnectCallCount() == 0);

  connection.Stop();
}
