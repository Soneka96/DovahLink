#include "ipc/adapter_ipc_connection.hpp"

#include "ipc/adapter_ipc_socket_test_support.hpp"
#include "ipc/ipc_constants.hpp"
#include "ipc/ipc_frame_codec.hpp"
#include "ipc/winsock_adapter_ipc_socket.hpp"

#include <catch2/catch_test_macros.hpp>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstdint>
#include <future>
#include <optional>
#include <stdexcept>
#include <thread>
#include <vector>

using dovahlink::adapter::ipc::AdapterIpcConnection;
using dovahlink::adapter::ipc::AdapterIpcConnectionCallbacks;
using dovahlink::adapter::ipc::AdapterIpcMessageDisposition;
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
using dovahlink::adapter::ipc::kMaxIpcQueuedMessages;
using dovahlink::adapter::ipc::test_support::FakeAdapterIpcSocket;

namespace {

///  Waits for `future` up to a generous bound and returns whether it became
///  ready, instead of hanging the test suite if production code regresses.
template <typename T> bool WaitReady(std::future<T> &future) {
  return future.wait_for(std::chrono::seconds(5)) == std::future_status::ready;
}

} //  namespace

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

TEST_CASE("AdapterIpcConnection reconnects after a failed connect attempt") {
  FakeAdapterIpcSocket socket;
  socket.SetConnectResults({false, false, true});
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
  CHECK(socket.ConnectCallCount() == 3);

  connection.Stop();
}

TEST_CASE("AdapterIpcConnection reports each failed connect attempt before it "
          "reconnects") {
  FakeAdapterIpcSocket socket;
  socket.SetConnectResults({false, false, true});
  IpcFrameCodec codec;
  std::promise<void> connectedPromise;
  std::atomic<int> failedAttemptCount{0};
  std::atomic<int> disconnectedCount{0};

  AdapterIpcConnectionCallbacks callbacks{
      .onConnected = [&] { connectedPromise.set_value(); },
      .onDisconnected = [&] { ++disconnectedCount; },
      .onConnectionAttemptFailed = [&] { ++failedAttemptCount; },
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks));
  connection.Start();

  auto connectedFuture = connectedPromise.get_future();
  REQUIRE(WaitReady(connectedFuture));
  CHECK(failedAttemptCount.load() == 2);
  CHECK(disconnectedCount.load() == 0);

  connection.Stop();
}

TEST_CASE("AdapterIpcConnection retries after a throwing connect attempt") {
  FakeAdapterIpcSocket socket;
  socket.SetConnectResults({true});
  std::promise<void> connectAttemptedPromise;
  socket.SetThrowOnConnect(connectAttemptedPromise);
  IpcFrameCodec codec;
  std::promise<void> failedAttemptPromise;
  std::promise<void> connectedPromise;
  std::atomic<int> failedAttemptCount{0};
  std::atomic<int> disconnectedCount{0};
  IpcMessage staleMessage{IpcCancelMessage{.correlationId = 1}};
  IpcMessage freshHello{IpcHelloMessage{.correlationId = 100}};
  AdapterIpcConnection *connectionPointer = nullptr;
  std::atomic<bool> enqueueSucceeded{true};

  AdapterIpcConnectionCallbacks callbacks{
      .onConnected =
          [&] {
            enqueueSucceeded = connectionPointer->TrySend(freshHello);
            connectedPromise.set_value();
          },
      .onDisconnected = [&] { ++disconnectedCount; },
      .onConnectionAttemptFailed =
          [&] {
            ++failedAttemptCount;
            failedAttemptPromise.set_value();
            throw std::runtime_error("failed-connect callback failed");
          },
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks));
  connectionPointer = &connection;
  REQUIRE(connection.TrySend(staleMessage));
  connection.Start();

  auto connectAttemptedFuture = connectAttemptedPromise.get_future();
  REQUIRE(WaitReady(connectAttemptedFuture));
  auto failedAttemptFuture = failedAttemptPromise.get_future();
  REQUIRE(WaitReady(failedAttemptFuture));
  auto connectedFuture = connectedPromise.get_future();
  REQUIRE(WaitReady(connectedFuture));
  CHECK(enqueueSucceeded.load());
  CHECK(failedAttemptCount.load() == 1);
  CHECK(socket.ConnectCallCount() == 2);
  CHECK(socket.CloseCallCount() >= 1);
  CHECK(disconnectedCount.load() == 0);

  std::vector<std::byte> expectedBytes = codec.Encode(freshHello);
  auto writeDeadline =
      std::chrono::steady_clock::now() + std::chrono::seconds(5);
  while (socket.WrittenBytes() != expectedBytes &&
         std::chrono::steady_clock::now() < writeDeadline) {
    std::this_thread::sleep_for(std::chrono::milliseconds(10));
  }
  CHECK(socket.WrittenBytes() == expectedBytes);
  CHECK(socket.AttemptedWrites() ==
        std::vector<std::vector<std::byte>>{expectedBytes});

  connection.Stop();
}

TEST_CASE("AdapterIpcConnection contains an exception from the failed-connect "
          "callback and continues its retry loop") {
  FakeAdapterIpcSocket socket;
  socket.SetConnectResults({false, true});
  IpcFrameCodec codec;
  std::promise<void> connectedPromise;
  std::atomic<int> failedAttemptCount{0};

  AdapterIpcConnectionCallbacks callbacks{
      .onConnected = [&] { connectedPromise.set_value(); },
      .onConnectionAttemptFailed =
          [&] {
            ++failedAttemptCount;
            throw std::runtime_error("failed-connect callback failed");
          },
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks));
  connection.Start();

  auto connectedFuture = connectedPromise.get_future();
  REQUIRE(WaitReady(connectedFuture));
  CHECK(failedAttemptCount.load() == 1);
  CHECK(socket.ConnectCallCount() == 2);

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

TEST_CASE("AdapterIpcConnection coordinates concurrent external Stop calls") {
  FakeAdapterIpcSocket socket;
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
  connection.Start();

  std::atomic<bool> firstStopThrew{false};
  std::atomic<bool> secondStopThrew{false};
  std::thread firstStopper([&] {
    try {
      connection.Stop();
    } catch (...) {
      firstStopThrew = true;
    }
  });
  std::thread secondStopper([&] {
    try {
      connection.Stop();
    } catch (...) {
      secondStopThrew = true;
    }
  });

  firstStopper.join();
  secondStopper.join();
  CHECK_FALSE(firstStopThrew.load());
  CHECK_FALSE(secondStopThrew.load());
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

TEST_CASE("AdapterIpcConnection discards pending outbound work between "
          "transport generations") {
  FakeAdapterIpcSocket socket;
  socket.SetConnectResults({true, true, false});
  //  A fails, the final same-generation flush of B fails, and the fresh
  //  Hello on generation 2 succeeds.
  socket.SetWriteResults({false, false, true});
  IpcFrameCodec codec;
  std::promise<void> firstConnectedPromise;
  std::promise<void> secondConnectedPromise;
  std::atomic<int> connectedCount{0};

  IpcMessage messageA{IpcCancelMessage{.correlationId = 1}};
  IpcMessage messageB{IpcCancelMessage{.correlationId = 2}};
  IpcMessage freshHello{IpcHelloMessage{.correlationId = 100}};
  IpcMessage postGenerationMessage{IpcCancelMessage{.correlationId = 3}};

  AdapterIpcConnection *connectionPointer = nullptr;
  std::atomic<bool> enqueueSucceeded{true};
  AdapterIpcConnectionCallbacks callbacks{
      .onConnected =
          [&] {
            if (connectedCount.fetch_add(1) == 0) {
              bool firstEnqueued = connectionPointer->TrySend(messageA);
              bool secondEnqueued = connectionPointer->TrySend(messageB);
              enqueueSucceeded = firstEnqueued && secondEnqueued;
              firstConnectedPromise.set_value();
            } else {
              enqueueSucceeded = enqueueSucceeded.load() &&
                                 connectionPointer->TrySend(freshHello);
              secondConnectedPromise.set_value();
            }
          },
      .onMessageReceived =
          [](const IpcMessage &) {
            return AdapterIpcMessageDisposition::kContinue;
          },
      .onDisconnected = [] {},
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks));
  connectionPointer = &connection;
  connection.Start();

  auto firstConnectedFuture = firstConnectedPromise.get_future();
  REQUIRE(WaitReady(firstConnectedFuture));
  auto secondConnectedFuture = secondConnectedPromise.get_future();
  REQUIRE(WaitReady(secondConnectedFuture));
  CHECK(enqueueSucceeded.load());

  std::vector<std::byte> expectedBytes = codec.Encode(freshHello);
  std::vector<std::vector<std::byte>> expectedAttempts{
      codec.Encode(messageA), codec.Encode(messageB), codec.Encode(freshHello)};
  auto writeDeadline =
      std::chrono::steady_clock::now() + std::chrono::seconds(5);
  while (socket.WrittenBytes() != expectedBytes &&
         std::chrono::steady_clock::now() < writeDeadline) {
    std::this_thread::sleep_for(std::chrono::milliseconds(10));
  }
  REQUIRE(socket.WrittenBytes() == expectedBytes);
  auto attempts = socket.AttemptedWrites();
  REQUIRE(attempts.size() >= expectedAttempts.size());
  CHECK(std::equal(expectedAttempts.begin(), expectedAttempts.end(),
                   attempts.begin()));

  REQUIRE(connection.TrySend(postGenerationMessage));
  std::vector<std::byte> postGenerationBytes =
      codec.Encode(postGenerationMessage);
  expectedBytes.insert(expectedBytes.end(), postGenerationBytes.begin(),
                       postGenerationBytes.end());
  while (socket.WrittenBytes() != expectedBytes &&
         std::chrono::steady_clock::now() < writeDeadline) {
    std::this_thread::sleep_for(std::chrono::milliseconds(10));
  }
  CHECK(socket.WrittenBytes() == expectedBytes);
  expectedAttempts.push_back(postGenerationBytes);
  attempts = socket.AttemptedWrites();
  REQUIRE(attempts.size() >= expectedAttempts.size());
  CHECK(std::equal(expectedAttempts.begin(), expectedAttempts.end(),
                   attempts.begin()));

  connection.Stop();
}

TEST_CASE("AdapterIpcConnection clears outbound work after a failed connect "
          "attempt") {
  FakeAdapterIpcSocket socket;
  socket.SetConnectResults({false, true, false});
  IpcFrameCodec codec;
  std::promise<void> connectedPromise;
  IpcMessage staleMessage{IpcCancelMessage{.correlationId = 1}};
  IpcMessage freshHello{IpcHelloMessage{.correlationId = 100}};

  AdapterIpcConnection *connectionPointer = nullptr;
  std::atomic<bool> enqueueSucceeded{true};
  AdapterIpcConnectionCallbacks callbacks{
      .onConnected =
          [&] {
            bool freshEnqueued = connectionPointer->TrySend(freshHello);
            enqueueSucceeded = enqueueSucceeded.load() && freshEnqueued;
            connectedPromise.set_value();
          },
      .onMessageReceived =
          [](const IpcMessage &) {
            return AdapterIpcMessageDisposition::kContinue;
          },
      .onDisconnected = [] {},
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks));
  connectionPointer = &connection;
  REQUIRE(connection.TrySend(staleMessage));
  connection.Start();

  auto connectedFuture = connectedPromise.get_future();
  REQUIRE(WaitReady(connectedFuture));
  CHECK(enqueueSucceeded.load());
  std::vector<std::byte> expectedBytes = codec.Encode(freshHello);
  auto writeDeadline =
      std::chrono::steady_clock::now() + std::chrono::seconds(5);
  while (socket.WrittenBytes() != expectedBytes &&
         std::chrono::steady_clock::now() < writeDeadline) {
    std::this_thread::sleep_for(std::chrono::milliseconds(10));
  }

  CHECK(socket.WrittenBytes() == expectedBytes);
  CHECK(socket.AttemptedWrites() ==
        std::vector<std::vector<std::byte>>{expectedBytes});
  connection.Stop();
}

TEST_CASE("AdapterIpcConnection ends serving and reconnects when "
          "onMessageReceived returns false") {
  FakeAdapterIpcSocket socket;
  IpcFrameCodec codec;
  std::promise<void> firstConnectedPromise;
  std::promise<void> disconnectedPromise;
  std::promise<void> secondConnectedPromise;
  std::atomic<int> connectedCount{0};

  AdapterIpcConnectionCallbacks callbacks{
      .onConnected =
          [&] {
            if (connectedCount.fetch_add(1) == 0) {
              firstConnectedPromise.set_value();
            } else {
              secondConnectedPromise.set_value();
            }
          },
      .onMessageReceived =
          [](const IpcMessage &) {
            return AdapterIpcMessageDisposition::kClose;
          },
      .onDecodeFailure = [] {},
      .onDisconnected = [&] { disconnectedPromise.set_value(); },
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks));
  connection.Start();

  auto firstConnectedFuture = firstConnectedPromise.get_future();
  REQUIRE(WaitReady(firstConnectedFuture));

  IpcMessage sent{IpcResynchronizeRequestMessage{.correlationId = 1}};
  socket.PushReadableBytes(codec.Encode(sent));

  auto disconnectedFuture = disconnectedPromise.get_future();
  REQUIRE(WaitReady(disconnectedFuture));

  //  A false return ends this connected session but is not itself Stop(); a
  //  fresh connect attempt should follow, same as any other disconnect.
  auto secondConnectedFuture = secondConnectedPromise.get_future();
  REQUIRE(WaitReady(secondConnectedFuture));
  CHECK(connectedCount.load() == 2);

  connection.Stop();
}
