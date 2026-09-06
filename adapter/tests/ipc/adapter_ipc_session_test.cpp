#include "ipc/adapter_ipc_session.hpp"

#include "ipc/adapter_ipc_connection.hpp"
#include "ipc/adapter_ipc_hmac.hpp"
#include "ipc/adapter_ipc_peer_proof_provider.hpp"

#include <catch2/catch_test_macros.hpp>

#include <atomic>
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
using dovahlink::adapter::ipc::AdapterIpcMessageDisposition;
using dovahlink::adapter::ipc::AdapterIpcSession;
using dovahlink::adapter::ipc::AdapterIpcTarget;
using dovahlink::adapter::ipc::BuildHostProofMessage;
using dovahlink::adapter::ipc::ComputeIpcHmacSha256;
using dovahlink::adapter::ipc::FixedAdapterIpcPeerProofProvider;
using dovahlink::adapter::ipc::IAdapterIpcConnection;
using dovahlink::adapter::ipc::IAdapterPairingNotificationSink;
using dovahlink::adapter::ipc::IpcCancelMessage;
using dovahlink::adapter::ipc::IpcCloseMessage;
using dovahlink::adapter::ipc::IpcCloseReason;
using dovahlink::adapter::ipc::IpcHelloAckMessage;
using dovahlink::adapter::ipc::IpcHelloMessage;
using dovahlink::adapter::ipc::IpcHelloRejectReason;
using dovahlink::adapter::ipc::IpcListenEventMessage;
using dovahlink::adapter::ipc::IpcMessage;
using dovahlink::adapter::ipc::IpcPairingAttemptsExhaustedMessage;
using dovahlink::adapter::ipc::IpcPairingDisplayAckMessage;
using dovahlink::adapter::ipc::IpcPairingDisplayMessage;
using dovahlink::adapter::ipc::IpcReadSampleMessage;
using dovahlink::adapter::ipc::IpcRejectMessage;
using dovahlink::adapter::ipc::IpcRejectReason;
using dovahlink::adapter::ipc::IpcResynchronizeRequestMessage;
using dovahlink::adapter::ipc::IpcResynchronizeResultMessage;
using dovahlink::adapter::ipc::IpcTrustAdminRequestMessage;
using dovahlink::adapter::ipc::IpcTrustAdminResultMessage;
using dovahlink::adapter::ipc::kIpcOwnerLifetimeIdBytes;
using dovahlink::adapter::ipc::kMaxPendingGameThreadDispatches;
using dovahlink::adapter::ipc::kMaxPendingIpcCancellations;
using dovahlink::adapter::ipc::PairingDisplayMode;
using dovahlink::adapter::ipc::TrustAdminListScope;
using dovahlink::adapter::ipc::TrustAdminOperation;
using dovahlink::adapter::runtime::IAdapterTaskMarshaller;

namespace {

///  A fake `IAdapterTaskMarshaller` that stores tasks instead of running
///  them, so tests control exactly when marshaled work executes.
class FakeAdapterTaskMarshaller final : public IAdapterTaskMarshaller {
public:
  void RunOnGameThread(std::function<void()> task) override {
    if (throwOnNextSchedule_) {
      throwOnNextSchedule_ = false;
      throw std::runtime_error("RunOnGameThread failed");
    }
    pendingTasks_.push_back(std::move(task));
  }

  ///  Makes the next `RunOnGameThread` call throw instead of admitting its
  ///  task, so a test can prove a scheduling failure never leaks the
  ///  caller's pending-dispatch slot. Consumed by the call it affects; a
  ///  later `RunOnGameThread` call schedules normally again.
  void ThrowOnNextSchedule() { throwOnNextSchedule_ = true; }

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
  ///  Whether the next `RunOnGameThread` call should throw instead of
  ///  admitting its task.
  bool throwOnNextSchedule_ = false;
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

///  A fake `IAdapterPairingNotificationSink` that records every call and
///  returns a configurable accepted result.
class FakeAdapterPairingNotificationSink final
    : public IAdapterPairingNotificationSink {
public:
  bool Display(const std::string &code, PairingDisplayMode mode) override {
    displayed_.emplace_back(code, mode);
    return displayResult_;
  }

  void NotifyAttemptsExhausted() override { ++attemptsExhaustedCalls_; }

  ///  The (code, mode) pairs passed to `Display`, in call order.
  const std::vector<std::pair<std::string, PairingDisplayMode>> &
  Displayed() const {
    return displayed_;
  }

  ///  The number of times `NotifyAttemptsExhausted` was called.
  std::size_t AttemptsExhaustedCalls() const { return attemptsExhaustedCalls_; }

  ///  Sets the result `Display` returns for every subsequent call.
  void SetDisplayResult(bool result) { displayResult_ = result; }

private:
  std::vector<std::pair<std::string, PairingDisplayMode>> displayed_;
  std::size_t attemptsExhaustedCalls_ = 0;
  bool displayResult_ = true;
};

///  A fake `IAdapterIpcConnection` that records every message sent through
///  it, instead of any real transport.
class FakeAdapterIpcConnection final : public IAdapterIpcConnection {
public:
  void ConfigureTarget(AdapterIpcTarget) override {}

  void Start() override {}

  bool TrySend(const IpcMessage &message) override {
    if (blockNextSend_) {
      blockNextSend_ = false;
      blockedSendEntered_.set_value();
      blockedSendRelease_.get_future().wait();
    }
    bool accepted;
    if (rejectNextSend_) {
      rejectNextSend_ = false;
      accepted = false;
    } else {
      sent_.push_back(message);
      accepted = true;
    }
    return accepted;
  }

  void Stop() override {}

  const std::vector<IpcMessage> &Sent() const { return sent_; }

  ///  Discards every recorded message, so a test can assert on only what it
  ///  sends after this call (for example, after using `Authenticate` as
  ///  setup).
  void Clear() { sent_.clear(); }

  ///  Makes the next `TrySend` call report rejection (as a full outbound
  ///  queue would) instead of recording and accepting the message. Consumed
  ///  by the call it affects; a later `TrySend` accepts normally again.
  void RejectNextSend() { rejectNextSend_ = true; }

  ///  Makes the next `TrySend` call block, after signaling entry, until the
  ///  test calls `ReleaseBlockedSend`. Lets a test deterministically
  ///  interleave a concurrent event (for example destroying the owning
  ///  session) with a caller still inside `TrySend`, rather than relying on
  ///  a timing sleep to approximate that window.
  ///  @return A future that resolves once the blocked `TrySend` call has
  ///  actually entered and is waiting to be released.
  std::future<void> BlockNextSend() {
    blockNextSend_ = true;
    blockedSendEntered_ = std::promise<void>();
    blockedSendRelease_ = std::promise<void>();
    return blockedSendEntered_.get_future();
  }

  ///  Releases a `TrySend` call blocked by `BlockNextSend`.
  void ReleaseBlockedSend() { blockedSendRelease_.set_value(); }

private:
  std::vector<IpcMessage> sent_;
  ///  Whether the next `TrySend` call should report rejection.
  bool rejectNextSend_ = false;
  ///  Whether the next `TrySend` call should block until released.
  bool blockNextSend_ = false;
  ///  Resolved the instant a blocked `TrySend` call actually enters.
  std::promise<void> blockedSendEntered_;
  ///  Resolved by `ReleaseBlockedSend` to let a blocked `TrySend` call proceed.
  std::promise<void> blockedSendRelease_;
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

///  A representative, fixed owner-lifetime-id for tests that don't care
///  about its value.
std::array<std::byte, kIpcOwnerLifetimeIdBytes> SampleOwnerLifetimeId() {
  std::array<std::byte, kIpcOwnerLifetimeIdBytes> id{};
  for (std::size_t index = 0; index < id.size(); ++index) {
    id[index] = static_cast<std::byte>(100 + index);
  }
  return id;
}

///  Bundles a session with the fakes it was constructed against, so each
///  test can inspect them without repeating setup.
struct SessionFixture {
  FixedAdapterIpcPeerProofProvider peerProofProvider{
      {std::byte{9}, std::byte{8}, std::byte{7}}};
  AdapterIpcTarget target{
      .port = 58231,
      .proofToken = peerProofProvider.Token(),
      .hostProofKey = {std::byte{1}, std::byte{1}, std::byte{1}},
      .targetGeneration = 1,
  };
  FakeAdapterTaskMarshaller marshaller;
  FakeAdapterNativeDispatcher dispatcher;
  FakeAdapterCaptureHandoffQueue captureQueue;
  FakeAdapterPairingNotificationSink pairingNotificationSink;
  ///  The number of times `session` reported a rejected game-thread dispatch.
  std::size_t rejectedDispatchCount = 0;
  ///  When true, the rejection callback throws instead of just counting, so
  ///  a test can prove the exception is contained.
  bool throwOnRejectedDispatch = false;
  AdapterIpcSession session{SampleInstanceId(),
                            SampleOwnerLifetimeId(),
                            marshaller,
                            dispatcher,
                            captureQueue,
                            pairingNotificationSink,
                            [this] {
                              ++rejectedDispatchCount;
                              if (throwOnRejectedDispatch) {
                                throw std::runtime_error(
                                    "rejected-dispatch diagnostics failure");
                              }
                            }};
};

///  Drives a real Hello/HelloAck handshake to completion: connects with
///  `target`, captures the Hello the session actually sent, computes the
///  matching accepted HelloAck's `hostProof` from `target`'s HostProof key
///  (independent of the proof token that Hello itself carried), and delivers
///  it -- exactly the mutual-authentication proof a legitimate host would
///  produce. Clears `connection`'s recorded messages afterward so a test's
///  own assertions on `connection.Sent()` see only what happens next.
void Authenticate(AdapterIpcSession &session,
                  FakeAdapterIpcConnection &connection,
                  const AdapterIpcTarget &target) {
  session.HandleConnected(target);
  REQUIRE(connection.Sent().size() == 1);
  auto *hello = std::get_if<IpcHelloMessage>(&connection.Sent().front());
  REQUIRE(hello != nullptr);

  auto expectedProof = ComputeIpcHmacSha256(
      target.hostProofKey,
      BuildHostProofMessage(hello->challenge, hello->correlationId,
                            hello->adapterInstanceId, hello->ownerLifetimeId));

  AdapterIpcMessageDisposition disposition =
      session.HandleMessage(IpcMessage{IpcHelloAckMessage{
          .correlationId = hello->correlationId,
          .accepted = true,
          .rejectReason = IpcHelloRejectReason::kNone,
          .hostProof = expectedProof,
      }});
  REQUIRE(disposition == AdapterIpcMessageDisposition::kAuthenticated);
  REQUIRE(session.IsHostAvailable());
  connection.Clear();
}

} //  namespace

TEST_CASE("AdapterIpcSession::PrepareHello builds Hello from the configured "
          "identity, proof token, and owner-lifetime-id") {
  SessionFixture fixture;

  IpcMessage first = fixture.session.PrepareHello(fixture.target);
  auto *hello = std::get_if<IpcHelloMessage>(&first);
  REQUIRE(hello != nullptr);
  CHECK(hello->correlationId == 1);
  CHECK(hello->adapterInstanceId == SampleInstanceId().value);
  CHECK(hello->peerProofToken ==
        std::vector<std::byte>{std::byte{9}, std::byte{8}, std::byte{7}});
  CHECK(hello->ownerLifetimeId == SampleOwnerLifetimeId());

  IpcMessage second = fixture.session.PrepareHello(fixture.target);
  auto *secondHello = std::get_if<IpcHelloMessage>(&second);
  REQUIRE(secondHello != nullptr);
  CHECK(secondHello->correlationId == 2);
  //  A fresh, unpredictable challenge is generated for every Hello, so two
  //  successive calls must never produce the same one.
  CHECK(hello->challenge != secondHello->challenge);
}

TEST_CASE("AdapterIpcSession::HandleConnected sends Hello through the "
          "attached connection") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);

  fixture.session.HandleConnected(fixture.target);

  REQUIRE(connection.Sent().size() == 1);
  CHECK(std::holds_alternative<IpcHelloMessage>(connection.Sent().front()));
}

TEST_CASE("AdapterIpcSession::HandleConnected does nothing without an "
          "attached connection") {
  SessionFixture fixture;

  fixture.session.HandleConnected(fixture.target);
}

TEST_CASE("AdapterIpcSession marks the host available only after an "
          "accepted, matching-proof HelloAck") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  CHECK_FALSE(fixture.session.IsHostAvailable());

  Authenticate(fixture.session, connection, fixture.target);

  CHECK(fixture.session.IsHostAvailable());
}

TEST_CASE("AdapterIpcSession keeps the host unavailable after a rejected "
          "HelloAck") {
  SessionFixture fixture;

  CHECK(fixture.session.HandleMessage(IpcMessage{IpcHelloAckMessage{
            .correlationId = 1,
            .accepted = false,
            .rejectReason = IpcHelloRejectReason::kInvalidProof}}) ==
        AdapterIpcMessageDisposition::kClose);

  CHECK_FALSE(fixture.session.IsHostAvailable());
}

TEST_CASE("AdapterIpcSession keeps the host unavailable when an "
          "accepted HelloAck's hostProof is forged, wrong, or missing") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  fixture.session.HandleConnected(fixture.target);
  REQUIRE(connection.Sent().size() == 1);
  auto *hello = std::get_if<IpcHelloMessage>(&connection.Sent().front());
  REQUIRE(hello != nullptr);

  SECTION("missing (all-zero) hostProof") {
    fixture.session.HandleMessage(IpcMessage{
        IpcHelloAckMessage{.correlationId = hello->correlationId,
                           .accepted = true,
                           .rejectReason = IpcHelloRejectReason::kNone}});
  }

  SECTION("forged (arbitrary) hostProof") {
    std::array<std::byte, 32> forged{};
    forged.fill(std::byte{0xAB});
    fixture.session.HandleMessage(IpcMessage{
        IpcHelloAckMessage{.correlationId = hello->correlationId,
                           .accepted = true,
                           .rejectReason = IpcHelloRejectReason::kNone,
                           .hostProof = forged}});
  }

  SECTION("wrong hostProof (computed with the wrong key)") {
    auto wrongProof = ComputeIpcHmacSha256(
        std::vector<std::byte>{std::byte{1}, std::byte{2}, std::byte{3}},
        BuildHostProofMessage(hello->challenge, hello->correlationId,
                              hello->adapterInstanceId,
                              hello->ownerLifetimeId));
    fixture.session.HandleMessage(IpcMessage{
        IpcHelloAckMessage{.correlationId = hello->correlationId,
                           .accepted = true,
                           .rejectReason = IpcHelloRejectReason::kNone,
                           .hostProof = wrongProof}});
  }

  SECTION("hostProof keyed by the bearer proof token instead of the "
          "HostProof key") {
    //  Proves the domain-separation invariant this fields split exists for:
    //  a HelloAck computed with the value this adapter itself presented in
    //  Hello -- exactly what an observer of that Hello alone would have --
    //  is still rejected. Only the independent, never-transmitted
    //  hostProofKey can produce a valid HostProof.
    auto proofKeyedByBearerToken = ComputeIpcHmacSha256(
        fixture.target.proofToken,
        BuildHostProofMessage(hello->challenge, hello->correlationId,
                              hello->adapterInstanceId,
                              hello->ownerLifetimeId));
    fixture.session.HandleMessage(IpcMessage{
        IpcHelloAckMessage{.correlationId = hello->correlationId,
                           .accepted = true,
                           .rejectReason = IpcHelloRejectReason::kNone,
                           .hostProof = proofKeyedByBearerToken}});
  }

  SECTION("replayed hostProof (valid for a different, earlier challenge)") {
    //  A prior, separate handshake's genuinely-correct proof, replayed
    //  against this attempt's different challenge/correlationId.
    std::array<std::byte, 32> staleChallenge{};
    staleChallenge.fill(std::byte{0x11});
    auto staleProof = ComputeIpcHmacSha256(
        fixture.target.hostProofKey,
        BuildHostProofMessage(staleChallenge, 999, hello->adapterInstanceId,
                              hello->ownerLifetimeId));
    fixture.session.HandleMessage(IpcMessage{
        IpcHelloAckMessage{.correlationId = hello->correlationId,
                           .accepted = true,
                           .rejectReason = IpcHelloRejectReason::kNone,
                           .hostProof = staleProof}});
  }

  CHECK_FALSE(fixture.session.IsHostAvailable());
}

TEST_CASE("AdapterIpcSession keeps the host unavailable when an accepted "
          "HelloAck's correlation id does not match the outstanding Hello") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  fixture.session.HandleConnected(fixture.target);
  REQUIRE(connection.Sent().size() == 1);
  auto *hello = std::get_if<IpcHelloMessage>(&connection.Sent().front());
  REQUIRE(hello != nullptr);

  //  A genuinely-correct proof for a *different* correlation id than the
  //  outstanding Hello's -- the mismatch alone must reject it, even though
  //  the proof math is otherwise valid for that other id.
  auto proofForDifferentCorrelationId = ComputeIpcHmacSha256(
      fixture.target.hostProofKey,
      BuildHostProofMessage(hello->challenge, hello->correlationId + 1,
                            hello->adapterInstanceId, hello->ownerLifetimeId));

  fixture.session.HandleMessage(IpcMessage{
      IpcHelloAckMessage{.correlationId = hello->correlationId + 1,
                         .accepted = true,
                         .rejectReason = IpcHelloRejectReason::kNone,
                         .hostProof = proofForDifferentCorrelationId}});

  CHECK_FALSE(fixture.session.IsHostAvailable());
}

TEST_CASE("AdapterIpcSession closes on a duplicate rejected HelloAck and "
          "waits for disconnect to become unavailable") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);
  REQUIRE(fixture.session.IsHostAvailable());

  CHECK(fixture.session.HandleMessage(IpcMessage{IpcHelloAckMessage{
            .correlationId = 999,
            .accepted = false,
            .rejectReason = IpcHelloRejectReason::kInvalidProof}}) ==
        AdapterIpcMessageDisposition::kClose);

  CHECK(fixture.session.IsHostAvailable());
  fixture.session.HandleDisconnected();
  CHECK_FALSE(fixture.session.IsHostAvailable());
}

TEST_CASE("AdapterIpcSession::HandleDisconnected marks the host "
          "unavailable") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);
  REQUIRE(fixture.session.IsHostAvailable());

  fixture.session.HandleDisconnected();

  CHECK_FALSE(fixture.session.IsHostAvailable());
}

TEST_CASE("AdapterIpcSession handles a resynchronize request by marshaling "
          "a task that reports no baseline is available") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);

  CHECK(fixture.session.HandleMessage(IpcMessage{IpcResynchronizeRequestMessage{
            .correlationId = 42}}) == AdapterIpcMessageDisposition::kContinue);

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

TEST_CASE("AdapterIpcSession closes for a pre-authentication resynchronize "
          "request without an attached connection") {
  SessionFixture fixture;

  CHECK(fixture.session.HandleMessage(IpcMessage{IpcResynchronizeRequestMessage{
            .correlationId = 1}}) == AdapterIpcMessageDisposition::kClose);
  CHECK(fixture.marshaller.PendingCount() == 0);
}

TEST_CASE("AdapterIpcSession::HandleClosing is a harmless no-op on a session "
          "that never connected") {
  SessionFixture fixture;

  REQUIRE_NOTHROW(fixture.session.HandleClosing());

  CHECK_FALSE(fixture.session.IsHostAvailable());
  //  A subsequent, legitimate connection must still authenticate normally:
  //  the no-op call must not have left the session in some closed-forever
  //  state.
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);
}

TEST_CASE("AdapterIpcSession drops pending game-thread work after session "
          "destruction") {
  FixedAdapterIpcPeerProofProvider peerProofProvider{
      {std::byte{9}, std::byte{8}, std::byte{7}}};
  AdapterIpcTarget target{
      .port = 58231,
      .proofToken = peerProofProvider.Token(),
      .hostProofKey = {std::byte{1}, std::byte{1}, std::byte{1}},
      .targetGeneration = 1,
  };
  FakeAdapterTaskMarshaller marshaller;
  FakeAdapterNativeDispatcher dispatcher;
  FakeAdapterCaptureHandoffQueue captureQueue;
  FakeAdapterPairingNotificationSink pairingNotificationSink;
  FakeAdapterIpcConnection connection;

  {
    AdapterIpcSession session{SampleInstanceId(), SampleOwnerLifetimeId(),
                              marshaller,         dispatcher,
                              captureQueue,       pairingNotificationSink};
    session.AttachConnection(connection);
    Authenticate(session, connection, target);
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
  Authenticate(fixture.session, connection, fixture.target);

  fixture.session.HandleMessage(
      IpcMessage{IpcResynchronizeRequestMessage{.correlationId = 42}});
  fixture.session.HandleDisconnected();
  fixture.marshaller.RunAllPending();

  CHECK(connection.Sent().empty());
}

TEST_CASE("AdapterIpcSession drops a pending listen-event request after "
          "disconnect") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);
  fixture.dispatcher.SetResult(7, {std::byte{1}});

  fixture.session.HandleMessage(
      IpcMessage{IpcListenEventMessage{.correlationId = 1, .eventKey = 7}});
  fixture.session.HandleDisconnected();
  fixture.marshaller.RunAllPending();

  CHECK(fixture.dispatcher.DispatchedKeys().empty());
  CHECK(fixture.captureQueue.Enqueued().empty());
}

TEST_CASE("AdapterIpcSession drops a pending read-sample request after "
          "disconnect") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);
  fixture.dispatcher.SetResult(8, {std::byte{2}});

  fixture.session.HandleMessage(
      IpcMessage{IpcReadSampleMessage{.correlationId = 1, .sampleToken = 8}});
  fixture.session.HandleDisconnected();
  fixture.marshaller.RunAllPending();

  CHECK(fixture.dispatcher.DispatchedKeys().empty());
  CHECK(fixture.captureQueue.Enqueued().empty());
}

TEST_CASE("AdapterIpcSession drops a pending resynchronization result after "
          "logical closing, even before the physical disconnect notifies "
          "the session") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);

  fixture.session.HandleMessage(
      IpcMessage{IpcResynchronizeRequestMessage{.correlationId = 42}});
  //  HandleClosing alone, deliberately never followed by HandleDisconnected
  //  in this test: the transport's physical teardown (drain/socket close)
  //  can take a while after serving has already irreversibly ended, and a
  //  task marshaled before that point must not wait for the later physical
  //  disconnect to be rejected.
  fixture.session.HandleClosing();
  fixture.marshaller.RunAllPending();

  CHECK(connection.Sent().empty());
}

TEST_CASE("AdapterIpcSession drops a pending listen-event request after "
          "logical closing, even before the physical disconnect notifies "
          "the session") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);
  fixture.dispatcher.SetResult(7, {std::byte{1}});

  fixture.session.HandleMessage(
      IpcMessage{IpcListenEventMessage{.correlationId = 1, .eventKey = 7}});
  fixture.session.HandleClosing();
  fixture.marshaller.RunAllPending();

  CHECK(fixture.dispatcher.DispatchedKeys().empty());
  CHECK(fixture.captureQueue.Enqueued().empty());
}

TEST_CASE("AdapterIpcSession drops a pending read-sample request after "
          "logical closing, even before the physical disconnect notifies "
          "the session") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);
  fixture.dispatcher.SetResult(8, {std::byte{2}});

  fixture.session.HandleMessage(
      IpcMessage{IpcReadSampleMessage{.correlationId = 1, .sampleToken = 8}});
  fixture.session.HandleClosing();
  fixture.marshaller.RunAllPending();

  CHECK(fixture.dispatcher.DispatchedKeys().empty());
  CHECK(fixture.captureQueue.Enqueued().empty());
}

TEST_CASE("AdapterIpcSession::HandleClosing and HandleDisconnected "
          "cooperate safely regardless of order, leaving reconnection "
          "unaffected") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);
  fixture.dispatcher.SetResult(7, {std::byte{1}});

  fixture.session.HandleMessage(
      IpcMessage{IpcListenEventMessage{.correlationId = 1, .eventKey = 7}});
  //  Both fire for the same generation, as production does: onClosing then
  //  onDisconnected. Neither may re-open eligibility for the other, and the
  //  pair together must still leave the session able to authenticate a
  //  fresh generation afterward.
  fixture.session.HandleClosing();
  fixture.session.HandleDisconnected();
  REQUIRE_FALSE(fixture.session.IsHostAvailable());

  Authenticate(fixture.session, connection, fixture.target);
  fixture.dispatcher.SetResult(8, {std::byte{2}});
  fixture.session.HandleMessage(
      IpcMessage{IpcReadSampleMessage{.correlationId = 2, .sampleToken = 8}});
  fixture.marshaller.RunAllPending();

  //  The listen-event queued against the closed generation stayed dropped;
  //  only the read-sample queued against the new generation dispatched.
  CHECK(fixture.dispatcher.DispatchedKeys() == std::vector<std::uint32_t>{8});
  CHECK(fixture.captureQueue.Enqueued().size() == 1);
}

TEST_CASE("AdapterIpcSession drops pending intent requests from an older "
          "generation after reconnect") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);
  fixture.dispatcher.SetResult(7, {std::byte{1}});
  fixture.dispatcher.SetResult(8, {std::byte{2}});

  fixture.session.HandleMessage(
      IpcMessage{IpcListenEventMessage{.correlationId = 1, .eventKey = 7}});
  fixture.session.HandleMessage(
      IpcMessage{IpcReadSampleMessage{.correlationId = 2, .sampleToken = 8}});
  fixture.session.HandleDisconnected();
  fixture.session.HandleConnected(fixture.target);
  fixture.marshaller.RunAllPending();

  CHECK(fixture.dispatcher.DispatchedKeys().empty());
  CHECK(fixture.captureQueue.Enqueued().empty());
}

TEST_CASE("AdapterIpcSession can authenticate again after reconnecting") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);
  REQUIRE(fixture.session.IsHostAvailable());

  fixture.session.HandleDisconnected();
  REQUIRE_FALSE(fixture.session.IsHostAvailable());
  fixture.session.HandleConnected(fixture.target);
  connection.Clear();

  Authenticate(fixture.session, connection, fixture.target);

  CHECK(fixture.session.IsHostAvailable());
}

TEST_CASE("AdapterIpcSession does not let a cancellation from an earlier "
          "connection generation cancel a same-numbered request on a later "
          "generation") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);

  //  The host cancels correlation id 1 on the first generation. No matching
  //  request is pending yet; HandleCancel still records the tombstone.
  CHECK(fixture.session.HandleMessage(IpcMessage{IpcCancelMessage{
            .correlationId = 1}}) == AdapterIpcMessageDisposition::kContinue);

  fixture.session.HandleDisconnected();
  fixture.session.HandleConnected(fixture.target);
  connection.Clear();
  Authenticate(fixture.session, connection, fixture.target);

  //  The new generation's host reuses correlation id 1 for an unrelated
  //  request; the stale cancellation from the old generation must not apply.
  fixture.dispatcher.SetResult(7, {std::byte{1}});
  CHECK(fixture.session.HandleMessage(IpcMessage{
            IpcListenEventMessage{.correlationId = 1, .eventKey = 7}}) ==
        AdapterIpcMessageDisposition::kContinue);
  fixture.marshaller.RunAllPending();

  CHECK(fixture.dispatcher.DispatchedKeys() == std::vector<std::uint32_t>{7});
  REQUIRE(fixture.captureQueue.Enqueued().size() == 1);
  CHECK(fixture.captureQueue.Enqueued().front().intentKey == 7);
}

TEST_CASE("AdapterIpcSession destruction waits for an in-flight game-thread "
          "callback before returning") {
  FixedAdapterIpcPeerProofProvider peerProofProvider{
      {std::byte{9}, std::byte{8}, std::byte{7}}};
  AdapterIpcTarget target{
      .port = 58231,
      .proofToken = peerProofProvider.Token(),
      .hostProofKey = {std::byte{1}, std::byte{1}, std::byte{1}},
      .targetGeneration = 1,
  };
  FakeAdapterTaskMarshaller marshaller;
  std::promise<void> enteredPromise;
  std::shared_future<void> enteredFuture = enteredPromise.get_future();
  std::promise<void> releasePromise;
  std::shared_future<void> releaseFuture = releasePromise.get_future().share();
  BlockingAdapterNativeDispatcher dispatcher{enteredPromise, releaseFuture};
  FakeAdapterCaptureHandoffQueue captureQueue;
  FakeAdapterPairingNotificationSink pairingNotificationSink;
  FakeAdapterIpcConnection connection;
  auto session = std::make_unique<AdapterIpcSession>(
      SampleInstanceId(), SampleOwnerLifetimeId(), marshaller, dispatcher,
      captureQueue, pairingNotificationSink);
  session->AttachConnection(connection);
  Authenticate(*session, connection, target);
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

TEST_CASE("AdapterIpcSession's queued game-thread dispatch stays safe to run "
          "after the session itself has been destroyed") {
  //  Complementary to the test above: that one covers a task already
  //  running and holding callbackMutex_ when destruction begins, which
  //  ~AdapterIpcSession() waits for. This covers a task still sitting
  //  unstarted in the marshaller's queue at that same moment -- the
  //  destructor's own lock never blocks on that task, since it hasn't
  //  reached callbackMutex_ yet. Running it here, after the session is
  //  gone, only stays well-defined because nothing in the scheduled
  //  closure reaches through `this` before its own lifetime gate.
  FixedAdapterIpcPeerProofProvider peerProofProvider{
      {std::byte{9}, std::byte{8}, std::byte{7}}};
  AdapterIpcTarget target{
      .port = 58231,
      .proofToken = peerProofProvider.Token(),
      .hostProofKey = {std::byte{1}, std::byte{1}, std::byte{1}},
      .targetGeneration = 1,
  };
  FakeAdapterTaskMarshaller marshaller;
  FakeAdapterNativeDispatcher dispatcher;
  FakeAdapterCaptureHandoffQueue captureQueue;
  FakeAdapterPairingNotificationSink pairingNotificationSink;
  FakeAdapterIpcConnection connection;
  auto session = std::make_unique<AdapterIpcSession>(
      SampleInstanceId(), SampleOwnerLifetimeId(), marshaller, dispatcher,
      captureQueue, pairingNotificationSink);
  session->AttachConnection(connection);
  Authenticate(*session, connection, target);

  dispatcher.SetResult(7, {std::byte{1}});
  session->HandleMessage(
      IpcMessage{IpcListenEventMessage{.correlationId = 1, .eventKey = 7}});
  REQUIRE(marshaller.PendingCount() == 1);

  session.reset();

  //  Must complete without touching freed session memory.
  marshaller.RunAllPending();
}

TEST_CASE("AdapterIpcSession closes on a pre-authentication "
          "ResynchronizeResult without sending a response") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);

  AdapterIpcMessageDisposition disposition =
      fixture.session.HandleMessage(IpcMessage{
          IpcResynchronizeResultMessage{.correlationId = 7, .accepted = true}});

  CHECK(disposition == AdapterIpcMessageDisposition::kClose);
  CHECK(connection.Sent().empty());
}

TEST_CASE("AdapterIpcSession handles a listen-event request by dispatching "
          "the key on the game thread and enqueuing a captured value") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);
  fixture.dispatcher.SetResult(7, {std::byte{1}, std::byte{2}});

  CHECK(fixture.session.HandleMessage(IpcMessage{
            IpcListenEventMessage{.correlationId = 1, .eventKey = 7}}) ==
        AdapterIpcMessageDisposition::kContinue);

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
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);

  fixture.session.HandleMessage(
      IpcMessage{IpcListenEventMessage{.correlationId = 1, .eventKey = 99}});
  fixture.marshaller.RunAllPending();

  CHECK(fixture.captureQueue.Enqueued().empty());
}

TEST_CASE("AdapterIpcSession enqueues nothing for a read-sample token with "
          "no registered translation") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);

  fixture.session.HandleMessage(
      IpcMessage{IpcReadSampleMessage{.correlationId = 1, .sampleToken = 99}});
  fixture.marshaller.RunAllPending();

  CHECK(fixture.dispatcher.DispatchedKeys() == std::vector<std::uint32_t>{99});
  CHECK(fixture.captureQueue.Enqueued().empty());
}

TEST_CASE("AdapterIpcSession contains an exception thrown by the "
          "dispatcher inside a marshaled listen-event task") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);
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
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);
  fixture.dispatcher.SetResult(3, {std::byte{5}});

  CHECK(fixture.session.HandleMessage(IpcMessage{
            IpcReadSampleMessage{.correlationId = 1, .sampleToken = 3}}) ==
        AdapterIpcMessageDisposition::kContinue);
  fixture.marshaller.RunAllPending();

  REQUIRE(fixture.captureQueue.Enqueued().size() == 1);
  CHECK(fixture.captureQueue.Enqueued().front().intentKey == 3);
}

TEST_CASE("AdapterIpcSession never dispatches a listen-event or read-sample "
          "request received before any accepted, matching-proof HelloAck") {
  SessionFixture fixture;
  fixture.dispatcher.SetResult(7, {std::byte{1}});
  fixture.dispatcher.SetResult(8, {std::byte{2}});

  fixture.session.HandleMessage(
      IpcMessage{IpcListenEventMessage{.correlationId = 1, .eventKey = 7}});
  fixture.session.HandleMessage(
      IpcMessage{IpcReadSampleMessage{.correlationId = 2, .sampleToken = 8}});

  CHECK(fixture.marshaller.PendingCount() == 0);
  CHECK(fixture.dispatcher.DispatchedKeys().empty());
  CHECK(fixture.captureQueue.Enqueued().empty());
}

TEST_CASE("AdapterIpcSession never dispatches a listen-event marshaled "
          "while authenticated if a later HelloAck rejects the peer before "
          "the marshaled task runs") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);
  fixture.dispatcher.SetResult(7, {std::byte{1}});

  //  Enqueued while authenticated, so it passes the enqueue-time gate and is
  //  marshaled.
  fixture.session.HandleMessage(
      IpcMessage{IpcListenEventMessage{.correlationId = 1, .eventKey = 7}});
  REQUIRE(fixture.marshaller.PendingCount() == 1);

  //  A second HelloAck on the *same* connection is a protocol violation. It
  //  must close the transport rather than changing the authenticated state
  //  directly; the connection reports the physical teardown separately.
  CHECK(fixture.session.HandleMessage(IpcMessage{IpcHelloAckMessage{
            .correlationId = 999,
            .accepted = false,
            .rejectReason = IpcHelloRejectReason::kInvalidProof}}) ==
        AdapterIpcMessageDisposition::kClose);
  REQUIRE(fixture.session.IsHostAvailable());
  fixture.session.HandleDisconnected();
  REQUIRE_FALSE(fixture.session.IsHostAvailable());

  fixture.marshaller.RunAllPending();

  CHECK(fixture.dispatcher.DispatchedKeys().empty());
  CHECK(fixture.captureQueue.Enqueued().empty());
}

TEST_CASE("AdapterIpcSession never dispatches a listen-event or read-sample "
          "request received while the peer is rejected") {
  SessionFixture fixture;
  fixture.dispatcher.SetResult(7, {std::byte{1}});
  fixture.dispatcher.SetResult(8, {std::byte{2}});
  fixture.session.HandleMessage(IpcMessage{
      IpcHelloAckMessage{.correlationId = 1,
                         .accepted = false,
                         .rejectReason = IpcHelloRejectReason::kInvalidProof}});
  REQUIRE_FALSE(fixture.session.IsHostAvailable());

  fixture.session.HandleMessage(
      IpcMessage{IpcListenEventMessage{.correlationId = 1, .eventKey = 7}});
  fixture.session.HandleMessage(
      IpcMessage{IpcReadSampleMessage{.correlationId = 2, .sampleToken = 8}});

  CHECK(fixture.marshaller.PendingCount() == 0);
  CHECK(fixture.dispatcher.DispatchedKeys().empty());
  CHECK(fixture.captureQueue.Enqueued().empty());
}

TEST_CASE("AdapterIpcSession ends serving on a received Close, without "
          "replying") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);

  AdapterIpcMessageDisposition disposition =
      fixture.session.HandleMessage(IpcMessage{IpcCloseMessage{
          .correlationId = 0, .reason = IpcCloseReason::kNormal}});

  CHECK(disposition == AdapterIpcMessageDisposition::kClose);
  CHECK(connection.Sent().empty());
}

TEST_CASE("AdapterIpcSession keeps serving on a received Reject or Cancel "
          "after authentication") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);

  CHECK(
      fixture.session.HandleMessage(IpcMessage{IpcRejectMessage{
          .correlationId = 1, .reason = IpcRejectReason::kMalformedPayload}}) ==
      AdapterIpcMessageDisposition::kContinue);
  CHECK(fixture.session.HandleMessage(IpcMessage{IpcCancelMessage{
            .correlationId = 1}}) == AdapterIpcMessageDisposition::kContinue);
}

TEST_CASE("AdapterIpcSession closes on an unexpected message kind before "
          "authentication without sending a response") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);

  //  IpcHelloMessage is adapter-outbound only; the adapter should never
  //  receive one.
  AdapterIpcMessageDisposition disposition = fixture.session.HandleMessage(
      IpcMessage{IpcHelloMessage{.correlationId = 5,
                                 .adapterInstanceId = SampleInstanceId().value,
                                 .peerProofToken = {}}});

  CHECK(disposition == AdapterIpcMessageDisposition::kClose);
  CHECK(connection.Sent().empty());
}

TEST_CASE("AdapterIpcSession rejects and closes an unexpected message kind "
          "after authentication") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);

  AdapterIpcMessageDisposition disposition =
      fixture.session.HandleMessage(IpcMessage{
          IpcResynchronizeResultMessage{.correlationId = 5, .accepted = true}});

  CHECK(disposition == AdapterIpcMessageDisposition::kClose);
  REQUIRE(connection.Sent().size() == 1);
  auto *reject = std::get_if<IpcRejectMessage>(&connection.Sent().front());
  REQUIRE(reject != nullptr);
  CHECK(reject->correlationId == 5);
  CHECK(reject->reason == IpcRejectReason::kUnknownMessageKind);
}

TEST_CASE("AdapterIpcSession closes for every non-HelloAck message before "
          "authentication") {
  SessionFixture fixture;

  CHECK(fixture.session.HandleMessage(IpcMessage{IpcResynchronizeRequestMessage{
            .correlationId = 1}}) == AdapterIpcMessageDisposition::kClose);
  CHECK(fixture.marshaller.PendingCount() == 0);

  SessionFixture listenFixture;
  CHECK(listenFixture.session.HandleMessage(IpcMessage{
            IpcListenEventMessage{.correlationId = 2, .eventKey = 7}}) ==
        AdapterIpcMessageDisposition::kClose);
  CHECK(listenFixture.marshaller.PendingCount() == 0);

  SessionFixture readFixture;
  CHECK(readFixture.session.HandleMessage(IpcMessage{
            IpcReadSampleMessage{.correlationId = 3, .sampleToken = 8}}) ==
        AdapterIpcMessageDisposition::kClose);
  CHECK(readFixture.marshaller.PendingCount() == 0);

  SessionFixture rejectFixture;
  CHECK(
      rejectFixture.session.HandleMessage(IpcMessage{IpcRejectMessage{
          .correlationId = 4, .reason = IpcRejectReason::kMalformedPayload}}) ==
      AdapterIpcMessageDisposition::kClose);

  SessionFixture cancelFixture;
  CHECK(cancelFixture.session.HandleMessage(IpcMessage{IpcCancelMessage{
            .correlationId = 5}}) == AdapterIpcMessageDisposition::kClose);
}

TEST_CASE("AdapterIpcSession rejects an accepted HelloAck before a transport "
          "has connected") {
  SessionFixture fixture;
  auto proof = ComputeIpcHmacSha256(
      fixture.target.hostProofKey,
      BuildHostProofMessage({}, 0, SampleInstanceId().value,
                            SampleOwnerLifetimeId()));

  CHECK(fixture.session.HandleMessage(IpcMessage{IpcHelloAckMessage{
            .correlationId = 0,
            .accepted = true,
            .rejectReason = IpcHelloRejectReason::kNone,
            .hostProof = proof}}) == AdapterIpcMessageDisposition::kClose);
  CHECK_FALSE(fixture.session.IsHostAvailable());
}

TEST_CASE("AdapterIpcSession closes every subsequent message after the "
          "transport disconnects") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);
  fixture.session.HandleDisconnected();

  CHECK(fixture.session.HandleMessage(IpcMessage{
            IpcListenEventMessage{.correlationId = 1, .eventKey = 7}}) ==
        AdapterIpcMessageDisposition::kClose);
  CHECK(fixture.session.HandleMessage(IpcMessage{
            IpcHelloAckMessage{.correlationId = 2,
                               .accepted = true,
                               .rejectReason = IpcHelloRejectReason::kNone}}) ==
        AdapterIpcMessageDisposition::kClose);
  CHECK(fixture.marshaller.PendingCount() == 0);
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

TEST_CASE("AdapterIpcSession rejects a deferred game-thread dispatch once "
          "the pending bound is reached, without scheduling it") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);

  for (std::uint32_t eventKey = 1; eventKey <= kMaxPendingGameThreadDispatches;
       ++eventKey) {
    fixture.session.HandleMessage(IpcMessage{IpcListenEventMessage{
        .correlationId = eventKey, .eventKey = eventKey}});
  }
  REQUIRE(fixture.marshaller.PendingCount() == kMaxPendingGameThreadDispatches);
  CHECK(fixture.rejectedDispatchCount == 0);

  fixture.session.HandleMessage(IpcMessage{IpcListenEventMessage{
      .correlationId = kMaxPendingGameThreadDispatches + 1,
      .eventKey = kMaxPendingGameThreadDispatches + 1}});

  //  The rejected request is never scheduled: the pending count does not
  //  grow past the bound.
  CHECK(fixture.marshaller.PendingCount() == kMaxPendingGameThreadDispatches);
  CHECK(fixture.rejectedDispatchCount == 1);
}

TEST_CASE("AdapterIpcSession admits a new dispatch once previously pending "
          "ones have run and freed their slot") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);

  for (std::uint32_t eventKey = 1; eventKey <= kMaxPendingGameThreadDispatches;
       ++eventKey) {
    fixture.session.HandleMessage(IpcMessage{IpcListenEventMessage{
        .correlationId = eventKey, .eventKey = eventKey}});
  }
  fixture.session.HandleMessage(IpcMessage{IpcListenEventMessage{
      .correlationId = kMaxPendingGameThreadDispatches + 1,
      .eventKey = kMaxPendingGameThreadDispatches + 1}});
  REQUIRE(fixture.rejectedDispatchCount == 1);

  fixture.marshaller.RunAllPending();

  fixture.session.HandleMessage(
      IpcMessage{IpcListenEventMessage{.correlationId = 900, .eventKey = 900}});

  CHECK(fixture.marshaller.PendingCount() == 1);
  //  No new rejection: the earlier tasks running freed their slots.
  CHECK(fixture.rejectedDispatchCount == 1);
}

TEST_CASE("AdapterIpcSession shares its pending game-thread dispatch bound "
          "across resynchronization, listen-event, and read-sample "
          "requests") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);

  fixture.session.HandleMessage(
      IpcMessage{IpcResynchronizeRequestMessage{.correlationId = 1}});
  fixture.session.HandleMessage(
      IpcMessage{IpcReadSampleMessage{.correlationId = 2, .sampleToken = 1}});
  for (std::uint32_t eventKey = 1;
       eventKey <= kMaxPendingGameThreadDispatches - 2; ++eventKey) {
    fixture.session.HandleMessage(IpcMessage{IpcListenEventMessage{
        .correlationId = eventKey + 2, .eventKey = eventKey}});
  }
  REQUIRE(fixture.marshaller.PendingCount() == kMaxPendingGameThreadDispatches);
  CHECK(fixture.rejectedDispatchCount == 0);

  fixture.session.HandleMessage(IpcMessage{
      IpcReadSampleMessage{.correlationId = 999, .sampleToken = 999}});

  CHECK(fixture.marshaller.PendingCount() == kMaxPendingGameThreadDispatches);
  CHECK(fixture.rejectedDispatchCount == 1);
}

TEST_CASE("AdapterIpcSession releases its pending-dispatch slot when "
          "RunOnGameThread throws instead of admitting the task") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);

  fixture.marshaller.ThrowOnNextSchedule();
  fixture.session.HandleMessage(
      IpcMessage{IpcListenEventMessage{.correlationId = 1, .eventKey = 1}});

  //  The failed admission was reported and never reached the marshaller's
  //  pending queue.
  CHECK(fixture.rejectedDispatchCount == 1);
  CHECK(fixture.marshaller.PendingCount() == 0);

  //  A fresh dispatch is still admitted afterward: the failed attempt above
  //  did not leak its slot.
  fixture.session.HandleMessage(
      IpcMessage{IpcListenEventMessage{.correlationId = 2, .eventKey = 2}});

  CHECK(fixture.marshaller.PendingCount() == 1);
  CHECK(fixture.rejectedDispatchCount == 1);
}

TEST_CASE("AdapterIpcSession recovers from repeated game-thread scheduling "
          "failures without leaking any pending-dispatch slot") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);

  //  Every one of these fails to schedule; if a single failure ever leaked
  //  its slot, this loop alone would exhaust the bound and the assertions
  //  below would see rejections caused by admission, not by the induced
  //  throw.
  for (std::uint32_t eventKey = 1; eventKey <= kMaxPendingGameThreadDispatches;
       ++eventKey) {
    fixture.marshaller.ThrowOnNextSchedule();
    fixture.session.HandleMessage(IpcMessage{IpcListenEventMessage{
        .correlationId = eventKey, .eventKey = eventKey}});
  }
  CHECK(fixture.marshaller.PendingCount() == 0);
  CHECK(fixture.rejectedDispatchCount == kMaxPendingGameThreadDispatches);

  //  A dispatch that succeeds is still admitted after that many failures.
  fixture.session.HandleMessage(
      IpcMessage{IpcListenEventMessage{.correlationId = 900, .eventKey = 900}});

  CHECK(fixture.marshaller.PendingCount() == 1);
  CHECK(fixture.rejectedDispatchCount == kMaxPendingGameThreadDispatches);
}

TEST_CASE("AdapterIpcSession contains an exception thrown by the "
          "game-thread-dispatch-rejected callback, and still reports "
          "kContinue for the rejected request") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);

  for (std::uint32_t eventKey = 1; eventKey <= kMaxPendingGameThreadDispatches;
       ++eventKey) {
    fixture.session.HandleMessage(IpcMessage{IpcListenEventMessage{
        .correlationId = eventKey, .eventKey = eventKey}});
  }
  REQUIRE(fixture.marshaller.PendingCount() == kMaxPendingGameThreadDispatches);
  fixture.throwOnRejectedDispatch = true;

  AdapterIpcMessageDisposition disposition =
      AdapterIpcMessageDisposition::kClose;
  REQUIRE_NOTHROW(disposition = fixture.session.HandleMessage(
                      IpcMessage{IpcListenEventMessage{
                          .correlationId = kMaxPendingGameThreadDispatches + 1,
                          .eventKey = kMaxPendingGameThreadDispatches + 1}}));
  CHECK(disposition == AdapterIpcMessageDisposition::kContinue);
  CHECK(fixture.rejectedDispatchCount == 1);
}

TEST_CASE("AdapterIpcSession cancels a listen-event dispatch received "
          "before its marshaled task runs") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);
  fixture.dispatcher.SetResult(7, {std::byte{1}});

  CHECK(fixture.session.HandleMessage(IpcMessage{
            IpcListenEventMessage{.correlationId = 11, .eventKey = 7}}) ==
        AdapterIpcMessageDisposition::kContinue);
  REQUIRE(fixture.marshaller.PendingCount() == 1);

  CHECK(fixture.session.HandleMessage(IpcMessage{IpcCancelMessage{
            .correlationId = 11}}) == AdapterIpcMessageDisposition::kContinue);

  fixture.marshaller.RunAllPending();

  CHECK(fixture.dispatcher.DispatchedKeys().empty());
  CHECK(fixture.captureQueue.Enqueued().empty());
}

TEST_CASE("AdapterIpcSession cancelling a request after its marshaled task "
          "already ran has no effect") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);
  fixture.dispatcher.SetResult(7, {std::byte{1}});

  fixture.session.HandleMessage(
      IpcMessage{IpcListenEventMessage{.correlationId = 11, .eventKey = 7}});
  fixture.marshaller.RunAllPending();

  REQUIRE(fixture.dispatcher.DispatchedKeys() == std::vector<std::uint32_t>{7});
  REQUIRE(fixture.captureQueue.Enqueued().size() == 1);

  CHECK(fixture.session.HandleMessage(IpcMessage{IpcCancelMessage{
            .correlationId = 11}}) == AdapterIpcMessageDisposition::kContinue);

  //  The already-produced result is unaffected: cancellation cannot undo
  //  work that already happened.
  CHECK(fixture.dispatcher.DispatchedKeys() == std::vector<std::uint32_t>{7});
  CHECK(fixture.captureQueue.Enqueued().size() == 1);
}

TEST_CASE("AdapterIpcSession cancels only the listen-event request whose "
          "correlation id matches the cancellation") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);
  fixture.dispatcher.SetResult(7, {std::byte{1}});
  fixture.dispatcher.SetResult(8, {std::byte{2}});

  fixture.session.HandleMessage(
      IpcMessage{IpcListenEventMessage{.correlationId = 11, .eventKey = 7}});
  fixture.session.HandleMessage(
      IpcMessage{IpcListenEventMessage{.correlationId = 12, .eventKey = 8}});
  fixture.session.HandleMessage(
      IpcMessage{IpcCancelMessage{.correlationId = 11}});

  fixture.marshaller.RunAllPending();

  CHECK(fixture.dispatcher.DispatchedKeys() == std::vector<std::uint32_t>{8});
  REQUIRE(fixture.captureQueue.Enqueued().size() == 1);
  CHECK(fixture.captureQueue.Enqueued().front().intentKey == 8);
}

TEST_CASE("AdapterIpcSession evicts the oldest pending cancellation once "
          "more than the bound have been received") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);
  fixture.dispatcher.SetResult(7, {std::byte{1}});
  fixture.dispatcher.SetResult(8, {std::byte{2}});

  for (std::uint64_t correlationId = 1;
       correlationId <= kMaxPendingIpcCancellations + 1; ++correlationId) {
    fixture.session.HandleMessage(
        IpcMessage{IpcCancelMessage{.correlationId = correlationId}});
  }

  //  Correlation id 1 was evicted to admit the
  //  (kMaxPendingIpcCancellations + 1)th cancellation, so a request reusing
  //  it now dispatches normally.
  fixture.session.HandleMessage(
      IpcMessage{IpcListenEventMessage{.correlationId = 1, .eventKey = 7}});
  //  Correlation id (kMaxPendingIpcCancellations + 1) is still recorded, so
  //  the matching request is cancelled.
  fixture.session.HandleMessage(IpcMessage{IpcListenEventMessage{
      .correlationId = kMaxPendingIpcCancellations + 1, .eventKey = 8}});

  fixture.marshaller.RunAllPending();

  CHECK(fixture.dispatcher.DispatchedKeys() == std::vector<std::uint32_t>{7});
  REQUIRE(fixture.captureQueue.Enqueued().size() == 1);
  CHECK(fixture.captureQueue.Enqueued().front().intentKey == 7);
}

TEST_CASE("AdapterIpcSession cancels a resynchronization request received "
          "before its marshaled task runs, sending no result") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);

  fixture.session.HandleMessage(
      IpcMessage{IpcResynchronizeRequestMessage{.correlationId = 42}});
  REQUIRE(fixture.marshaller.PendingCount() == 1);

  fixture.session.HandleMessage(
      IpcMessage{IpcCancelMessage{.correlationId = 42}});
  fixture.marshaller.RunAllPending();

  CHECK(connection.Sent().empty());
}

TEST_CASE("AdapterIpcSession cancels a read-sample dispatch received before "
          "its marshaled task runs") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);
  fixture.dispatcher.SetResult(8, {std::byte{2}});

  fixture.session.HandleMessage(
      IpcMessage{IpcReadSampleMessage{.correlationId = 21, .sampleToken = 8}});
  REQUIRE(fixture.marshaller.PendingCount() == 1);

  fixture.session.HandleMessage(
      IpcMessage{IpcCancelMessage{.correlationId = 21}});
  fixture.marshaller.RunAllPending();

  CHECK(fixture.dispatcher.DispatchedKeys().empty());
  CHECK(fixture.captureQueue.Enqueued().empty());
}

TEST_CASE("AdapterIpcSession handles a pairing-display request by "
          "presenting it through the sink and acknowledging it, for every "
          "display mode") {
  for (PairingDisplayMode mode :
       {PairingDisplayMode::kInitial, PairingDisplayMode::kManualRedisplay,
        PairingDisplayMode::kWrongCodeRedisplay}) {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);

    CHECK(fixture.session.HandleMessage(IpcMessage{IpcPairingDisplayMessage{
              .correlationId = 5, .code = "123456", .mode = mode}}) ==
          AdapterIpcMessageDisposition::kContinue);
    REQUIRE(fixture.pairingNotificationSink.Displayed().empty());
    fixture.marshaller.RunAllPending();

    REQUIRE(fixture.pairingNotificationSink.Displayed().size() == 1);
    CHECK(fixture.pairingNotificationSink.Displayed().front().first ==
          "123456");
    CHECK(fixture.pairingNotificationSink.Displayed().front().second == mode);
    REQUIRE(connection.Sent().size() == 1);
    auto *ack =
        std::get_if<IpcPairingDisplayAckMessage>(&connection.Sent().front());
    REQUIRE(ack != nullptr);
    CHECK(ack->correlationId == 5);
    CHECK(ack->accepted);
  }
}

TEST_CASE("AdapterIpcSession's pairing-display acknowledgement reflects a "
          "declined sink result") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);
  fixture.pairingNotificationSink.SetDisplayResult(false);

  fixture.session.HandleMessage(IpcMessage{
      IpcPairingDisplayMessage{.correlationId = 5,
                               .code = "123456",
                               .mode = PairingDisplayMode::kInitial}});
  fixture.marshaller.RunAllPending();

  REQUIRE(connection.Sent().size() == 1);
  auto *ack =
      std::get_if<IpcPairingDisplayAckMessage>(&connection.Sent().front());
  REQUIRE(ack != nullptr);
  CHECK_FALSE(ack->accepted);
}

TEST_CASE("AdapterIpcSession handles an attempts-exhausted notification by "
          "presenting it through the sink and sending no reply") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);

  CHECK(fixture.session.HandleMessage(IpcMessage{
            IpcPairingAttemptsExhaustedMessage{.correlationId = 0}}) ==
        AdapterIpcMessageDisposition::kContinue);
  REQUIRE(fixture.pairingNotificationSink.AttemptsExhaustedCalls() == 0);
  fixture.marshaller.RunAllPending();

  CHECK(fixture.pairingNotificationSink.AttemptsExhaustedCalls() == 1);
  CHECK(connection.Sent().empty());
}

TEST_CASE("AdapterIpcSession never dispatches a pairing-display or "
          "attempts-exhausted notification received before any accepted, "
          "matching-proof HelloAck") {
  SessionFixture fixture;

  fixture.session.HandleMessage(IpcMessage{
      IpcPairingDisplayMessage{.correlationId = 1,
                               .code = "123456",
                               .mode = PairingDisplayMode::kInitial}});
  fixture.session.HandleMessage(
      IpcMessage{IpcPairingAttemptsExhaustedMessage{.correlationId = 0}});

  CHECK(fixture.marshaller.PendingCount() == 0);
  CHECK(fixture.pairingNotificationSink.Displayed().empty());
  CHECK(fixture.pairingNotificationSink.AttemptsExhaustedCalls() == 0);
}

TEST_CASE("AdapterIpcSession cancels a pairing-display dispatch received "
          "before its marshaled task runs, sending no acknowledgement") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);

  CHECK(fixture.session.HandleMessage(IpcMessage{
            IpcPairingDisplayMessage{.correlationId = 11,
                                     .code = "123456",
                                     .mode = PairingDisplayMode::kInitial}}) ==
        AdapterIpcMessageDisposition::kContinue);
  REQUIRE(fixture.marshaller.PendingCount() == 1);

  CHECK(fixture.session.HandleMessage(IpcMessage{IpcCancelMessage{
            .correlationId = 11}}) == AdapterIpcMessageDisposition::kContinue);

  fixture.marshaller.RunAllPending();

  CHECK(fixture.pairingNotificationSink.Displayed().empty());
  CHECK(connection.Sent().empty());
}

namespace {

///  A sink whose every method throws, so a test can prove
///  `AdapterIpcSession`'s marshaled pairing-display and attempts-exhausted
///  tasks contain a sink failure the same way they already contain a
///  dispatcher failure.
class ThrowingPairingNotificationSink final
    : public IAdapterPairingNotificationSink {
public:
  bool Display(const std::string &, PairingDisplayMode) override {
    throw std::runtime_error("Display failed");
  }
  void NotifyAttemptsExhausted() override {
    throw std::runtime_error("NotifyAttemptsExhausted failed");
  }
};

} //  namespace

TEST_CASE("AdapterIpcSession contains an exception thrown by the pairing "
          "notification sink inside a marshaled pairing-display task") {
  FixedAdapterIpcPeerProofProvider peerProofProvider{
      {std::byte{9}, std::byte{8}, std::byte{7}}};
  AdapterIpcTarget target{
      .port = 58231,
      .proofToken = peerProofProvider.Token(),
      .hostProofKey = {std::byte{1}, std::byte{1}, std::byte{1}},
      .targetGeneration = 1,
  };
  ThrowingPairingNotificationSink throwingSink;
  FakeAdapterTaskMarshaller marshaller;
  FakeAdapterNativeDispatcher dispatcher;
  FakeAdapterCaptureHandoffQueue captureQueue;
  FakeAdapterIpcConnection connection;
  AdapterIpcSession session{SampleInstanceId(), SampleOwnerLifetimeId(),
                            marshaller,         dispatcher,
                            captureQueue,       throwingSink};
  session.AttachConnection(connection);
  Authenticate(session, connection, target);

  session.HandleMessage(IpcMessage{
      IpcPairingDisplayMessage{.correlationId = 1,
                               .code = "123456",
                               .mode = PairingDisplayMode::kInitial}});
  session.HandleMessage(
      IpcMessage{IpcPairingAttemptsExhaustedMessage{.correlationId = 0}});

  //  If either exception escaped, it would propagate out of RunAllPending()
  //  and fail this test.
  REQUIRE_NOTHROW(marshaller.RunAllPending());
  CHECK(connection.Sent().empty());
}

TEST_CASE("AdapterIpcSession drops a pending pairing-display request after "
          "disconnect") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);

  fixture.session.HandleMessage(IpcMessage{
      IpcPairingDisplayMessage{.correlationId = 1,
                               .code = "123456",
                               .mode = PairingDisplayMode::kInitial}});
  fixture.session.HandleDisconnected();
  fixture.marshaller.RunAllPending();

  CHECK(fixture.pairingNotificationSink.Displayed().empty());
  CHECK(connection.Sent().empty());
}

TEST_CASE("AdapterIpcSession drops a pending pairing-display request after "
          "logical closing, even before the physical disconnect notifies "
          "the session") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);

  fixture.session.HandleMessage(IpcMessage{
      IpcPairingDisplayMessage{.correlationId = 1,
                               .code = "123456",
                               .mode = PairingDisplayMode::kInitial}});
  fixture.session.HandleClosing();
  fixture.marshaller.RunAllPending();

  CHECK(fixture.pairingNotificationSink.Displayed().empty());
  CHECK(connection.Sent().empty());
}

TEST_CASE("AdapterIpcSession drops a pending pairing-display request from an "
          "older connection generation after reconnect") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);

  fixture.session.HandleMessage(IpcMessage{
      IpcPairingDisplayMessage{.correlationId = 1,
                               .code = "123456",
                               .mode = PairingDisplayMode::kInitial}});
  REQUIRE(fixture.marshaller.PendingCount() == 1);

  //  A full second handshake is not needed to prove the old generation's
  //  pending work is dropped: HandleConnected alone unconditionally bumps
  //  connectionGeneration_, matching "drops pending intent requests from an
  //  older generation after reconnect"'s identical shape for listen-event
  //  and read-sample.
  fixture.session.HandleDisconnected();
  fixture.session.HandleConnected(fixture.target);
  fixture.marshaller.RunAllPending();

  CHECK(fixture.pairingNotificationSink.Displayed().empty());
}

//  ---- SendTrustAdminRequest ----

namespace {

///  Builds an `onResult` callback that resolves the returned future with
///  whatever `SendTrustAdminRequest` eventually delivers, so a test can
///  observe when (and whether) it is invoked without its own thread ever
///  blocking on it directly.
std::pair<std::function<void(std::optional<std::string>)>,
          std::future<std::optional<std::string>>>
CaptureTrustAdminResult() {
  auto promise = std::make_shared<std::promise<std::optional<std::string>>>();
  std::future<std::optional<std::string>> future = promise->get_future();
  return {[promise](std::optional<std::string> result) {
            promise->set_value(std::move(result));
          },
          std::move(future)};
}

} //  namespace

TEST_CASE("AdapterIpcSession::SendTrustAdminRequest invokes its callback "
          "synchronously with nullopt, without sending, when no "
          "authenticated connection is available") {
  SessionFixture fixture;

  auto [onResult, resultFuture] = CaptureTrustAdminResult();
  fixture.session.SendTrustAdminRequest(TrustAdminOperation::kHelp,
                                        std::nullopt, std::nullopt,
                                        std::nullopt, onResult);
  REQUIRE(resultFuture.wait_for(std::chrono::seconds(0)) ==
          std::future_status::ready);
  CHECK_FALSE(resultFuture.get().has_value());

  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  //  Attached but not yet authenticated.
  auto [onResultUnauthenticated, resultFutureUnauthenticated] =
      CaptureTrustAdminResult();
  fixture.session.SendTrustAdminRequest(TrustAdminOperation::kHelp,
                                        std::nullopt, std::nullopt,
                                        std::nullopt, onResultUnauthenticated);
  REQUIRE(resultFutureUnauthenticated.wait_for(std::chrono::seconds(0)) ==
          std::future_status::ready);
  CHECK_FALSE(resultFutureUnauthenticated.get().has_value());
  CHECK(connection.Sent().empty());
}

TEST_CASE("AdapterIpcSession::SendTrustAdminRequest sends exactly the "
          "operation and argument it was given") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);

  auto [onResult, resultFuture] = CaptureTrustAdminResult();
  fixture.session.SendTrustAdminRequest(
      TrustAdminOperation::kRevoke, std::nullopt,
      std::optional<std::string>("12345"), std::nullopt, onResult);

  REQUIRE(connection.Sent().size() == 1);
  auto *sentRequest =
      std::get_if<IpcTrustAdminRequestMessage>(&connection.Sent().front());
  REQUIRE(sentRequest != nullptr);
  CHECK(sentRequest->correlationId != 0);
  CHECK(sentRequest->operation == TrustAdminOperation::kRevoke);
  CHECK(sentRequest->shortId == "12345");
  CHECK_FALSE(sentRequest->listScope.has_value());
  CHECK_FALSE(sentRequest->confirmationCode.has_value());

  //  Resolve it deterministically through HandleMessage rather than leaving
  //  fixture.session's own default (several-second) timeout worker to do so
  //  during test teardown.
  fixture.session.HandleMessage(IpcMessage{IpcTrustAdminResultMessage{
      .correlationId = sentRequest->correlationId, .resultText = "ok"}});
  REQUIRE(resultFuture.wait_for(std::chrono::seconds(1)) ==
          std::future_status::ready);
}

TEST_CASE("AdapterIpcSession::SendTrustAdminRequest returns nullopt when "
          "the connection rejects the send") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);
  connection.RejectNextSend();

  auto [onResult, resultFuture] = CaptureTrustAdminResult();
  fixture.session.SendTrustAdminRequest(TrustAdminOperation::kHelp,
                                        std::nullopt, std::nullopt,
                                        std::nullopt, onResult);

  REQUIRE(resultFuture.wait_for(std::chrono::seconds(0)) ==
          std::future_status::ready);
  CHECK_FALSE(resultFuture.get().has_value());
  CHECK(connection.Sent().empty());
}

TEST_CASE("AdapterIpcSession::SendTrustAdminRequest returns immediately and "
          "its callback fires with nullopt only once the bound elapses, "
          "never blocking the calling thread") {
  //  A short injected timeout keeps this test fast: nothing ever resolves
  //  this request, so its callback only fires once the bound elapses.
  FixedAdapterIpcPeerProofProvider peerProofProvider{
      {std::byte{9}, std::byte{8}, std::byte{7}}};
  AdapterIpcTarget target{
      .port = 58231,
      .proofToken = peerProofProvider.Token(),
      .hostProofKey = {std::byte{1}, std::byte{1}, std::byte{1}},
      .targetGeneration = 1,
  };
  FakeAdapterTaskMarshaller marshaller;
  FakeAdapterNativeDispatcher dispatcher;
  FakeAdapterCaptureHandoffQueue captureQueue;
  FakeAdapterPairingNotificationSink pairingNotificationSink;
  FakeAdapterIpcConnection connection;
  AdapterIpcSession session{SampleInstanceId(),
                            SampleOwnerLifetimeId(),
                            marshaller,
                            dispatcher,
                            captureQueue,
                            pairingNotificationSink,
                            [] {},
                            std::chrono::milliseconds(200)};
  session.AttachConnection(connection);
  Authenticate(session, connection, target);

  auto [onResult, resultFuture] = CaptureTrustAdminResult();
  auto before = std::chrono::steady_clock::now();
  session.SendTrustAdminRequest(TrustAdminOperation::kHelp, std::nullopt,
                                std::nullopt, std::nullopt, onResult);
  auto elapsed = std::chrono::steady_clock::now() - before;

  //  Well under the 200ms bound: the call itself never waits for the
  //  timeout, only enqueues work that resolves later.
  CHECK(elapsed < std::chrono::milliseconds(50));
  CHECK(resultFuture.wait_for(std::chrono::seconds(0)) ==
        std::future_status::timeout);

  REQUIRE(resultFuture.wait_for(std::chrono::seconds(1)) ==
          std::future_status::ready);
  CHECK_FALSE(resultFuture.get().has_value());
}

TEST_CASE("AdapterIpcSession::SendTrustAdminRequest resolves with the "
          "host's correlated result, synchronously, once HandleMessage "
          "delivers it") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);

  auto [onResult, resultFuture] = CaptureTrustAdminResult();
  fixture.session.SendTrustAdminRequest(TrustAdminOperation::kResetTrust,
                                        std::nullopt, std::nullopt,
                                        std::nullopt, onResult);
  REQUIRE(connection.Sent().size() == 1);
  std::uint64_t correlationId =
      std::get<IpcTrustAdminRequestMessage>(connection.Sent().front())
          .correlationId;

  fixture.session.HandleMessage(IpcMessage{IpcTrustAdminResultMessage{
      .correlationId = correlationId,
      .resultText = "Reset Trust complete (0 devices revoked)."}});

  REQUIRE(resultFuture.wait_for(std::chrono::seconds(0)) ==
          std::future_status::ready);
  std::optional<std::string> result = resultFuture.get();
  REQUIRE(result.has_value());
  CHECK(*result == "Reset Trust complete (0 devices revoked).");
}

TEST_CASE("AdapterIpcSession::SendTrustAdminRequest resolves concurrent "
          "requests independently by correlation id") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);

  auto [firstOnResult, firstResultFuture] = CaptureTrustAdminResult();
  fixture.session.SendTrustAdminRequest(TrustAdminOperation::kHelp,
                                        std::nullopt, std::nullopt,
                                        std::nullopt, firstOnResult);
  std::uint64_t firstCorrelationId =
      std::get<IpcTrustAdminRequestMessage>(connection.Sent().back())
          .correlationId;

  auto [secondOnResult, secondResultFuture] = CaptureTrustAdminResult();
  fixture.session.SendTrustAdminRequest(TrustAdminOperation::kResetTrust,
                                        std::nullopt, std::nullopt,
                                        std::nullopt, secondOnResult);
  std::uint64_t secondCorrelationId =
      std::get<IpcTrustAdminRequestMessage>(connection.Sent().back())
          .correlationId;

  REQUIRE(firstCorrelationId != secondCorrelationId);

  //  Resolved in reverse order, proving neither result is misdelivered to the
  //  other request.
  fixture.session.HandleMessage(IpcMessage{IpcTrustAdminResultMessage{
      .correlationId = secondCorrelationId, .resultText = "second"}});
  fixture.session.HandleMessage(IpcMessage{IpcTrustAdminResultMessage{
      .correlationId = firstCorrelationId, .resultText = "first"}});

  REQUIRE(firstResultFuture.wait_for(std::chrono::seconds(0)) ==
          std::future_status::ready);
  REQUIRE(secondResultFuture.wait_for(std::chrono::seconds(0)) ==
          std::future_status::ready);
  std::optional<std::string> first = firstResultFuture.get();
  std::optional<std::string> second = secondResultFuture.get();
  REQUIRE(first.has_value());
  REQUIRE(second.has_value());
  CHECK(*first == "first");
  CHECK(*second == "second");
}

TEST_CASE("AdapterIpcSession::SendTrustAdminRequest's onResult callback is "
          "invoked exactly once even when HandleMessage resolves the "
          "request before its timeout worker wakes") {
  //  A short injected timeout keeps this test fast. Rather than sleeping
  //  past it, the destructor's own wait for every timeout worker to finish
  //  (proven separately) is reused here as the deterministic signal that
  //  this request's worker has actually woken, found the entry already
  //  resolved by HandleMessage below, and observed it as a no-op --
  //  ai/context/skse/testing.md's "do not rely on timing sleeps to prove
  //  concurrency."
  FixedAdapterIpcPeerProofProvider peerProofProvider{
      {std::byte{9}, std::byte{8}, std::byte{7}}};
  AdapterIpcTarget target{
      .port = 58231,
      .proofToken = peerProofProvider.Token(),
      .hostProofKey = {std::byte{1}, std::byte{1}, std::byte{1}},
      .targetGeneration = 1,
  };
  FakeAdapterTaskMarshaller marshaller;
  FakeAdapterNativeDispatcher dispatcher;
  FakeAdapterCaptureHandoffQueue captureQueue;
  FakeAdapterPairingNotificationSink pairingNotificationSink;
  FakeAdapterIpcConnection connection;
  auto invocationCount = std::make_shared<std::atomic<int>>(0);
  {
    auto session = std::make_unique<AdapterIpcSession>(
        SampleInstanceId(), SampleOwnerLifetimeId(), marshaller, dispatcher,
        captureQueue, pairingNotificationSink, [] {},
        std::chrono::milliseconds(30));
    session->AttachConnection(connection);
    Authenticate(*session, connection, target);

    session->SendTrustAdminRequest(
        TrustAdminOperation::kHelp, std::nullopt, std::nullopt, std::nullopt,
        [invocationCount](std::optional<std::string>) {
          invocationCount->fetch_add(1);
        });
    std::uint64_t correlationId =
        std::get<IpcTrustAdminRequestMessage>(connection.Sent().front())
            .correlationId;

    session->HandleMessage(IpcMessage{IpcTrustAdminResultMessage{
        .correlationId = correlationId, .resultText = "ok"}});
    CHECK(invocationCount->load() == 1);

    //  session's destructor blocks until this request's timeout worker has
    //  actually finished (see the destructor tests above), so by the time
    //  this scope exits, the worker has already woken, found the entry
    //  gone, and confirmed its own no-op -- deterministically, not by
    //  outrunning a clock.
  }

  CHECK(invocationCount->load() == 1);
}

TEST_CASE("AdapterIpcSession contains an exception a trust-admin onResult "
          "callback throws when HandleMessage delivers its result") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);

  fixture.session.SendTrustAdminRequest(
      TrustAdminOperation::kHelp, std::nullopt, std::nullopt, std::nullopt,
      [](std::optional<std::string>) {
        throw std::runtime_error("onResult failed");
      });
  std::uint64_t correlationId =
      std::get<IpcTrustAdminRequestMessage>(connection.Sent().front())
          .correlationId;

  CHECK(fixture.session.HandleMessage(IpcMessage{IpcTrustAdminResultMessage{
            .correlationId = correlationId, .resultText = "ok"}}) ==
        AdapterIpcMessageDisposition::kContinue);
}

TEST_CASE("AdapterIpcSession contains an exception a trust-admin onResult "
          "callback throws when invoked immediately for an unauthenticated "
          "session") {
  SessionFixture fixture;

  //  A throw here must not propagate into this call, since it may run
  //  directly on a Papyrus-invoking thread.
  fixture.session.SendTrustAdminRequest(
      TrustAdminOperation::kHelp, std::nullopt, std::nullopt, std::nullopt,
      [](std::optional<std::string>) {
        throw std::runtime_error("onResult failed");
      });
}

TEST_CASE("AdapterIpcSession ignores a trust-admin result with no matching "
          "pending request") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);

  CHECK(fixture.session.HandleMessage(IpcMessage{IpcTrustAdminResultMessage{
            .correlationId = 999, .resultText = "unexpected"}}) ==
        AdapterIpcMessageDisposition::kContinue);
}

TEST_CASE("AdapterIpcSession closes the connection on a trust-admin result "
          "received before any accepted, matching-proof HelloAck") {
  SessionFixture fixture;

  CHECK(fixture.session.HandleMessage(IpcMessage{IpcTrustAdminResultMessage{
            .correlationId = 1, .resultText = "unexpected"}}) ==
        AdapterIpcMessageDisposition::kClose);
}

TEST_CASE("AdapterIpcSession::SendTrustAdminRequest's callback fires "
          "synchronously with nullopt when disconnect ends the connection "
          "while the request is outstanding") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);

  auto [onResult, resultFuture] = CaptureTrustAdminResult();
  fixture.session.SendTrustAdminRequest(TrustAdminOperation::kHelp,
                                        std::nullopt, std::nullopt,
                                        std::nullopt, onResult);
  REQUIRE(connection.Sent().size() == 1);

  fixture.session.HandleDisconnected();

  REQUIRE(resultFuture.wait_for(std::chrono::seconds(0)) ==
          std::future_status::ready);
  CHECK_FALSE(resultFuture.get().has_value());
}

TEST_CASE("AdapterIpcSession::SendTrustAdminRequest's callback fires "
          "synchronously with nullopt when logical closing ends the "
          "connection while the request is outstanding") {
  SessionFixture fixture;
  FakeAdapterIpcConnection connection;
  fixture.session.AttachConnection(connection);
  Authenticate(fixture.session, connection, fixture.target);

  auto [onResult, resultFuture] = CaptureTrustAdminResult();
  fixture.session.SendTrustAdminRequest(TrustAdminOperation::kHelp,
                                        std::nullopt, std::nullopt,
                                        std::nullopt, onResult);
  REQUIRE(connection.Sent().size() == 1);

  fixture.session.HandleClosing();

  REQUIRE(resultFuture.wait_for(std::chrono::seconds(0)) ==
          std::future_status::ready);
  CHECK_FALSE(resultFuture.get().has_value());
}

TEST_CASE("AdapterIpcSession's destructor waits for an outstanding "
          "trust-admin request's timeout worker to finish, resolving it "
          "with nullopt") {
  FixedAdapterIpcPeerProofProvider peerProofProvider{
      {std::byte{9}, std::byte{8}, std::byte{7}}};
  AdapterIpcTarget target{
      .port = 58231,
      .proofToken = peerProofProvider.Token(),
      .hostProofKey = {std::byte{1}, std::byte{1}, std::byte{1}},
      .targetGeneration = 1,
  };
  FakeAdapterTaskMarshaller marshaller;
  FakeAdapterNativeDispatcher dispatcher;
  FakeAdapterCaptureHandoffQueue captureQueue;
  FakeAdapterPairingNotificationSink pairingNotificationSink;
  FakeAdapterIpcConnection connection;
  std::future<std::optional<std::string>> resultFuture;
  {
    auto session = std::make_unique<AdapterIpcSession>(
        SampleInstanceId(), SampleOwnerLifetimeId(), marshaller, dispatcher,
        captureQueue, pairingNotificationSink, [] {},
        std::chrono::milliseconds(50));
    session->AttachConnection(connection);
    Authenticate(*session, connection, target);

    auto [onResult, future] = CaptureTrustAdminResult();
    resultFuture = std::move(future);
    session->SendTrustAdminRequest(TrustAdminOperation::kHelp, std::nullopt,
                                   std::nullopt, std::nullopt, onResult);
    REQUIRE(connection.Sent().size() == 1);

    //  Destroying the session here, with the request's timeout worker still
    //  sleeping out its bound, is exactly the scenario the destructor must
    //  make safe: it must wait for that worker to finish before returning,
    //  not merely notify it.
  }

  //  A defined nullopt (from the destructor's own force-abandonment) rather
  //  than a crash or hang proves the destructor actually waited.
  REQUIRE(resultFuture.wait_for(std::chrono::seconds(0)) ==
          std::future_status::ready);
  CHECK_FALSE(resultFuture.get().has_value());
}

TEST_CASE("AdapterIpcSession's destructor safely waits for a "
          "SendTrustAdminRequest call still inside TrySend on another "
          "thread, proving activeTrustAdminWaiters_ is counted before "
          "TrySend rather than after it") {
  FixedAdapterIpcPeerProofProvider peerProofProvider{
      {std::byte{9}, std::byte{8}, std::byte{7}}};
  AdapterIpcTarget target{
      .port = 58231,
      .proofToken = peerProofProvider.Token(),
      .hostProofKey = {std::byte{1}, std::byte{1}, std::byte{1}},
      .targetGeneration = 1,
  };
  FakeAdapterTaskMarshaller marshaller;
  FakeAdapterNativeDispatcher dispatcher;
  FakeAdapterCaptureHandoffQueue captureQueue;
  FakeAdapterPairingNotificationSink pairingNotificationSink;
  FakeAdapterIpcConnection connection;
  auto session = std::make_unique<AdapterIpcSession>(
      SampleInstanceId(), SampleOwnerLifetimeId(), marshaller, dispatcher,
      captureQueue, pairingNotificationSink, [] {},
      std::chrono::milliseconds(50));
  session->AttachConnection(connection);
  Authenticate(*session, connection, target);

  std::future<void> sendEntered = connection.BlockNextSend();
  auto [onResult, resultFuture] = CaptureTrustAdminResult();
  AdapterIpcSession *sessionPtr = session.get();
  std::future<void> sendCall = std::async(std::launch::async, [sessionPtr,
                                                               onResult] {
    sessionPtr->SendTrustAdminRequest(TrustAdminOperation::kHelp, std::nullopt,
                                      std::nullopt, std::nullopt, onResult);
  });
  REQUIRE(sendEntered.wait_for(std::chrono::seconds(1)) ==
          std::future_status::ready);

  //  SendTrustAdminRequest is now blocked inside TrySend, on another
  //  thread, strictly after registering its pending entry and incrementing
  //  activeTrustAdminWaiters_ (both happen, under the same lock, before
  //  TrySend is ever called). Destroying the session concurrently here must
  //  wait for this in-flight call to finish touching
  //  trustAdminMutex_-guarded state before tearing it down -- exactly the
  //  interleaving the prior increment-after-TrySend ordering could not
  //  survive: this session's destructor would previously have been free to
  //  observe zero in-flight requests and destroy trustAdminMutex_ /
  //  trustAdminCondition_ while this blocked call was still about to lock
  //  them.
  std::thread destroyer([&session] { session.reset(); });
  connection.ReleaseBlockedSend();

  sendCall.get();
  destroyer.join();

  REQUIRE(resultFuture.wait_for(std::chrono::seconds(1)) ==
          std::future_status::ready);
  CHECK_FALSE(resultFuture.get().has_value());
}
