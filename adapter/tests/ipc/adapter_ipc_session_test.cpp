#include "ipc/adapter_ipc_session.hpp"

#include "ipc/adapter_ipc_connection.hpp"

#include <catch2/catch_test_macros.hpp>

#include <chrono>
#include <cstddef>
#include <cstdint>
#include <future>
#include <memory>
#include <mutex>
#include <optional>
#include <stdexcept>
#include <thread>
#include <unordered_map>
#include <unordered_set>
#include <utility>
#include <variant>
#include <vector>

using dovahlink::adapter::capture::AdapterCaptureWorkItem;
using dovahlink::adapter::capture::IAdapterCaptureHandoffQueue;
using dovahlink::adapter::dispatch::IAdapterNativeDispatcher;
using dovahlink::adapter::identity::AdapterInstanceId;
using dovahlink::adapter::ipc::AdapterIpcSession;
using dovahlink::adapter::ipc::FixedAdapterIpcPeerProofProvider;
using dovahlink::adapter::ipc::IAdapterIpcConnection;
using dovahlink::adapter::ipc::IpcCancelMessage;
using dovahlink::adapter::ipc::IpcCloseMessage;
using dovahlink::adapter::ipc::IpcCloseReason;
using dovahlink::adapter::ipc::IpcHelloAckMessage;
using dovahlink::adapter::ipc::IpcHelloMessage;
using dovahlink::adapter::ipc::IpcHelloRejectReason;
using dovahlink::adapter::ipc::IpcListenEventMessage;
using dovahlink::adapter::ipc::IpcMessage;
using dovahlink::adapter::ipc::IpcReadSampleMessage;
using dovahlink::adapter::ipc::IpcRejectMessage;
using dovahlink::adapter::ipc::IpcRejectReason;
using dovahlink::adapter::ipc::IpcResynchronizeRequestMessage;
using dovahlink::adapter::ipc::IpcResynchronizeResultMessage;
using dovahlink::adapter::runtime::IAdapterTaskMarshaller;

namespace {

///  A fake `IAdapterTaskMarshaller` that stores tasks instead of running
///  them, so tests control exactly when marshaled work executes.
class FakeAdapterTaskMarshaller final : public IAdapterTaskMarshaller {
public:
  void RunOnGameThread(std::function<void()> task) override {
    pendingTasks_.push_back(std::move(task));
  }

  ///  The number of tasks not yet run.
  std::size_t PendingCount() const { return pendingTasks_.size(); }

  ///  Runs and clears every currently pending task, in order.
  void RunAllPending() {
    std::vector<std::function<void()>> tasks;
    std::swap(tasks, pendingTasks_);
    for (auto &task : tasks) {
      task();
    }
  }

private:
  std::vector<std::function<void()>> pendingTasks_;
};

///  A fake `IAdapterNativeDispatcher` with a configurable per-key result.
class FakeAdapterNativeDispatcher final : public IAdapterNativeDispatcher {
public:
  void SetResult(std::uint32_t key, std::vector<std::byte> value) {
    results_[key] = std::move(value);
  }

  ///  Makes `TryDispatch(key)` throw instead of returning.
  void SetThrows(std::uint32_t key) { throwingKeys_.insert(key); }

  std::optional<std::vector<std::byte>>
  TryDispatch(std::uint32_t intentKey) override {
    dispatchedKeys_.push_back(intentKey);
    if (throwingKeys_.contains(intentKey)) {
      throw std::runtime_error("TryDispatch failed");
    }
    auto it = results_.find(intentKey);
    if (it == results_.end()) {
      return std::nullopt;
    }
    return it->second;
  }

  const std::vector<std::uint32_t> &DispatchedKeys() const {
    return dispatchedKeys_;
  }

private:
  std::unordered_map<std::uint32_t, std::vector<std::byte>> results_;
  std::unordered_set<std::uint32_t> throwingKeys_;
  std::vector<std::uint32_t> dispatchedKeys_;
};

///  A dispatcher that holds a game-thread callback until the test releases it.
class BlockingAdapterNativeDispatcher final : public IAdapterNativeDispatcher {
public:
  ///  Creates a dispatcher synchronized by the supplied entry and release
  ///  signals.
  BlockingAdapterNativeDispatcher(std::promise<void> &entered,
                                  std::shared_future<void> release)
      : entered_(entered), release_(std::move(release)) {}

  ///  Signals that the callback entered, then waits for the test to release
  ///  it before reporting that no translation exists.
  std::optional<std::vector<std::byte>>
  TryDispatch(std::uint32_t /*intentKey*/) override {
    entered_.set_value();
    release_.wait();
    return std::nullopt;
  }

private:
  ///  Signals that the callback has entered the dispatcher.
  std::promise<void> &entered_;
  ///  Keeps the callback blocked until the test releases it.
  std::shared_future<void> release_;
};

///  A fake `IAdapterCaptureHandoffQueue` that records every enqueued item.
class FakeAdapterCaptureHandoffQueue final
    : public IAdapterCaptureHandoffQueue {
public:
  bool TryEnqueue(AdapterCaptureWorkItem item) override {
    enqueued_.push_back(std::move(item));
    return true;
  }

  void Stop() override {}

  const std::vector<AdapterCaptureWorkItem> &Enqueued() const {
    return enqueued_;
  }

private:
  std::vector<AdapterCaptureWorkItem> enqueued_;
};

///  A fake `IAdapterIpcConnection` that records every message sent through
///  it, instead of any real transport.
class FakeAdapterIpcConnection final : public IAdapterIpcConnection {
public:
  void Start() override {}

  bool TrySend(const IpcMessage &message) override {
    sent_.push_back(message);
    return true;
  }

  void Stop() override {}

  const std::vector<IpcMessage> &Sent() const { return sent_; }

private:
  std::vector<IpcMessage> sent_;
};

///  A representative, fixed adapter instance identity for tests that don't
///  care about its value.
AdapterInstanceId SampleInstanceId() {
  AdapterInstanceId id{};
  for (std::size_t index = 0; index < id.value.size(); ++index) {
    id.value[index] = static_cast<std::byte>(index + 1);
  }
  return id;
}

///  Bundles a session with the fakes it was constructed against, so each
///  test can inspect them without repeating setup.
struct SessionFixture {
  FixedAdapterIpcPeerProofProvider peerProofProvider{
      {std::byte{9}, std::byte{8}, std::byte{7}}};
  FakeAdapterTaskMarshaller marshaller;
  FakeAdapterNativeDispatcher dispatcher;
  FakeAdapterCaptureHandoffQueue captureQueue;
  AdapterIpcSession session{SampleInstanceId(), peerProofProvider, marshaller,
                            dispatcher, captureQueue};
};

} //  namespace

TEST_CASE("AdapterIpcSession::PrepareHello builds Hello from the configured "
          "identity and proof token") {
  SessionFixture fixture;

  IpcMessage first = fixture.session.PrepareHello();
  auto *hello = std::get_if<IpcHelloMessage>(&first);
  REQUIRE(hello != nullptr);
  CHECK(hello->correlationId == 1);
  CHECK(hello->adapterInstanceId == SampleInstanceId().value);
  CHECK(hello->peerProofToken ==
        std::vector<std::byte>{std::byte{9}, std::byte{8}, std::byte{7}});

  IpcMessage second = fixture.session.PrepareHello();
  auto *secondHello = std::get_if<IpcHelloMessage>(&second);
  REQUIRE(secondHello != nullptr);
  CHECK(secondHello->correlationId == 2);
}

TEST_CASE("AdapterIpcSession::HandleConnected sends Hello through the "
          "attached connection") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);

  fixture.session.HandleConnected();

  REQUIRE(connection.Sent().size() == 1);
  CHECK(std::holds_alternative<IpcHelloMessage>(connection.Sent().front()));
}

TEST_CASE("AdapterIpcSession::HandleConnected does nothing without an "
          "attached connection") {
  SessionFixture fixture;

  fixture.session.HandleConnected();
}

TEST_CASE("AdapterIpcSession marks the host available only after an "
          "accepted HelloAck") {
  SessionFixture fixture;
  CHECK_FALSE(fixture.session.IsHostAvailable());

  CHECK(fixture.session.HandleMessage(IpcMessage{
      IpcHelloAckMessage{.correlationId = 1,
                         .accepted = true,
                         .rejectReason = IpcHelloRejectReason::kNone}}));

  CHECK(fixture.session.IsHostAvailable());
}

TEST_CASE("AdapterIpcSession keeps the host unavailable after a rejected "
          "HelloAck") {
  SessionFixture fixture;

  CHECK(fixture.session.HandleMessage(IpcMessage{IpcHelloAckMessage{
      .correlationId = 1,
      .accepted = false,
      .rejectReason = IpcHelloRejectReason::kInvalidProof}}));

  CHECK_FALSE(fixture.session.IsHostAvailable());
}

TEST_CASE("AdapterIpcSession returns to unavailable after a rejected "
          "HelloAck follows an accepted one") {
  SessionFixture fixture;
  fixture.session.HandleMessage(IpcMessage{
      IpcHelloAckMessage{.correlationId = 1,
                         .accepted = true,
                         .rejectReason = IpcHelloRejectReason::kNone}});
  REQUIRE(fixture.session.IsHostAvailable());

  fixture.session.HandleMessage(IpcMessage{
      IpcHelloAckMessage{.correlationId = 2,
                         .accepted = false,
                         .rejectReason = IpcHelloRejectReason::kInvalidProof}});

  CHECK_FALSE(fixture.session.IsHostAvailable());
}

TEST_CASE("AdapterIpcSession::HandleDisconnected marks the host "
          "unavailable") {
  SessionFixture fixture;
  fixture.session.HandleMessage(IpcMessage{
      IpcHelloAckMessage{.correlationId = 1,
                         .accepted = true,
                         .rejectReason = IpcHelloRejectReason::kNone}});
  REQUIRE(fixture.session.IsHostAvailable());

  fixture.session.HandleDisconnected();

  CHECK_FALSE(fixture.session.IsHostAvailable());
}

TEST_CASE("AdapterIpcSession handles a resynchronize request by marshaling "
          "a task that reports no baseline is available") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);

  CHECK(fixture.session.HandleMessage(
      IpcMessage{IpcResynchronizeRequestMessage{.correlationId = 42}}));

  //  Not sent synchronously: it must go through the game-thread marshaller.
  CHECK(connection.Sent().empty());
  REQUIRE(fixture.marshaller.PendingCount() == 1);

  fixture.marshaller.RunAllPending();

  REQUIRE(connection.Sent().size() == 1);
  auto *result =
      std::get_if<IpcResynchronizeResultMessage>(&connection.Sent().front());
  REQUIRE(result != nullptr);
  CHECK(result->correlationId == 42);
  CHECK_FALSE(result->accepted);
  CHECK(fixture.dispatcher.DispatchedKeys().empty());
  CHECK(fixture.captureQueue.Enqueued().empty());
}

TEST_CASE("AdapterIpcSession's marshaled resynchronize reply does nothing "
          "without an attached connection") {
  SessionFixture fixture;

  fixture.session.HandleMessage(
      IpcMessage{IpcResynchronizeRequestMessage{.correlationId = 1}});
  fixture.marshaller.RunAllPending();
}

TEST_CASE("AdapterIpcSession drops pending game-thread work after session "
          "destruction") {
  FixedAdapterIpcPeerProofProvider peerProofProvider{
      {std::byte{9}, std::byte{8}, std::byte{7}}};
  FakeAdapterTaskMarshaller marshaller;
  FakeAdapterNativeDispatcher dispatcher;
  FakeAdapterCaptureHandoffQueue captureQueue;
  FakeAdapterIpcConnection connection;

  {
    AdapterIpcSession session{SampleInstanceId(), peerProofProvider, marshaller,
                              dispatcher, captureQueue};
    session.AttachConnection(connection);
    session.HandleMessage(
        IpcMessage{IpcListenEventMessage{.correlationId = 1, .eventKey = 7}});
    session.HandleMessage(
        IpcMessage{IpcResynchronizeRequestMessage{.correlationId = 42}});
    session.HandleMessage(
        IpcMessage{IpcReadSampleMessage{.correlationId = 3, .sampleToken = 8}});
  }

  REQUIRE(marshaller.PendingCount() == 3);
  REQUIRE_NOTHROW(marshaller.RunAllPending());
  CHECK(dispatcher.DispatchedKeys().empty());
  CHECK(captureQueue.Enqueued().empty());
  CHECK(connection.Sent().empty());
}

TEST_CASE("AdapterIpcSession drops a pending resynchronization result after "
          "disconnect") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  fixture.session.HandleMessage(IpcMessage{
      IpcHelloAckMessage{.correlationId = 1,
                         .accepted = true,
                         .rejectReason = IpcHelloRejectReason::kNone}});

  fixture.session.HandleMessage(
      IpcMessage{IpcResynchronizeRequestMessage{.correlationId = 42}});
  fixture.session.HandleDisconnected();
  fixture.marshaller.RunAllPending();

  CHECK(connection.Sent().empty());
}

TEST_CASE("AdapterIpcSession destruction waits for an in-flight game-thread "
          "callback before returning") {
  FixedAdapterIpcPeerProofProvider peerProofProvider{
      {std::byte{9}, std::byte{8}, std::byte{7}}};
  FakeAdapterTaskMarshaller marshaller;
  std::promise<void> enteredPromise;
  std::shared_future<void> enteredFuture = enteredPromise.get_future();
  std::promise<void> releasePromise;
  std::shared_future<void> releaseFuture = releasePromise.get_future().share();
  BlockingAdapterNativeDispatcher dispatcher{enteredPromise, releaseFuture};
  FakeAdapterCaptureHandoffQueue captureQueue;
  FakeAdapterIpcConnection connection;
  auto session =
      std::make_unique<AdapterIpcSession>(SampleInstanceId(), peerProofProvider,
                                          marshaller, dispatcher, captureQueue);
  session->AttachConnection(connection);
  session->HandleMessage(
      IpcMessage{IpcListenEventMessage{.correlationId = 1, .eventKey = 7}});

  std::thread gameThread([&] { marshaller.RunAllPending(); });
  bool callbackEntered = enteredFuture.wait_for(std::chrono::seconds(5)) ==
                         std::future_status::ready;
  if (!callbackEntered) {
    releasePromise.set_value();
    gameThread.join();
    FAIL("the game-thread callback did not enter the dispatcher");
  }

  std::promise<void> destroyedPromise;
  std::future<void> destroyedFuture = destroyedPromise.get_future();
  std::thread destructionThread([&] {
    session.reset();
    destroyedPromise.set_value();
  });

  CHECK(destroyedFuture.wait_for(std::chrono::milliseconds(50)) ==
        std::future_status::timeout);

  releasePromise.set_value();
  REQUIRE(destroyedFuture.wait_for(std::chrono::seconds(5)) ==
          std::future_status::ready);
  gameThread.join();
  destructionThread.join();
}

TEST_CASE("AdapterIpcSession rejects and ends serving on a received "
          "ResynchronizeResult, since it is adapter-outbound only") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);

  bool keepServing = fixture.session.HandleMessage(IpcMessage{
      IpcResynchronizeResultMessage{.correlationId = 7, .accepted = true}});

  CHECK_FALSE(keepServing);
  REQUIRE(connection.Sent().size() == 1);
  auto *reject = std::get_if<IpcRejectMessage>(&connection.Sent().front());
  REQUIRE(reject != nullptr);
  CHECK(reject->correlationId == 7);
  CHECK(reject->reason == IpcRejectReason::kUnknownMessageKind);
}

TEST_CASE("AdapterIpcSession handles a listen-event request by dispatching "
          "the key on the game thread and enqueuing a captured value") {
  SessionFixture fixture;
  fixture.dispatcher.SetResult(7, {std::byte{1}, std::byte{2}});

  CHECK(fixture.session.HandleMessage(
      IpcMessage{IpcListenEventMessage{.correlationId = 1, .eventKey = 7}}));

  REQUIRE(fixture.captureQueue.Enqueued().empty());
  fixture.marshaller.RunAllPending();

  CHECK(fixture.dispatcher.DispatchedKeys() == std::vector<std::uint32_t>{7});
  REQUIRE(fixture.captureQueue.Enqueued().size() == 1);
  CHECK(fixture.captureQueue.Enqueued().front().intentKey == 7);
  CHECK(fixture.captureQueue.Enqueued().front().capturedValue ==
        std::vector<std::byte>{std::byte{1}, std::byte{2}});
}

TEST_CASE("AdapterIpcSession enqueues nothing for a listen-event key with "
          "no registered translation") {
  SessionFixture fixture;

  fixture.session.HandleMessage(
      IpcMessage{IpcListenEventMessage{.correlationId = 1, .eventKey = 99}});
  fixture.marshaller.RunAllPending();

  CHECK(fixture.captureQueue.Enqueued().empty());
}

TEST_CASE("AdapterIpcSession enqueues nothing for a read-sample token with "
          "no registered translation") {
  SessionFixture fixture;

  fixture.session.HandleMessage(
      IpcMessage{IpcReadSampleMessage{.correlationId = 1, .sampleToken = 99}});
  fixture.marshaller.RunAllPending();

  CHECK(fixture.dispatcher.DispatchedKeys() == std::vector<std::uint32_t>{99});
  CHECK(fixture.captureQueue.Enqueued().empty());
}

TEST_CASE("AdapterIpcSession contains an exception thrown by the "
          "dispatcher inside a marshaled listen-event task") {
  SessionFixture fixture;
  fixture.dispatcher.SetThrows(13);

  fixture.session.HandleMessage(
      IpcMessage{IpcListenEventMessage{.correlationId = 1, .eventKey = 13}});

  //  If the exception escaped, it would propagate out of RunAllPending() --
  //  the fake marshaller's stand-in for SKSE's own game-thread task queue --
  //  and fail this test.
  fixture.marshaller.RunAllPending();

  CHECK(fixture.captureQueue.Enqueued().empty());
}

TEST_CASE("AdapterIpcSession handles a read-sample request the same way as "
          "a listen-event request") {
  SessionFixture fixture;
  fixture.dispatcher.SetResult(3, {std::byte{5}});

  CHECK(fixture.session.HandleMessage(
      IpcMessage{IpcReadSampleMessage{.correlationId = 1, .sampleToken = 3}}));
  fixture.marshaller.RunAllPending();

  REQUIRE(fixture.captureQueue.Enqueued().size() == 1);
  CHECK(fixture.captureQueue.Enqueued().front().intentKey == 3);
}

TEST_CASE("AdapterIpcSession ends serving on a received Close, without "
          "replying") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);

  bool keepServing = fixture.session.HandleMessage(IpcMessage{
      IpcCloseMessage{.correlationId = 0, .reason = IpcCloseReason::kNormal}});

  CHECK_FALSE(keepServing);
  CHECK(connection.Sent().empty());
}

TEST_CASE("AdapterIpcSession keeps serving on a received Reject or Cancel") {
  SessionFixture fixture;

  CHECK(fixture.session.HandleMessage(IpcMessage{IpcRejectMessage{
      .correlationId = 1, .reason = IpcRejectReason::kMalformedPayload}}));
  CHECK(fixture.session.HandleMessage(
      IpcMessage{IpcCancelMessage{.correlationId = 1}}));
}

TEST_CASE("AdapterIpcSession rejects and ends serving on an unexpected "
          "adapter-outbound-only message kind") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);

  //  IpcHelloMessage is adapter-outbound only; the adapter should never
  //  receive one.
  bool keepServing = fixture.session.HandleMessage(
      IpcMessage{IpcHelloMessage{.correlationId = 5,
                                 .adapterInstanceId = SampleInstanceId().value,
                                 .peerProofToken = {}}});

  CHECK_FALSE(keepServing);
  REQUIRE(connection.Sent().size() == 1);
  auto *reject = std::get_if<IpcRejectMessage>(&connection.Sent().front());
  REQUIRE(reject != nullptr);
  CHECK(reject->correlationId == 5);
  CHECK(reject->reason == IpcRejectReason::kUnknownMessageKind);
}

TEST_CASE("AdapterIpcSession::HandleDecodeFailure sends a best-effort Close "
          "with reason kError") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);

  fixture.session.HandleDecodeFailure();

  REQUIRE(connection.Sent().size() == 1);
  auto *close = std::get_if<IpcCloseMessage>(&connection.Sent().front());
  REQUIRE(close != nullptr);
  CHECK(close->correlationId == 0);
  CHECK(close->reason == IpcCloseReason::kError);
}

TEST_CASE("AdapterIpcSession::HandleDecodeFailure does nothing without an "
          "attached connection") {
  SessionFixture fixture;

  fixture.session.HandleDecodeFailure();
}
