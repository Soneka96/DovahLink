#include "ipc/adapter_ipc_connection.hpp"

#include "ipc/adapter_ipc_socket_test_support.hpp"
#include "ipc/ipc_constants.hpp"
#include "ipc/ipc_frame_codec.hpp"
#include "ipc/winsock_adapter_ipc_socket.hpp"

#include <catch2/catch_test_macros.hpp>

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
using dovahlink::adapter::ipc::IAdapterIpcSocket;
using dovahlink::adapter::ipc::IpcCancelMessage;
using dovahlink::adapter::ipc::IpcCloseMessage;
using dovahlink::adapter::ipc::IpcCloseReason;
using dovahlink::adapter::ipc::IpcFrameCodec;
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
            return true;
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

TEST_CASE("AdapterIpcConnection::TrySend is written even while no inbound "
          "message ever arrives") {
  FakeAdapterIpcSocket socket;
  IpcFrameCodec codec;
  std::promise<void> connectedPromise;

  AdapterIpcConnectionCallbacks callbacks{
      .onConnected = [&] { connectedPromise.set_value(); },
      .onMessageReceived = [](const IpcMessage &) { return true; },
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
      .onMessageReceived = [](const IpcMessage &) { return true; },
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
      .onMessageReceived = [](const IpcMessage &) { return true; },
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
            return true;
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
      .onMessageReceived = [](const IpcMessage &) { return true; },
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
            return true;
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
      .onMessageReceived = [](const IpcMessage &) { return true; },
      .onDecodeFailure = [] {},
      .onDisconnected = [] {},
  };
  AdapterIpcConnection connection(socket, codec, std::move(callbacks));
  connection.Start();

  IpcMessage message{
      IpcCloseMessage{.correlationId = 0, .reason = IpcCloseReason::kNormal}};
  for (std::size_t i = 0; i < kMaxIpcQueuedMessages; ++i) {
    REQUIRE(connection.TrySend(message));
  }
  CHECK_FALSE(connection.TrySend(message));

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
            return true;
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
      .onMessageReceived = [](const IpcMessage &) { return true; },
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
      .onMessageReceived = [](const IpcMessage &) { return true; },
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
      .onMessageReceived = [&](const IpcMessage &) -> bool {
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
      .onMessageReceived = [](const IpcMessage &) { return true; },
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
      .onMessageReceived = [](const IpcMessage &) { return false; },
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
