#include "ipc/adapter_ipc_session.hpp"

#include "ipc/adapter_ipc_connection.hpp"
#include "ipc/adapter_ipc_hmac.hpp"
#include "ipc/adapter_ipc_peer_proof_provider.hpp"
#include "ipc/adapter_task_marshaller_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <array>
#include <atomic>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <future>
#include <latch>
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
using dovahlink::adapter::capture::CaptureAvailability;
using dovahlink::adapter::capture::CaptureSourceKind;
using dovahlink::adapter::capture::CharacterEventKey;
using dovahlink::adapter::capture::CharacterSampleToken;
using dovahlink::adapter::capture::IAdapterCaptureHandoffQueue;
using dovahlink::adapter::dispatch::IAdapterNativeCaptureRouter;
using dovahlink::adapter::dispatch::SampleCaptureResult;
using dovahlink::adapter::dispatch::SampleCaptureStatus;
using dovahlink::adapter::identity::AdapterInstanceId;
using dovahlink::adapter::identity::AdapterPlayContextState;
using dovahlink::adapter::ipc::AdapterIpcMessageDisposition;
using dovahlink::adapter::ipc::AdapterIpcSession;
using dovahlink::adapter::ipc::AdapterIpcTarget;
using dovahlink::adapter::ipc::BuildHostProofMessage;
using dovahlink::adapter::ipc::ComputeIpcHmacSha256;
using dovahlink::adapter::ipc::FixedAdapterIpcPeerProofProvider;
using dovahlink::adapter::ipc::IAdapterIpcConnection;
using dovahlink::adapter::ipc::IAdapterPairingNotificationSink;
using dovahlink::adapter::ipc::IpcCancelMessage;
using dovahlink::adapter::ipc::IpcCaptureResultMessage;
using dovahlink::adapter::ipc::IpcCloseMessage;
using dovahlink::adapter::ipc::IpcCloseReason;
using dovahlink::adapter::ipc::IpcHelloAckMessage;
using dovahlink::adapter::ipc::IpcHelloMessage;
using dovahlink::adapter::ipc::IpcHelloRejectReason;
using dovahlink::adapter::ipc::IpcListenEventMessage;
using dovahlink::adapter::ipc::IpcListenEventResultMessage;
using dovahlink::adapter::ipc::IpcMessage;
using dovahlink::adapter::ipc::IpcPairingAttemptsExhaustedMessage;
using dovahlink::adapter::ipc::IpcPairingDisplayAckMessage;
using dovahlink::adapter::ipc::IpcPairingDisplayMessage;
using dovahlink::adapter::ipc::IpcPlayContextChangedMessage;
using dovahlink::adapter::ipc::IpcPlayContextEndedMessage;
using dovahlink::adapter::ipc::IpcReadSampleMessage;
using dovahlink::adapter::ipc::IpcRejectMessage;
using dovahlink::adapter::ipc::IpcRejectReason;
using dovahlink::adapter::ipc::IpcResynchronizeRequestMessage;
using dovahlink::adapter::ipc::IpcResynchronizeResultMessage;
using dovahlink::adapter::ipc::IpcTrustAdminRequestMessage;
using dovahlink::adapter::ipc::IpcTrustAdminResultMessage;
using dovahlink::adapter::ipc::kIpcOwnerLifetimeIdBytes;
using dovahlink::adapter::ipc::kMaxPendingGameThreadDispatches;
using dovahlink::adapter::ipc::kMaxPendingTrustAdminRequests;
using dovahlink::adapter::ipc::PairingDisplayMode;
using dovahlink::adapter::ipc::TrustAdminListScope;
using dovahlink::adapter::ipc::TrustAdminOperation;
using dovahlink::adapter::ipc::TrustAdminRequestOutcome;
using dovahlink::adapter::ipc::TrustAdminRequestResult;
using dovahlink::adapter::ipc::test_support::FakeAdapterTaskMarshaller;
using dovahlink::adapter::runtime::IAdapterTaskMarshaller;

namespace {

///  A fake `IAdapterNativeCaptureRouter` with configurable per-key sample
///  results and event registration outcomes. `DispatchedKeys()` logs both
///  `CaptureSample` and `RegisterEvent` calls, in call order, since most
///  tests only care whether -- and in what order -- a key reached the
///  router at all, not which of the two operations carried it. A sample
///  token with no configured result reports `kUnavailable` -- a known,
///  approved token whose underlying value just is not ready -- matching this
///  fake's role as a stand-in for a real router whose supported tokens all
///  fail closed rather than as a stand-in for a version-mismatched one; use
///  `SetSampleUnsupported` for a test that specifically needs that case.
class FakeAdapterNativeCaptureRouter final : public IAdapterNativeCaptureRouter {
  public:
    ///  Configures `CaptureSample(sampleToken)` to return `value` as available.
    void SetSampleResult(std::uint32_t sampleToken, std::vector<std::byte> value) {
        sampleResults_[sampleToken] = std::move(value);
    }

    ///  Configures `CaptureSample(sampleToken)` to report `kUnsupported`.
    void SetSampleUnsupported(std::uint32_t sampleToken) {
        unsupportedSampleTokens_.insert(sampleToken);
    }

    ///  Makes `CaptureSample(sampleToken)` throw instead of returning.
    void SetSampleThrows(std::uint32_t sampleToken) {
        throwingSampleTokens_.insert(sampleToken);
    }

    ///  Configures `RegisterEvent(eventKey)` to return `registered`.
    void SetEventRegistered(std::uint32_t eventKey, bool registered) {
        eventResults_[eventKey] = registered;
    }

    ///  Makes `RegisterEvent(eventKey)` throw instead of returning.
    void SetEventThrows(std::uint32_t eventKey) {
        throwingEventKeys_.insert(eventKey);
    }

    SampleCaptureResult CaptureSample(std::uint32_t sampleToken) override {
        dispatchedKeys_.push_back(sampleToken);
        if (throwingSampleTokens_.contains(sampleToken)) {
            throw std::runtime_error("CaptureSample failed");
        }
        if (unsupportedSampleTokens_.contains(sampleToken)) {
            return SampleCaptureResult{
                .status = SampleCaptureStatus::kUnsupported};
        }
        auto it = sampleResults_.find(sampleToken);
        if (it == sampleResults_.end()) {
            return SampleCaptureResult{
                .status = SampleCaptureStatus::kUnavailable};
        }
        return SampleCaptureResult{
            .status = SampleCaptureStatus::kAvailable,
            .payload = dovahlink::adapter::capture::MakeCapturedPayload(it->second)};
    }

    bool RegisterEvent(std::uint32_t eventKey) override {
        dispatchedKeys_.push_back(eventKey);
        if (throwingEventKeys_.contains(eventKey)) {
            throw std::runtime_error("RegisterEvent failed");
        }
        auto it = eventResults_.find(eventKey);
        return it != eventResults_.end() && it->second;
    }

    const std::vector<std::uint32_t>& DispatchedKeys() const {
        return dispatchedKeys_;
    }

  private:
    std::unordered_map<std::uint32_t, std::vector<std::byte>> sampleResults_;
    std::unordered_set<std::uint32_t> unsupportedSampleTokens_;
    std::unordered_set<std::uint32_t> throwingSampleTokens_;
    std::unordered_map<std::uint32_t, bool> eventResults_;
    std::unordered_set<std::uint32_t> throwingEventKeys_;
    std::vector<std::uint32_t> dispatchedKeys_;
};

///  A capture router that holds a game-thread callback until the test
///  releases it, for either operation.
class BlockingAdapterNativeCaptureRouter final : public IAdapterNativeCaptureRouter {
  public:
    ///  Creates a router synchronized by the supplied entry and release
    ///  signals.
    BlockingAdapterNativeCaptureRouter(std::promise<void>& entered,
                                       std::shared_future<void> release)
        : entered_(entered), release_(std::move(release)) {}

    ///  Signals that the callback entered, then waits for the test to release
    ///  it before reporting that no translation exists.
    SampleCaptureResult CaptureSample(std::uint32_t /*sampleToken*/) override {
        Block();
        return SampleCaptureResult{
            .status = SampleCaptureStatus::kUnsupported};
    }

    ///  Signals that the callback entered, then waits for the test to release
    ///  it before reporting that no translation exists.
    bool RegisterEvent(std::uint32_t /*eventKey*/) override {
        Block();
        return false;
    }

  private:
    ///  Signals entry and waits for release; shared by both operations.
    void Block() {
        entered_.set_value();
        release_.wait();
    }

    ///  Signals that the callback has entered the router.
    std::promise<void>& entered_;
    ///  Keeps the callback blocked until the test releases it.
    std::shared_future<void> release_;
};

///  A fake `IAdapterCaptureHandoffQueue` that records every enqueued item,
///  and can be configured to reject a specific intent key -- simulating the
///  real queue's own bounded, non-blocking rejection at capacity.
class FakeAdapterCaptureHandoffQueue final
    : public IAdapterCaptureHandoffQueue {
  public:
    bool TryEnqueue(AdapterCaptureWorkItem item) override {
        if (rejectIntentKey_.has_value() && item.intentKey == *rejectIntentKey_) {
            return false;
        }
        enqueued_.push_back(std::move(item));
        return true;
    }

    void Stop() override {}

    const std::vector<AdapterCaptureWorkItem>& Enqueued() const {
        return enqueued_;
    }

    ///  Makes every subsequent `TryEnqueue` for `intentKey` reject the item
    ///  instead of accepting it, the same as a real queue at capacity.
    void SetRejectIntentKey(std::uint32_t intentKey) {
        rejectIntentKey_ = intentKey;
    }

  private:
    std::vector<AdapterCaptureWorkItem> enqueued_;
    ///  The intent key `TryEnqueue` rejects, if configured.
    std::optional<std::uint32_t> rejectIntentKey_;
};

///  A fake `IAdapterPairingNotificationSink` that records every call and
///  returns a configurable accepted result.
class FakeAdapterPairingNotificationSink final
    : public IAdapterPairingNotificationSink {
  public:
    bool Display(const std::string& code, PairingDisplayMode mode) override {
        displayed_.emplace_back(code, mode);
        return displayResult_;
    }

    void NotifyAttemptsExhausted() override { ++attemptsExhaustedCalls_; }

    ///  The (code, mode) pairs passed to `Display`, in call order.
    const std::vector<std::pair<std::string, PairingDisplayMode>>&
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

    bool TrySend(const IpcMessage& message) override {
        if (blockNextSend_) {
            blockNextSend_ = false;
            blockedSendEntered_.set_value();
            blockedSendRelease_.get_future().wait();
        }
        //  Guards sent_ and the one-shot flags below: SendTrustAdminRequest's
        //  timeout worker can call TrySend (for its own best-effort cancellation)
        //  concurrently with other requests' own timeout workers, so this fake
        //  must tolerate genuinely concurrent callers, not just concurrent
        //  callers serialized by the session's own locking as every prior use of
        //  this fake was.
        std::lock_guard<std::mutex> lock(mutex_);
        if (throwOnNextSend_) {
            throwOnNextSend_ = false;
            throw std::runtime_error("TrySend failed");
        }
        if (throwNonStandardOnNextSend_) {
            throwNonStandardOnNextSend_ = false;
            throw 42;
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

    ///  Records the call; this fake never actually resets a transport.
    void RequestReconnect() override { ++reconnectRequests_; }

    void Stop() override {}

    ///  The number of times `RequestReconnect` was called.
    int ReconnectRequests() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return reconnectRequests_;
    }

    const std::vector<IpcMessage>& Sent() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return sent_;
    }

    ///  Discards every recorded message, so a test can assert on only what it
    ///  sends after this call (for example, after using `Authenticate` as
    ///  setup).
    void Clear() {
        std::lock_guard<std::mutex> lock(mutex_);
        sent_.clear();
    }

    ///  Makes the next `TrySend` call report rejection (as a full outbound
    ///  queue would) instead of recording and accepting the message. Consumed
    ///  by the call it affects; a later `TrySend` accepts normally again.
    void RejectNextSend() {
        std::lock_guard<std::mutex> lock(mutex_);
        rejectNextSend_ = true;
    }

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

    ///  Makes the next `TrySend` call throw `std::runtime_error` instead of
    ///  recording and accepting the message, as a real transport's
    ///  variable-sized ring-buffer write could on `std::bad_alloc`. Consumed by
    ///  the call it affects; a later `TrySend` accepts normally again.
    void ThrowOnNextSend() {
        std::lock_guard<std::mutex> lock(mutex_);
        throwOnNextSend_ = true;
    }

    ///  Makes the next `TrySend` call throw a non-`std::exception` value (a
    ///  plain `int`) instead of recording and accepting the message, proving a
    ///  caller that catches only `(...)` -- not `const std::exception&` --
    ///  still contains it. Consumed by the call it affects; a later `TrySend`
    ///  accepts normally again.
    void ThrowNonStandardOnNextSend() {
        std::lock_guard<std::mutex> lock(mutex_);
        throwNonStandardOnNextSend_ = true;
    }

  private:
    std::vector<IpcMessage> sent_;
    ///  Whether the next `TrySend` call should report rejection.
    bool rejectNextSend_ = false;
    ///  Whether the next `TrySend` call should block until released.
    bool blockNextSend_ = false;
    ///  Whether the next `TrySend` call should throw instead of sending.
    bool throwOnNextSend_ = false;
    ///  Whether the next `TrySend` call should throw a non-`std::exception`
    ///  value instead of sending.
    bool throwNonStandardOnNextSend_ = false;
    ///  The number of times `RequestReconnect` was called.
    int reconnectRequests_ = 0;
    ///  Resolved the instant a blocked `TrySend` call actually enters.
    std::promise<void> blockedSendEntered_;
    ///  Resolved by `ReleaseBlockedSend` to let a blocked `TrySend` call proceed.
    std::promise<void> blockedSendRelease_;
    ///  Guards sent_ and the one-shot flags above against concurrent `TrySend`
    ///  callers.
    mutable std::mutex mutex_;
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
    FakeAdapterNativeCaptureRouter dispatcher;
    FakeAdapterCaptureHandoffQueue captureQueue;
    FakeAdapterPairingNotificationSink pairingNotificationSink;
    AdapterPlayContextState playContextState;
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
                              playContextState,
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
void Authenticate(AdapterIpcSession& session,
                  FakeAdapterIpcConnection& connection,
                  const AdapterIpcTarget& target) {
    session.HandleConnected(target);
    REQUIRE(connection.Sent().size() == 1);
    auto* hello = std::get_if<IpcHelloMessage>(&connection.Sent().front());
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
    auto* hello = std::get_if<IpcHelloMessage>(&first);
    REQUIRE(hello != nullptr);
    CHECK(hello->correlationId == 1);
    CHECK(hello->adapterInstanceId == SampleInstanceId().value);
    CHECK(hello->peerProofToken ==
          std::vector<std::byte>{std::byte{9}, std::byte{8}, std::byte{7}});
    CHECK(hello->ownerLifetimeId == SampleOwnerLifetimeId());

    IpcMessage second = fixture.session.PrepareHello(fixture.target);
    auto* secondHello = std::get_if<IpcHelloMessage>(&second);
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
    auto* hello = std::get_if<IpcHelloMessage>(&connection.Sent().front());
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
    auto* hello = std::get_if<IpcHelloMessage>(&connection.Sent().front());
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

TEST_CASE("AdapterIpcSession handles a resynchronize request by registering "
          "the level-changed event, enqueueing level/vitals/XP baseline "
          "samples, and reporting the resync accepted") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    std::uint32_t levelChangedKey =
        static_cast<std::uint32_t>(CharacterEventKey::kCharacterLevelChanged);
    std::uint32_t levelBaselineToken =
        static_cast<std::uint32_t>(CharacterSampleToken::kCharacterLevelBaseline);
    std::uint32_t vitalsToken =
        static_cast<std::uint32_t>(CharacterSampleToken::kCharacterVitals);
    std::uint32_t xpToken = static_cast<std::uint32_t>(CharacterSampleToken::kCharacterXp);
    fixture.dispatcher.SetEventRegistered(levelChangedKey, true);
    fixture.dispatcher.SetSampleResult(levelBaselineToken, {std::byte{9}});
    fixture.dispatcher.SetSampleResult(
        vitalsToken, {std::byte{1}, std::byte{2}, std::byte{3}});
    fixture.dispatcher.SetSampleResult(xpToken, {std::byte{7}});

    CHECK(fixture.session.HandleMessage(IpcMessage{IpcResynchronizeRequestMessage{
              .correlationId = 42}}) == AdapterIpcMessageDisposition::kContinue);

    //  Not sent synchronously: it must go through the game-thread marshaller.
    CHECK(connection.Sent().empty());
    REQUIRE(fixture.marshaller.PendingCount() == 1);

    fixture.marshaller.RunAllPending();

    //  Registration reaches the router before any baseline sample -- in this
    //  same game-thread task -- so a level-up cannot land in the gap between
    //  them.
    REQUIRE(fixture.dispatcher.DispatchedKeys().size() == 4);
    CHECK(fixture.dispatcher.DispatchedKeys()[0] == levelChangedKey);
    CHECK(fixture.dispatcher.DispatchedKeys()[1] == levelBaselineToken);
    CHECK(fixture.dispatcher.DispatchedKeys()[2] == vitalsToken);
    CHECK(fixture.dispatcher.DispatchedKeys()[3] == xpToken);

    REQUIRE(fixture.captureQueue.Enqueued().size() == 3);
    CHECK(fixture.captureQueue.Enqueued()[0].intentKey == levelBaselineToken);
    CHECK(fixture.captureQueue.Enqueued()[0].capturedValue ==
          dovahlink::adapter::capture::MakeCapturedPayload(std::array{std::byte{9}}));
    CHECK(fixture.captureQueue.Enqueued()[0].availability ==
          CaptureAvailability::kAvailable);
    CHECK(fixture.captureQueue.Enqueued()[0].source == CaptureSourceKind::kSample);
    CHECK(fixture.captureQueue.Enqueued()[0].correlationId == 0);
    CHECK(fixture.captureQueue.Enqueued()[1].intentKey == vitalsToken);
    CHECK(fixture.captureQueue.Enqueued()[2].intentKey == xpToken);

    REQUIRE(connection.Sent().size() == 1);
    auto* result =
        std::get_if<IpcResynchronizeResultMessage>(&connection.Sent().front());
    REQUIRE(result != nullptr);
    CHECK(result->correlationId == 42);
    CHECK(result->accepted);
    CHECK(connection.ReconnectRequests() == 0);
}

TEST_CASE("AdapterIpcSession still reports a resynchronize request accepted, "
          "and still enqueues each baseline sample, when every underlying "
          "capture is unavailable") {
    //  Unavailable is a legitimate per-value state communicated through each
    //  capture's own availability field, not a reason to reject the whole
    //  resync: the game-thread capture path still ran using the approved
    //  native operations. Registration is configured to succeed so this test
    //  isolates per-value unavailability as the only variable; accepted's own
    //  dependence on registration is covered separately.
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    fixture.dispatcher.SetEventRegistered(
        static_cast<std::uint32_t>(CharacterEventKey::kCharacterLevelChanged), true);

    fixture.session.HandleMessage(
        IpcMessage{IpcResynchronizeRequestMessage{.correlationId = 1}});
    fixture.marshaller.RunAllPending();

    REQUIRE(fixture.captureQueue.Enqueued().size() == 3);
    for (const auto& item : fixture.captureQueue.Enqueued()) {
        CHECK(item.availability == CaptureAvailability::kUnavailable);
        CHECK(item.capturedValue.size == 0);
    }
    REQUIRE(connection.Sent().size() == 1);
    auto* result =
        std::get_if<IpcResynchronizeResultMessage>(&connection.Sent().front());
    REQUIRE(result != nullptr);
    CHECK(result->accepted);
}

TEST_CASE("AdapterIpcSession reports a resynchronize request not accepted "
          "when the level-changed event registration itself fails, even "
          "though every baseline sample is recognized and available") {
    //  accepted now truthfully reports admission -- registering the event and
    //  recognizing every requested sample token -- not merely that a capture
    //  attempt ran.
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    fixture.dispatcher.SetEventRegistered(
        static_cast<std::uint32_t>(CharacterEventKey::kCharacterLevelChanged), false);
    fixture.dispatcher.SetSampleResult(
        static_cast<std::uint32_t>(CharacterSampleToken::kCharacterLevelBaseline),
        {std::byte{9}});
    fixture.dispatcher.SetSampleResult(
        static_cast<std::uint32_t>(CharacterSampleToken::kCharacterVitals),
        {std::byte{1}, std::byte{2}, std::byte{3}});
    fixture.dispatcher.SetSampleResult(
        static_cast<std::uint32_t>(CharacterSampleToken::kCharacterXp), {std::byte{7}});

    fixture.session.HandleMessage(
        IpcMessage{IpcResynchronizeRequestMessage{.correlationId = 1}});
    fixture.marshaller.RunAllPending();

    REQUIRE(connection.Sent().size() == 1);
    auto* result =
        std::get_if<IpcResynchronizeResultMessage>(&connection.Sent().front());
    REQUIRE(result != nullptr);
    CHECK_FALSE(result->accepted);
}

TEST_CASE("AdapterIpcSession reports a resynchronize request not accepted, "
          "and does not enqueue a fabricated capture, when a baseline "
          "sample token is unsupported") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    fixture.dispatcher.SetEventRegistered(
        static_cast<std::uint32_t>(CharacterEventKey::kCharacterLevelChanged), true);
    fixture.dispatcher.SetSampleUnsupported(
        static_cast<std::uint32_t>(CharacterSampleToken::kCharacterVitals));
    fixture.dispatcher.SetSampleResult(
        static_cast<std::uint32_t>(CharacterSampleToken::kCharacterLevelBaseline),
        {std::byte{9}});
    fixture.dispatcher.SetSampleResult(
        static_cast<std::uint32_t>(CharacterSampleToken::kCharacterXp), {std::byte{7}});

    fixture.session.HandleMessage(
        IpcMessage{IpcResynchronizeRequestMessage{.correlationId = 1}});
    fixture.marshaller.RunAllPending();

    //  Only the two recognized tokens enqueue; the unsupported one never
    //  fabricates a capture.
    REQUIRE(fixture.captureQueue.Enqueued().size() == 2);
    for (const auto& item : fixture.captureQueue.Enqueued()) {
        CHECK(item.intentKey !=
              static_cast<std::uint32_t>(CharacterSampleToken::kCharacterVitals));
    }
    REQUIRE(connection.Sent().size() == 1);
    auto* result =
        std::get_if<IpcResynchronizeResultMessage>(&connection.Sent().front());
    REQUIRE(result != nullptr);
    CHECK_FALSE(result->accepted);
}

TEST_CASE("AdapterIpcSession reports a resynchronize request not accepted "
          "when a recognized, available baseline sample's capture queue "
          "admission is rejected") {
    //  A recognized sample token is not enough: the bounded, non-blocking
    //  capture queue can still reject it at capacity, and that rejection
    //  must gate accepted the same way an unrecognized token does -- the
    //  host must never be told a baseline was admitted when it never
    //  actually reached the capture handoff queue.
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    fixture.dispatcher.SetEventRegistered(
        static_cast<std::uint32_t>(CharacterEventKey::kCharacterLevelChanged), true);
    fixture.dispatcher.SetSampleResult(
        static_cast<std::uint32_t>(CharacterSampleToken::kCharacterLevelBaseline),
        {std::byte{9}});
    fixture.dispatcher.SetSampleResult(
        static_cast<std::uint32_t>(CharacterSampleToken::kCharacterVitals),
        {std::byte{1}, std::byte{2}, std::byte{3}});
    fixture.dispatcher.SetSampleResult(
        static_cast<std::uint32_t>(CharacterSampleToken::kCharacterXp), {std::byte{7}});
    fixture.captureQueue.SetRejectIntentKey(
        static_cast<std::uint32_t>(CharacterSampleToken::kCharacterVitals));

    fixture.session.HandleMessage(
        IpcMessage{IpcResynchronizeRequestMessage{.correlationId = 1}});
    fixture.marshaller.RunAllPending();

    //  The rejected sample never actually lands in the queue; the other two
    //  still do, since a legitimate Skyrim-value queue rejection is a
    //  per-sample admission failure, not a reason to withhold the rest.
    REQUIRE(fixture.captureQueue.Enqueued().size() == 2);
    for (const auto& item : fixture.captureQueue.Enqueued()) {
        CHECK(item.intentKey !=
              static_cast<std::uint32_t>(CharacterSampleToken::kCharacterVitals));
    }
    REQUIRE(connection.Sent().size() == 1);
    auto* result =
        std::get_if<IpcResynchronizeResultMessage>(&connection.Sent().front());
    REQUIRE(result != nullptr);
    CHECK_FALSE(result->accepted);
}

TEST_CASE("AdapterIpcSession contains an exception thrown by the router's "
          "level-changed event registration inside a marshaled "
          "resynchronize task, sending no result but resetting the "
          "connection so the Host is not left waiting forever") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    fixture.dispatcher.SetEventThrows(
        static_cast<std::uint32_t>(CharacterEventKey::kCharacterLevelChanged));

    fixture.session.HandleMessage(
        IpcMessage{IpcResynchronizeRequestMessage{.correlationId = 1}});

    //  If the exception escaped, it would propagate out of RunAllPending() --
    //  the fake marshaller's stand-in for SKSE's own game-thread task queue --
    //  and fail this test.
    fixture.marshaller.RunAllPending();

    CHECK(fixture.captureQueue.Enqueued().empty());
    CHECK(connection.Sent().empty());
    CHECK(connection.ReconnectRequests() == 1);
}

TEST_CASE("AdapterIpcSession contains an exception thrown mid-sequence by "
          "one baseline sample capture, leaving only the samples captured "
          "before it enqueued, sending no result, and resetting the "
          "connection so the Host is not left waiting forever") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    //  Level baseline is captured first; vitals throws before it or XP ever
    //  enqueue.
    fixture.dispatcher.SetSampleResult(
        static_cast<std::uint32_t>(CharacterSampleToken::kCharacterLevelBaseline),
        {std::byte{9}});
    fixture.dispatcher.SetSampleThrows(
        static_cast<std::uint32_t>(CharacterSampleToken::kCharacterVitals));

    fixture.session.HandleMessage(
        IpcMessage{IpcResynchronizeRequestMessage{.correlationId = 1}});
    fixture.marshaller.RunAllPending();

    REQUIRE(fixture.captureQueue.Enqueued().size() == 1);
    CHECK(fixture.captureQueue.Enqueued().front().intentKey ==
          static_cast<std::uint32_t>(CharacterSampleToken::kCharacterLevelBaseline));
    CHECK(connection.Sent().empty());
    CHECK(connection.ReconnectRequests() == 1);
}

TEST_CASE("AdapterIpcSession resets the connection when the resynchronize "
          "result itself fails to send, so the Host is not left waiting "
          "forever for an outcome that will now never arrive") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    fixture.dispatcher.SetEventRegistered(
        static_cast<std::uint32_t>(CharacterEventKey::kCharacterLevelChanged), true);

    fixture.session.HandleMessage(
        IpcMessage{IpcResynchronizeRequestMessage{.correlationId = 1}});
    connection.RejectNextSend();
    fixture.marshaller.RunAllPending();

    CHECK(connection.Sent().empty());
    CHECK(connection.ReconnectRequests() == 1);
}

TEST_CASE("AdapterIpcSession stamps every resynchronize baseline sample with "
          "the play context most recently sent by SendPlayContextChanged") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    std::array<std::byte, 16> playContextId{
        std::byte{1}, std::byte{2}, std::byte{3}, std::byte{4},
        std::byte{5}, std::byte{6}, std::byte{7}, std::byte{8},
        std::byte{9}, std::byte{10}, std::byte{11}, std::byte{12},
        std::byte{13}, std::byte{14}, std::byte{15}, std::byte{16}};
    fixture.session.SendPlayContextChanged(playContextId);

    fixture.session.HandleMessage(
        IpcMessage{IpcResynchronizeRequestMessage{.correlationId = 1}});
    fixture.marshaller.RunAllPending();

    REQUIRE(fixture.captureQueue.Enqueued().size() == 3);
    for (const auto& item : fixture.captureQueue.Enqueued()) {
        CHECK(item.playContextId == playContextId);
    }
}

TEST_CASE("AdapterIpcSession closes for a pre-authentication resynchronize "
          "request without an attached connection") {
    SessionFixture fixture;

    CHECK(fixture.session.HandleMessage(IpcMessage{IpcResynchronizeRequestMessage{
              .correlationId = 1}}) == AdapterIpcMessageDisposition::kClose);
    CHECK(fixture.marshaller.PendingCount() == 0);
}

TEST_CASE("AdapterIpcSession closes the connection when a resynchronize "
          "request's game-thread dispatch cannot be admitted because the "
          "pending-dispatch bound is full") {
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

    CHECK(fixture.session.HandleMessage(IpcMessage{IpcResynchronizeRequestMessage{
              .correlationId = kMaxPendingGameThreadDispatches + 1}}) ==
          AdapterIpcMessageDisposition::kClose);
    CHECK(fixture.rejectedDispatchCount == 1);
}

TEST_CASE("AdapterIpcSession closes the connection when RunOnGameThread "
          "throws scheduling a resynchronize request's dispatch") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);

    fixture.marshaller.ThrowOnNextSchedule();

    CHECK(fixture.session.HandleMessage(IpcMessage{IpcResynchronizeRequestMessage{
              .correlationId = 1}}) == AdapterIpcMessageDisposition::kClose);
    CHECK(fixture.rejectedDispatchCount == 1);
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
    FakeAdapterNativeCaptureRouter dispatcher;
    FakeAdapterCaptureHandoffQueue captureQueue;
    FakeAdapterPairingNotificationSink pairingNotificationSink;
    AdapterPlayContextState playContextState;
    FakeAdapterIpcConnection connection;

    {
        AdapterIpcSession session{SampleInstanceId(), SampleOwnerLifetimeId(),
                                  marshaller, dispatcher, captureQueue,
                                  pairingNotificationSink, playContextState};
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
    fixture.dispatcher.SetEventRegistered(7, true);

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
    fixture.dispatcher.SetSampleResult(8, {std::byte{2}});

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
    fixture.dispatcher.SetEventRegistered(7, true);

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
    fixture.dispatcher.SetSampleResult(8, {std::byte{2}});

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
    fixture.dispatcher.SetEventRegistered(7, true);

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
    fixture.dispatcher.SetSampleResult(8, {std::byte{2}});
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
    fixture.dispatcher.SetEventRegistered(7, true);
    fixture.dispatcher.SetSampleResult(8, {std::byte{2}});

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

TEST_CASE("AdapterIpcSession replays the active play context to a "
          "newly authenticated generation, so a host that starts a fresh "
          "generation while the same save stays loaded still learns the "
          "current context") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    std::array<std::byte, 16> playContextId{};
    playContextId[0] = std::byte{42};
    fixture.session.SendPlayContextChanged(playContextId);
    REQUIRE(fixture.playContextState.CurrentPlayContext() == playContextId);
    connection.Clear();

    //  Reconnect without a new kNewGame/kPostLoadGame: the save stays loaded,
    //  so nothing else would ever re-announce this context to the new
    //  generation's host.
    fixture.session.HandleDisconnected();
    fixture.session.HandleConnected(fixture.target);
    REQUIRE(connection.Sent().size() == 1);
    auto* hello = std::get_if<IpcHelloMessage>(&connection.Sent().front());
    REQUIRE(hello != nullptr);
    auto expectedProof = ComputeIpcHmacSha256(
        fixture.target.hostProofKey,
        BuildHostProofMessage(hello->challenge, hello->correlationId,
                              hello->adapterInstanceId, hello->ownerLifetimeId));
    connection.Clear();

    AdapterIpcMessageDisposition disposition =
        fixture.session.HandleMessage(IpcMessage{IpcHelloAckMessage{
            .correlationId = hello->correlationId,
            .accepted = true,
            .rejectReason = IpcHelloRejectReason::kNone,
            .hostProof = expectedProof,
        }});
    REQUIRE(disposition == AdapterIpcMessageDisposition::kAuthenticated);

    REQUIRE(connection.Sent().size() == 1);
    auto* notification =
        std::get_if<IpcPlayContextChangedMessage>(&connection.Sent().front());
    REQUIRE(notification != nullptr);
    CHECK(notification->correlationId == 0);
    CHECK(notification->playContextId == playContextId);
}

TEST_CASE("AdapterIpcSession replays the ended play-context state after "
          "the context ends while disconnected") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);

    std::array<std::byte, 16> playContextId{};
    playContextId[0] = std::byte{42};
    fixture.session.SendPlayContextChanged(playContextId);
    REQUIRE(fixture.playContextState.CurrentPlayContext() == playContextId);
    connection.Clear();

    fixture.session.HandleDisconnected();
    fixture.session.SendPlayContextEnded();
    REQUIRE(fixture.playContextState.CurrentPlayContext() == std::nullopt);

    fixture.session.HandleConnected(fixture.target);
    REQUIRE(connection.Sent().size() == 1);
    auto* hello = std::get_if<IpcHelloMessage>(&connection.Sent().front());
    REQUIRE(hello != nullptr);
    auto expectedProof = ComputeIpcHmacSha256(
        fixture.target.hostProofKey,
        BuildHostProofMessage(hello->challenge, hello->correlationId,
                              hello->adapterInstanceId, hello->ownerLifetimeId));
    connection.Clear();

    AdapterIpcMessageDisposition disposition = fixture.session.HandleMessage(
        IpcMessage{IpcHelloAckMessage{.correlationId = hello->correlationId,
                                      .accepted = true,
                                      .rejectReason = IpcHelloRejectReason::kNone,
                                      .hostProof = expectedProof}});
    REQUIRE(disposition == AdapterIpcMessageDisposition::kAuthenticated);

    CHECK(fixture.playContextState.CurrentPlayContext() == std::nullopt);
    REQUIRE(connection.Sent().size() == 1);
    auto* notification =
        std::get_if<IpcPlayContextEndedMessage>(&connection.Sent().front());
    REQUIRE(notification != nullptr);
    CHECK(notification->correlationId == 0);
}

TEST_CASE("AdapterIpcSession replays the ended play-context state when no "
          "context has ever been established") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    connection.Clear();

    fixture.session.HandleDisconnected();
    fixture.session.HandleConnected(fixture.target);
    REQUIRE(connection.Sent().size() == 1);
    auto* hello = std::get_if<IpcHelloMessage>(&connection.Sent().front());
    REQUIRE(hello != nullptr);
    auto expectedProof = ComputeIpcHmacSha256(
        fixture.target.hostProofKey,
        BuildHostProofMessage(hello->challenge, hello->correlationId,
                              hello->adapterInstanceId, hello->ownerLifetimeId));
    connection.Clear();

    AdapterIpcMessageDisposition disposition = fixture.session.HandleMessage(
        IpcMessage{IpcHelloAckMessage{.correlationId = hello->correlationId,
                                      .accepted = true,
                                      .rejectReason = IpcHelloRejectReason::kNone,
                                      .hostProof = expectedProof}});
    REQUIRE(disposition == AdapterIpcMessageDisposition::kAuthenticated);

    REQUIRE(connection.Sent().size() == 1);
    auto* notification =
        std::get_if<IpcPlayContextEndedMessage>(&connection.Sent().front());
    REQUIRE(notification != nullptr);
    CHECK(notification->correlationId == 0);
}

TEST_CASE("AdapterIpcSession does not let a cancellation from an earlier "
          "connection generation cancel a same-numbered request on a later "
          "generation") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    fixture.dispatcher.SetEventRegistered(9, true);

    //  Genuinely admit and cancel correlation id 1 on the first generation --
    //  a real registration exists in gameThreadDispatchCancellation_, not
    //  merely an inbound cancellation for a correlation id nothing was ever
    //  admitted under. Its own marshaled task never runs before disconnect.
    CHECK(fixture.session.HandleMessage(IpcMessage{
              IpcListenEventMessage{.correlationId = 1, .eventKey = 9}}) ==
          AdapterIpcMessageDisposition::kContinue);
    CHECK(fixture.session.HandleMessage(IpcMessage{IpcCancelMessage{
              .correlationId = 1}}) == AdapterIpcMessageDisposition::kContinue);
    REQUIRE(fixture.marshaller.PendingCount() == 1);

    fixture.session.HandleDisconnected();
    fixture.session.HandleConnected(fixture.target);
    connection.Clear();
    Authenticate(fixture.session, connection, fixture.target);

    //  The new generation's host reuses correlation id 1 for an unrelated
    //  request; the old generation's registration -- cleared by
    //  CloseCurrentGenerationLocked -- must not apply to it.
    fixture.dispatcher.SetEventRegistered(7, true);
    CHECK(fixture.session.HandleMessage(IpcMessage{
              IpcListenEventMessage{.correlationId = 1, .eventKey = 7}}) ==
          AdapterIpcMessageDisposition::kContinue);
    fixture.marshaller.RunAllPending();

    //  Only the new generation's dispatch ran; the old generation's own
    //  marshaled task (still queued when it disconnected) self-rejected on its
    //  generation check when the marshaller drained it.
    CHECK(fixture.dispatcher.DispatchedKeys() == std::vector<std::uint32_t>{7});
    //  A listen-event registration produces no captured value of its own.
    CHECK(fixture.captureQueue.Enqueued().empty());
}

TEST_CASE("AdapterIpcSession does not let a stale generation's still-queued "
          "dispatch consume a later generation's own live cancellation "
          "registration for a reused correlation id") {
    //  A race admitting two dispatches under the same correlation id across a
    //  reconnect: ScheduleGameThreadDispatch admits Gen1's dispatch
    //  (registering its own cancellation state), the connection closes before
    //  that dispatch's marshaled task ever runs, Gen2 reconnects and reuses
    //  the same correlation id for an unrelated request (registering its own,
    //  separate cancellation state), and only then does the stale Gen1 task
    //  finally drain. Each dispatch consumes only its own captured
    //  cancellation object -- never a lookup by correlation id -- so the stale
    //  Gen1 task can never erase Gen2's live registration under the same
    //  reused id.
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    fixture.dispatcher.SetEventRegistered(9, true);

    //  Gen1 admits corr=1 and queues its game-thread task; it never runs before
    //  disconnect.
    CHECK(fixture.session.HandleMessage(IpcMessage{
              IpcListenEventMessage{.correlationId = 1, .eventKey = 9}}) ==
          AdapterIpcMessageDisposition::kContinue);
    REQUIRE(fixture.marshaller.PendingCount() == 1);

    fixture.session.HandleDisconnected();
    fixture.session.HandleConnected(fixture.target);
    connection.Clear();
    Authenticate(fixture.session, connection, fixture.target);

    //  Gen2 reuses corr=1 for an unrelated request, queuing its own game-thread
    //  task behind the still-pending Gen1 task.
    fixture.dispatcher.SetEventRegistered(7, true);
    CHECK(fixture.session.HandleMessage(IpcMessage{
              IpcListenEventMessage{.correlationId = 1, .eventKey = 7}}) ==
          AdapterIpcMessageDisposition::kContinue);
    REQUIRE(fixture.marshaller.PendingCount() == 2);

    //  Run only the stale Gen1 task -- it must self-reject on its own
    //  generation mismatch without touching the dispatcher or capture queue.
    fixture.marshaller.RunNextPending();
    CHECK(fixture.dispatcher.DispatchedKeys().empty());
    CHECK(fixture.captureQueue.Enqueued().empty());

    //  The host cancels the Gen2 request. If the stale Gen1 task had erased
    //  Gen2's registration, this cancellation would find nothing and be a
    //  no-op, and Gen2's dispatch would incorrectly run its Skyrim-facing work
    //  below.
    CHECK(fixture.session.HandleMessage(IpcMessage{IpcCancelMessage{
              .correlationId = 1}}) == AdapterIpcMessageDisposition::kContinue);

    fixture.marshaller.RunNextPending();

    //  Gen2's own dispatch honored the cancellation; neither generation's work
    //  ever touched the dispatcher or capture queue.
    CHECK(fixture.dispatcher.DispatchedKeys().empty());
    CHECK(fixture.captureQueue.Enqueued().empty());
}

TEST_CASE("AdapterIpcSession draining several stale queued dispatches after "
          "reconnect does not disturb a new generation's own cancellation "
          "registration") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    fixture.dispatcher.SetEventRegistered(9, true);
    fixture.dispatcher.SetEventRegistered(10, true);

    //  Gen1 admits two cancellable dispatches; neither runs before disconnect.
    //  One of them (corr=1) will have its correlation id reused by Gen2 below.
    CHECK(fixture.session.HandleMessage(IpcMessage{
              IpcListenEventMessage{.correlationId = 1, .eventKey = 9}}) ==
          AdapterIpcMessageDisposition::kContinue);
    CHECK(fixture.session.HandleMessage(IpcMessage{
              IpcListenEventMessage{.correlationId = 2, .eventKey = 10}}) ==
          AdapterIpcMessageDisposition::kContinue);
    REQUIRE(fixture.marshaller.PendingCount() == 2);

    fixture.session.HandleDisconnected();
    fixture.session.HandleConnected(fixture.target);
    connection.Clear();
    Authenticate(fixture.session, connection, fixture.target);

    //  Gen2 reuses corr=1 for a new request, queued behind both stale tasks.
    fixture.dispatcher.SetEventRegistered(7, true);
    CHECK(fixture.session.HandleMessage(IpcMessage{
              IpcListenEventMessage{.correlationId = 1, .eventKey = 7}}) ==
          AdapterIpcMessageDisposition::kContinue);
    REQUIRE(fixture.marshaller.PendingCount() == 3);

    //  Drain every stale Gen1 task before Gen2's own task ever runs.
    fixture.marshaller.RunNextPending();
    fixture.marshaller.RunNextPending();
    CHECK(fixture.dispatcher.DispatchedKeys().empty());

    //  Gen2's registration for the reused id survived both stale dispatches
    //  draining, so a real cancel for it is still honored.
    CHECK(fixture.session.HandleMessage(IpcMessage{IpcCancelMessage{
              .correlationId = 1}}) == AdapterIpcMessageDisposition::kContinue);

    fixture.marshaller.RunNextPending();

    CHECK(fixture.dispatcher.DispatchedKeys().empty());
    CHECK(fixture.captureQueue.Enqueued().empty());
}

TEST_CASE("AdapterIpcSession rejects and closes a listen-event request that "
          "reuses a correlation id already admitted and still outstanding in "
          "the same generation") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    fixture.dispatcher.SetEventRegistered(7, true);
    fixture.dispatcher.SetEventRegistered(8, true);

    CHECK(fixture.session.HandleMessage(IpcMessage{
              IpcListenEventMessage{.correlationId = 1, .eventKey = 7}}) ==
          AdapterIpcMessageDisposition::kContinue);
    REQUIRE(fixture.marshaller.PendingCount() == 1);

    CHECK(fixture.session.HandleMessage(IpcMessage{
              IpcListenEventMessage{.correlationId = 1, .eventKey = 8}}) ==
          AdapterIpcMessageDisposition::kClose);
    REQUIRE(connection.Sent().size() == 1);
    auto* reject = std::get_if<IpcRejectMessage>(&connection.Sent().front());
    REQUIRE(reject != nullptr);
    CHECK(reject->correlationId == 1);
    CHECK(reject->reason == IpcRejectReason::kDuplicateCancellableCorrelationId);
    //  The rejected duplicate was never admitted; the original request's own
    //  registration is untouched.
    CHECK(fixture.marshaller.PendingCount() == 1);

    fixture.marshaller.RunAllPending();

    //  The first request's own registration survived the rejected duplicate
    //  and dispatched normally.
    CHECK(fixture.dispatcher.DispatchedKeys() == std::vector<std::uint32_t>{7});
    CHECK(fixture.captureQueue.Enqueued().empty());
}

TEST_CASE("AdapterIpcSession rejects and closes a read-sample request that "
          "reuses a correlation id already admitted and still outstanding in "
          "the same generation") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    fixture.dispatcher.SetSampleResult(7, {std::byte{1}});
    fixture.dispatcher.SetSampleResult(8, {std::byte{2}});

    CHECK(fixture.session.HandleMessage(IpcMessage{
              IpcReadSampleMessage{.correlationId = 1, .sampleToken = 7}}) ==
          AdapterIpcMessageDisposition::kContinue);
    REQUIRE(fixture.marshaller.PendingCount() == 1);

    CHECK(fixture.session.HandleMessage(IpcMessage{
              IpcReadSampleMessage{.correlationId = 1, .sampleToken = 8}}) ==
          AdapterIpcMessageDisposition::kClose);
    REQUIRE(connection.Sent().size() == 1);
    auto* reject = std::get_if<IpcRejectMessage>(&connection.Sent().front());
    REQUIRE(reject != nullptr);
    CHECK(reject->correlationId == 1);
    CHECK(reject->reason == IpcRejectReason::kDuplicateCancellableCorrelationId);
    CHECK(fixture.marshaller.PendingCount() == 1);

    fixture.marshaller.RunAllPending();

    CHECK(fixture.dispatcher.DispatchedKeys() == std::vector<std::uint32_t>{7});
    REQUIRE(fixture.captureQueue.Enqueued().size() == 1);
    CHECK(fixture.captureQueue.Enqueued().front().intentKey == 7);
}

TEST_CASE("AdapterIpcSession rejects and closes a resynchronization request "
          "that reuses a correlation id already admitted and still "
          "outstanding in the same generation") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);

    CHECK(fixture.session.HandleMessage(IpcMessage{IpcResynchronizeRequestMessage{
              .correlationId = 1}}) == AdapterIpcMessageDisposition::kContinue);
    REQUIRE(fixture.marshaller.PendingCount() == 1);

    CHECK(fixture.session.HandleMessage(IpcMessage{IpcResynchronizeRequestMessage{
              .correlationId = 1}}) == AdapterIpcMessageDisposition::kClose);
    REQUIRE(connection.Sent().size() == 1);
    auto* reject = std::get_if<IpcRejectMessage>(&connection.Sent().front());
    REQUIRE(reject != nullptr);
    CHECK(reject->correlationId == 1);
    CHECK(reject->reason == IpcRejectReason::kDuplicateCancellableCorrelationId);
    CHECK(fixture.marshaller.PendingCount() == 1);

    fixture.marshaller.RunAllPending();

    //  The first request's own registration survived the rejected duplicate
    //  and still sent its own result.
    REQUIRE(connection.Sent().size() == 2);
    auto* result =
        std::get_if<IpcResynchronizeResultMessage>(&connection.Sent().back());
    REQUIRE(result != nullptr);
    CHECK(result->correlationId == 1);
}

TEST_CASE("AdapterIpcSession rejects and closes a pairing-display request "
          "that reuses a correlation id already admitted and still "
          "outstanding in the same generation") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);

    CHECK(fixture.session.HandleMessage(IpcMessage{
              IpcPairingDisplayMessage{.correlationId = 1,
                                       .code = "123456",
                                       .mode = PairingDisplayMode::kInitial}}) ==
          AdapterIpcMessageDisposition::kContinue);
    REQUIRE(fixture.marshaller.PendingCount() == 1);

    CHECK(fixture.session.HandleMessage(IpcMessage{IpcPairingDisplayMessage{
              .correlationId = 1,
              .code = "654321",
              .mode = PairingDisplayMode::kManualRedisplay}}) ==
          AdapterIpcMessageDisposition::kClose);
    REQUIRE(connection.Sent().size() == 1);
    auto* reject = std::get_if<IpcRejectMessage>(&connection.Sent().front());
    REQUIRE(reject != nullptr);
    CHECK(reject->correlationId == 1);
    CHECK(reject->reason == IpcRejectReason::kDuplicateCancellableCorrelationId);
    CHECK(fixture.marshaller.PendingCount() == 1);

    fixture.marshaller.RunAllPending();

    //  The first request's own registration survived the rejected duplicate
    //  and still ran, presenting the original code.
    REQUIRE(connection.Sent().size() == 2);
    auto* ack =
        std::get_if<IpcPairingDisplayAckMessage>(&connection.Sent().back());
    REQUIRE(ack != nullptr);
    CHECK(ack->correlationId == 1);
    REQUIRE(fixture.pairingNotificationSink.Displayed().size() == 1);
    CHECK(fixture.pairingNotificationSink.Displayed().front().first == "123456");
}

TEST_CASE("AdapterIpcSession admits a new listen-event request that reuses a "
          "correlation id already consumed by an earlier request's own "
          "completed dispatch, in the same generation") {
    //  Duplicate rejection is scoped to "still outstanding", not "ever used
    //  this generation": once the first request's own dispatch has run and
    //  unregistered itself, the same correlation id is free to admit a
    //  genuinely new request without being mistaken for a live duplicate.
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    fixture.dispatcher.SetEventRegistered(7, true);
    fixture.dispatcher.SetEventRegistered(8, true);

    CHECK(fixture.session.HandleMessage(IpcMessage{
              IpcListenEventMessage{.correlationId = 1, .eventKey = 7}}) ==
          AdapterIpcMessageDisposition::kContinue);
    fixture.marshaller.RunAllPending();
    REQUIRE(fixture.dispatcher.DispatchedKeys() == std::vector<std::uint32_t>{7});

    CHECK(fixture.session.HandleMessage(IpcMessage{
              IpcListenEventMessage{.correlationId = 1, .eventKey = 8}}) ==
          AdapterIpcMessageDisposition::kContinue);
    fixture.marshaller.RunAllPending();

    CHECK(fixture.dispatcher.DispatchedKeys() ==
          std::vector<std::uint32_t>{7, 8});
    CHECK(fixture.captureQueue.Enqueued().empty());
    //  Both dispatches replied with their own accepted listen-event result.
    REQUIRE(connection.Sent().size() == 2);
    for (const auto& sent : connection.Sent()) {
        auto* result = std::get_if<IpcListenEventResultMessage>(&sent);
        REQUIRE(result != nullptr);
        CHECK(result->correlationId == 1);
        CHECK(result->accepted);
    }
}

TEST_CASE("AdapterIpcSession still returns kClose for a duplicate "
          "cancellable request when the best-effort reject TrySend itself "
          "throws") {
    //  TrySend is not noexcept (see IAdapterIpcConnection::TrySend's own
    //  documentation); a failed best-effort notification of the peer must
    //  never undo the authoritative decision to close a protocol-invalid
    //  connection.
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    fixture.dispatcher.SetEventRegistered(7, true);

    CHECK(fixture.session.HandleMessage(IpcMessage{
              IpcListenEventMessage{.correlationId = 1, .eventKey = 7}}) ==
          AdapterIpcMessageDisposition::kContinue);
    REQUIRE(fixture.marshaller.PendingCount() == 1);

    connection.ThrowOnNextSend();
    AdapterIpcMessageDisposition disposition =
        AdapterIpcMessageDisposition::kContinue;
    REQUIRE_NOTHROW(
        disposition = fixture.session.HandleMessage(IpcMessage{
            IpcListenEventMessage{.correlationId = 1, .eventKey = 8}}));

    CHECK(disposition == AdapterIpcMessageDisposition::kClose);
    //  The duplicate was never admitted; the original request's own
    //  registration is the only one that exists.
    CHECK(fixture.marshaller.PendingCount() == 1);

    fixture.marshaller.RunAllPending();

    //  The original request's own registration survived and dispatched
    //  normally, proving the duplicate never replaced its cancellation state.
    CHECK(fixture.dispatcher.DispatchedKeys() == std::vector<std::uint32_t>{7});
    CHECK(fixture.captureQueue.Enqueued().empty());
}

TEST_CASE("AdapterIpcSession still returns kClose for a duplicate "
          "cancellable request when the best-effort reject TrySend throws a "
          "non-std::exception value") {
    //  This boundary catches with `catch (...)`, not `catch (const
    //  std::exception&)`; prove it contains a non-standard thrown value too.
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    fixture.dispatcher.SetEventRegistered(7, true);

    CHECK(fixture.session.HandleMessage(IpcMessage{
              IpcListenEventMessage{.correlationId = 1, .eventKey = 7}}) ==
          AdapterIpcMessageDisposition::kContinue);
    REQUIRE(fixture.marshaller.PendingCount() == 1);

    connection.ThrowNonStandardOnNextSend();
    AdapterIpcMessageDisposition disposition =
        AdapterIpcMessageDisposition::kContinue;
    REQUIRE_NOTHROW(
        disposition = fixture.session.HandleMessage(IpcMessage{
            IpcListenEventMessage{.correlationId = 1, .eventKey = 8}}));

    CHECK(disposition == AdapterIpcMessageDisposition::kClose);
    CHECK(fixture.marshaller.PendingCount() == 1);

    fixture.marshaller.RunAllPending();

    CHECK(fixture.dispatcher.DispatchedKeys() == std::vector<std::uint32_t>{7});
    CHECK(fixture.captureQueue.Enqueued().empty());
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
    BlockingAdapterNativeCaptureRouter dispatcher{enteredPromise, releaseFuture};
    FakeAdapterCaptureHandoffQueue captureQueue;
    FakeAdapterPairingNotificationSink pairingNotificationSink;
    AdapterPlayContextState playContextState;
    FakeAdapterIpcConnection connection;
    auto session = std::make_unique<AdapterIpcSession>(
        SampleInstanceId(), SampleOwnerLifetimeId(), marshaller, dispatcher,
        captureQueue, pairingNotificationSink, playContextState);
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
    FakeAdapterNativeCaptureRouter dispatcher;
    FakeAdapterCaptureHandoffQueue captureQueue;
    FakeAdapterPairingNotificationSink pairingNotificationSink;
    AdapterPlayContextState playContextState;
    FakeAdapterIpcConnection connection;
    auto session = std::make_unique<AdapterIpcSession>(
        SampleInstanceId(), SampleOwnerLifetimeId(), marshaller, dispatcher,
        captureQueue, pairingNotificationSink, playContextState);
    session->AttachConnection(connection);
    Authenticate(*session, connection, target);

    dispatcher.SetEventRegistered(7, true);
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

TEST_CASE("AdapterIpcSession handles a listen-event request by registering "
          "the key on the game thread and replying with the accepted "
          "result, without enqueuing a captured value") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    fixture.dispatcher.SetEventRegistered(7, true);

    CHECK(fixture.session.HandleMessage(IpcMessage{
              IpcListenEventMessage{.correlationId = 1, .eventKey = 7}}) ==
          AdapterIpcMessageDisposition::kContinue);

    REQUIRE(fixture.captureQueue.Enqueued().empty());
    fixture.marshaller.RunAllPending();

    //  Registration itself produces no captured value: any later capture for
    //  this event arrives through a separate capture path once the
    //  registered native event actually fires.
    CHECK(fixture.dispatcher.DispatchedKeys() == std::vector<std::uint32_t>{7});
    CHECK(fixture.captureQueue.Enqueued().empty());
    REQUIRE(connection.Sent().size() == 1);
    auto* result =
        std::get_if<IpcListenEventResultMessage>(&connection.Sent().front());
    REQUIRE(result != nullptr);
    CHECK(result->correlationId == 1);
    CHECK(result->accepted);
}

TEST_CASE("AdapterIpcSession replies with a rejected result for a "
          "listen-event key with no registered translation") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);

    fixture.session.HandleMessage(
        IpcMessage{IpcListenEventMessage{.correlationId = 1, .eventKey = 99}});
    fixture.marshaller.RunAllPending();

    REQUIRE(connection.Sent().size() == 1);
    auto* result =
        std::get_if<IpcListenEventResultMessage>(&connection.Sent().front());
    REQUIRE(result != nullptr);
    CHECK(result->correlationId == 1);
    CHECK_FALSE(result->accepted);

    CHECK(fixture.dispatcher.DispatchedKeys() == std::vector<std::uint32_t>{99});
    CHECK(fixture.captureQueue.Enqueued().empty());
}

TEST_CASE("AdapterIpcSession enqueues an unavailable capture for a "
          "read-sample token with no registered translation") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);

    fixture.session.HandleMessage(
        IpcMessage{IpcReadSampleMessage{.correlationId = 1, .sampleToken = 99}});
    fixture.marshaller.RunAllPending();

    CHECK(fixture.dispatcher.DispatchedKeys() == std::vector<std::uint32_t>{99});
    REQUIRE(fixture.captureQueue.Enqueued().size() == 1);
    const auto& item = fixture.captureQueue.Enqueued().front();
    CHECK(item.intentKey == 99);
    CHECK(item.correlationId == 1);
    CHECK(item.availability == CaptureAvailability::kUnavailable);
    CHECK(item.capturedValue.size == 0);
}

TEST_CASE("AdapterIpcSession contains an exception thrown by the "
          "dispatcher inside a marshaled listen-event task") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    fixture.dispatcher.SetEventThrows(13);

    fixture.session.HandleMessage(
        IpcMessage{IpcListenEventMessage{.correlationId = 1, .eventKey = 13}});

    //  If the exception escaped, it would propagate out of RunAllPending() --
    //  the fake marshaller's stand-in for SKSE's own game-thread task queue --
    //  and fail this test.
    fixture.marshaller.RunAllPending();

    CHECK(fixture.captureQueue.Enqueued().empty());
}

TEST_CASE("AdapterIpcSession contains an exception thrown by the "
          "dispatcher inside a marshaled read-sample task") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    fixture.dispatcher.SetSampleThrows(13);

    fixture.session.HandleMessage(
        IpcMessage{IpcReadSampleMessage{.correlationId = 1, .sampleToken = 13}});

    //  If the exception escaped, it would propagate out of RunAllPending() --
    //  the fake marshaller's stand-in for SKSE's own game-thread task queue --
    //  and fail this test.
    fixture.marshaller.RunAllPending();

    CHECK(fixture.captureQueue.Enqueued().empty());
}

TEST_CASE("AdapterIpcSession handles a read-sample request by dispatching "
          "the token on the game thread and enqueuing a captured value") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    fixture.dispatcher.SetSampleResult(3, {std::byte{5}});

    CHECK(fixture.session.HandleMessage(IpcMessage{
              IpcReadSampleMessage{.correlationId = 1, .sampleToken = 3}}) ==
          AdapterIpcMessageDisposition::kContinue);
    fixture.marshaller.RunAllPending();

    REQUIRE(fixture.captureQueue.Enqueued().size() == 1);
    CHECK(fixture.captureQueue.Enqueued().front().intentKey == 3);
}

TEST_CASE("AdapterIpcSession sends nothing back for a read-sample request "
          "whose token is unsupported, rather than fabricating an "
          "unavailable capture") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    fixture.dispatcher.SetSampleUnsupported(3);

    CHECK(fixture.session.HandleMessage(IpcMessage{
              IpcReadSampleMessage{.correlationId = 1, .sampleToken = 3}}) ==
          AdapterIpcMessageDisposition::kContinue);
    fixture.marshaller.RunAllPending();

    CHECK(fixture.dispatcher.DispatchedKeys() == std::vector<std::uint32_t>{3});
    CHECK(fixture.captureQueue.Enqueued().empty());
}

TEST_CASE("AdapterIpcSession stamps an enqueued sample capture with the "
          "play context most recently sent by SendPlayContextChanged") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    fixture.dispatcher.SetSampleResult(3, {std::byte{5}});
    std::array<std::byte, 16> playContextId{
        std::byte{1}, std::byte{2}, std::byte{3}, std::byte{4},
        std::byte{5}, std::byte{6}, std::byte{7}, std::byte{8},
        std::byte{9}, std::byte{10}, std::byte{11}, std::byte{12},
        std::byte{13}, std::byte{14}, std::byte{15}, std::byte{16}};
    fixture.session.SendPlayContextChanged(playContextId);

    fixture.session.HandleMessage(IpcMessage{
        IpcReadSampleMessage{.correlationId = 1, .sampleToken = 3}});
    fixture.marshaller.RunAllPending();

    REQUIRE(fixture.captureQueue.Enqueued().size() == 1);
    CHECK(fixture.captureQueue.Enqueued().front().playContextId ==
          playContextId);
}

TEST_CASE("AdapterIpcSession stamps an enqueued sample capture with an "
          "all-zero play context before any transition is ever notified") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    fixture.dispatcher.SetSampleResult(3, {std::byte{5}});

    fixture.session.HandleMessage(IpcMessage{
        IpcReadSampleMessage{.correlationId = 1, .sampleToken = 3}});
    fixture.marshaller.RunAllPending();

    REQUIRE(fixture.captureQueue.Enqueued().size() == 1);
    CHECK(fixture.captureQueue.Enqueued().front().playContextId ==
          std::array<std::byte, 16>{});
}

TEST_CASE("AdapterIpcSession stamps later captures with a second "
          "SendPlayContextChanged value, not the first") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    fixture.dispatcher.SetSampleResult(3, {std::byte{5}});
    std::array<std::byte, 16> firstPlayContextId{};
    firstPlayContextId[0] = std::byte{1};
    std::array<std::byte, 16> secondPlayContextId{};
    secondPlayContextId[0] = std::byte{2};

    fixture.session.SendPlayContextChanged(firstPlayContextId);
    fixture.session.SendPlayContextChanged(secondPlayContextId);
    fixture.session.HandleMessage(IpcMessage{
        IpcReadSampleMessage{.correlationId = 1, .sampleToken = 3}});
    fixture.marshaller.RunAllPending();

    REQUIRE(fixture.captureQueue.Enqueued().size() == 1);
    CHECK(fixture.captureQueue.Enqueued().front().playContextId ==
          secondPlayContextId);
}

TEST_CASE("AdapterIpcSession::SendPlayContextChanged updates the tracked "
          "play context even before authentication") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    fixture.dispatcher.SetSampleResult(3, {std::byte{5}});
    std::array<std::byte, 16> playContextId{};
    playContextId[0] = std::byte{7};

    //  Sent while unauthenticated, so the notification itself is a silent
    //  no-op -- but the tracked value must still update for later captures.
    fixture.session.SendPlayContextChanged(playContextId);
    CHECK(connection.Sent().empty());

    Authenticate(fixture.session, connection, fixture.target);
    fixture.session.HandleMessage(IpcMessage{
        IpcReadSampleMessage{.correlationId = 1, .sampleToken = 3}});
    fixture.marshaller.RunAllPending();

    REQUIRE(fixture.captureQueue.Enqueued().size() == 1);
    CHECK(fixture.captureQueue.Enqueued().front().playContextId ==
          playContextId);
}

TEST_CASE("AdapterIpcSession stamps a capture with an all-zero play context "
          "when no play context is currently active, rather than crashing "
          "or leaving it uninitialized") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    fixture.dispatcher.SetSampleResult(3, {std::byte{5}});
    REQUIRE(fixture.playContextState.CurrentPlayContext() == std::nullopt);

    fixture.session.HandleMessage(IpcMessage{
        IpcReadSampleMessage{.correlationId = 1, .sampleToken = 3}});
    fixture.marshaller.RunAllPending();

    REQUIRE(fixture.captureQueue.Enqueued().size() == 1);
    CHECK(fixture.captureQueue.Enqueued().front().playContextId ==
          std::array<std::byte, 16>{});
}

TEST_CASE("AdapterIpcSession::SendCaptureResult sends a capture result "
          "through the authenticated connection") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);

    std::array<std::byte, 16> playContextId{
        std::byte{1}, std::byte{2}, std::byte{3}, std::byte{4},
        std::byte{5}, std::byte{6}, std::byte{7}, std::byte{8},
        std::byte{9}, std::byte{10}, std::byte{11}, std::byte{12},
        std::byte{13}, std::byte{14}, std::byte{15}, std::byte{16}};
    fixture.session.SendCaptureResult(AdapterCaptureWorkItem{
        .intentKey = 5,
        .capturedValue = dovahlink::adapter::capture::MakeCapturedPayload(
            std::array{std::byte{1}, std::byte{2}}),
        .correlationId = 3,
        .source = CaptureSourceKind::kSample,
        .availability = CaptureAvailability::kAvailable,
        .playContextId = playContextId,
    });

    REQUIRE(connection.Sent().size() == 1);
    auto* captureResult =
        std::get_if<IpcCaptureResultMessage>(&connection.Sent().front());
    REQUIRE(captureResult != nullptr);
    CHECK(captureResult->correlationId == 3);
    CHECK(captureResult->source == CaptureSourceKind::kSample);
    CHECK(captureResult->captureKey == 5);
    CHECK(captureResult->availability == CaptureAvailability::kAvailable);
    CHECK(captureResult->playContextId == playContextId);
    CHECK(captureResult->payload ==
          std::vector<std::byte>{std::byte{1}, std::byte{2}});
    CHECK(connection.ReconnectRequests() == 0);
}

TEST_CASE("AdapterIpcSession::SendCaptureResult does nothing before "
          "authentication") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);

    fixture.session.SendCaptureResult(
        AdapterCaptureWorkItem{.intentKey = 5, .correlationId = 3});

    CHECK(connection.Sent().empty());
}

TEST_CASE("AdapterIpcSession::SendCaptureResult does nothing after "
          "disconnection") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    fixture.session.HandleDisconnected();

    fixture.session.SendCaptureResult(
        AdapterCaptureWorkItem{.intentKey = 5, .correlationId = 3});

    CHECK(connection.Sent().empty());
}

TEST_CASE("AdapterIpcSession::SendCaptureResult contains an exception "
          "thrown by TrySend") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    connection.ThrowOnNextSend();

    REQUIRE_NOTHROW(fixture.session.SendCaptureResult(
        AdapterCaptureWorkItem{.intentKey = 5, .correlationId = 3}));
}

TEST_CASE("AdapterIpcSession::SendCaptureResult resets the connection when "
          "TrySend rejects a reliable Event result") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    connection.RejectNextSend();

    fixture.session.SendCaptureResult(AdapterCaptureWorkItem{
        .intentKey = 5, .correlationId = 0, .source = CaptureSourceKind::kEvent});

    CHECK(connection.Sent().empty());
    CHECK(connection.ReconnectRequests() == 1);
}

TEST_CASE("AdapterIpcSession::SendCaptureResult resets the connection when "
          "TrySend rejects a reliable Event result even with a nonzero "
          "correlation id, proving the Event/zero-correlation-id "
          "classification is a true OR rather than an AND") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    connection.RejectNextSend();

    fixture.session.SendCaptureResult(AdapterCaptureWorkItem{
        .intentKey = 5, .correlationId = 11, .source = CaptureSourceKind::kEvent});

    CHECK(connection.Sent().empty());
    CHECK(connection.ReconnectRequests() == 1);
}

TEST_CASE("AdapterIpcSession::SendCaptureResult resets the connection when "
          "TrySend throws for a reliable Event result") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    connection.ThrowOnNextSend();

    REQUIRE_NOTHROW(fixture.session.SendCaptureResult(AdapterCaptureWorkItem{
        .intentKey = 5, .correlationId = 0, .source = CaptureSourceKind::kEvent}));

    CHECK(connection.ReconnectRequests() == 1);
}

TEST_CASE("AdapterIpcSession::SendCaptureResult resets the connection when "
          "TrySend rejects a resynchronization baseline result -- identified "
          "by its zero correlation id, distinct from a host-directed "
          "ReadSample's own nonzero one") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    connection.RejectNextSend();

    fixture.session.SendCaptureResult(AdapterCaptureWorkItem{
        .intentKey = 5, .correlationId = 0, .source = CaptureSourceKind::kSample});

    CHECK(connection.Sent().empty());
    CHECK(connection.ReconnectRequests() == 1);
}

TEST_CASE("AdapterIpcSession::SendCaptureResult does not reset the "
          "connection when TrySend rejects an ordinary host-requested "
          "sampled result, since the Host's own request timeout recovers it") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    connection.RejectNextSend();

    fixture.session.SendCaptureResult(AdapterCaptureWorkItem{
        .intentKey = 5, .correlationId = 7, .source = CaptureSourceKind::kSample});

    CHECK(connection.Sent().empty());
    CHECK(connection.ReconnectRequests() == 0);
}

TEST_CASE("AdapterIpcSession::SendPlayContextChanged writes through to the "
          "shared play-context state even before authentication") {
    //  AdapterPlayContextState's own get/set behavior is covered directly by
    //  adapter_play_context_state_test.cpp; this proves the session actually
    //  writes through to it, including while unauthenticated.
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);

    CHECK(fixture.playContextState.CurrentPlayContext() == std::nullopt);

    std::array<std::byte, 16> playContextId{};
    playContextId[0] = std::byte{9};
    fixture.session.SendPlayContextChanged(playContextId);

    CHECK(fixture.playContextState.CurrentPlayContext() == playContextId);
}

TEST_CASE("AdapterIpcSession::SendPlayContextChanged sends a notification "
          "through the authenticated connection") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    std::array<std::byte, 16> playContextId{
        std::byte{1}, std::byte{2}, std::byte{3}, std::byte{4},
        std::byte{5}, std::byte{6}, std::byte{7}, std::byte{8},
        std::byte{9}, std::byte{10}, std::byte{11}, std::byte{12},
        std::byte{13}, std::byte{14}, std::byte{15}, std::byte{16}};

    fixture.session.SendPlayContextChanged(playContextId);

    REQUIRE(connection.Sent().size() == 1);
    auto* notification =
        std::get_if<IpcPlayContextChangedMessage>(&connection.Sent().front());
    REQUIRE(notification != nullptr);
    CHECK(notification->correlationId == 0);
    CHECK(notification->playContextId == playContextId);
    CHECK(connection.ReconnectRequests() == 0);
}

TEST_CASE("AdapterIpcSession::SendPlayContextChanged does nothing before "
          "authentication") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);

    fixture.session.SendPlayContextChanged({});

    CHECK(connection.Sent().empty());
}

TEST_CASE("AdapterIpcSession::SendPlayContextChanged does nothing after "
          "disconnection") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    fixture.session.HandleDisconnected();

    fixture.session.SendPlayContextChanged({});

    CHECK(connection.Sent().empty());
}

TEST_CASE("AdapterIpcSession::SendPlayContextChanged contains an exception "
          "thrown by TrySend") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    connection.ThrowOnNextSend();

    REQUIRE_NOTHROW(fixture.session.SendPlayContextChanged({}));
}

TEST_CASE("AdapterIpcSession::SendPlayContextChanged resets the connection "
          "when TrySend rejects the notification, since a lost transition "
          "would leave the Host attributing every later capture to a stale "
          "context forever") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    connection.RejectNextSend();

    fixture.session.SendPlayContextChanged({});

    CHECK(connection.Sent().empty());
    CHECK(connection.ReconnectRequests() == 1);
}

TEST_CASE("AdapterIpcSession::SendPlayContextChanged resets the connection "
          "when TrySend throws for the notification") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    connection.ThrowOnNextSend();

    REQUIRE_NOTHROW(fixture.session.SendPlayContextChanged({}));

    CHECK(connection.ReconnectRequests() == 1);
}

TEST_CASE("AdapterIpcSession::SendPlayContextEnded clears the shared "
          "play-context state even before authentication") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    std::array<std::byte, 16> playContextId{};
    playContextId[0] = std::byte{9};
    fixture.playContextState.SetCurrentPlayContext(playContextId);

    fixture.session.SendPlayContextEnded();

    CHECK(fixture.playContextState.CurrentPlayContext() == std::nullopt);
    CHECK(connection.Sent().empty());
}

TEST_CASE("AdapterIpcSession::SendPlayContextEnded sends a notification "
          "through the authenticated connection") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);

    fixture.session.SendPlayContextEnded();

    REQUIRE(connection.Sent().size() == 1);
    auto* notification =
        std::get_if<IpcPlayContextEndedMessage>(&connection.Sent().front());
    REQUIRE(notification != nullptr);
    CHECK(notification->correlationId == 0);
}

TEST_CASE("AdapterIpcSession::SendPlayContextEnded does nothing before "
          "authentication") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);

    fixture.session.SendPlayContextEnded();

    CHECK(connection.Sent().empty());
}

TEST_CASE("AdapterIpcSession::SendPlayContextEnded does nothing after "
          "disconnection") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    fixture.session.HandleDisconnected();

    fixture.session.SendPlayContextEnded();

    CHECK(connection.Sent().empty());
}

TEST_CASE("AdapterIpcSession::SendPlayContextEnded contains an exception "
          "thrown by TrySend") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    connection.ThrowOnNextSend();

    REQUIRE_NOTHROW(fixture.session.SendPlayContextEnded());
}

TEST_CASE("AdapterIpcSession::SendPlayContextEnded resets the connection "
          "when TrySend rejects the notification, since the Host would "
          "otherwise keep treating a since-ended context as current forever") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    connection.RejectNextSend();

    fixture.session.SendPlayContextEnded();

    CHECK(connection.Sent().empty());
    CHECK(connection.ReconnectRequests() == 1);
}

TEST_CASE("AdapterIpcSession::SendPlayContextEnded resets the connection "
          "when TrySend throws for the notification") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    connection.ThrowOnNextSend();

    REQUIRE_NOTHROW(fixture.session.SendPlayContextEnded());

    CHECK(connection.ReconnectRequests() == 1);
}

TEST_CASE("AdapterIpcSession never dispatches a listen-event or read-sample "
          "request received before any accepted, matching-proof HelloAck") {
    SessionFixture fixture;
    fixture.dispatcher.SetEventRegistered(7, true);
    fixture.dispatcher.SetSampleResult(8, {std::byte{2}});

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
    fixture.dispatcher.SetEventRegistered(7, true);

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
    fixture.dispatcher.SetEventRegistered(7, true);
    fixture.dispatcher.SetSampleResult(8, {std::byte{2}});
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
    auto* reject = std::get_if<IpcRejectMessage>(&connection.Sent().front());
    REQUIRE(reject != nullptr);
    CHECK(reject->correlationId == 5);
    CHECK(reject->reason == IpcRejectReason::kUnknownMessageKind);
}

TEST_CASE("AdapterIpcSession still returns kClose for an unexpected message "
          "kind when the best-effort reject TrySend itself throws") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);

    connection.ThrowOnNextSend();
    AdapterIpcMessageDisposition disposition =
        AdapterIpcMessageDisposition::kContinue;
    REQUIRE_NOTHROW(disposition = fixture.session.HandleMessage(
                        IpcMessage{IpcResynchronizeResultMessage{
                            .correlationId = 5, .accepted = true}}));

    CHECK(disposition == AdapterIpcMessageDisposition::kClose);
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
    auto* close = std::get_if<IpcCloseMessage>(&connection.Sent().front());
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
    fixture.dispatcher.SetEventRegistered(7, true);

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
    fixture.dispatcher.SetEventRegistered(7, true);

    fixture.session.HandleMessage(
        IpcMessage{IpcListenEventMessage{.correlationId = 11, .eventKey = 7}});
    fixture.marshaller.RunAllPending();

    REQUIRE(fixture.dispatcher.DispatchedKeys() == std::vector<std::uint32_t>{7});
    REQUIRE(fixture.captureQueue.Enqueued().empty());

    CHECK(fixture.session.HandleMessage(IpcMessage{IpcCancelMessage{
              .correlationId = 11}}) == AdapterIpcMessageDisposition::kContinue);

    //  The already-produced result is unaffected: cancellation cannot undo
    //  work that already happened.
    CHECK(fixture.dispatcher.DispatchedKeys() == std::vector<std::uint32_t>{7});
    CHECK(fixture.captureQueue.Enqueued().empty());
}

TEST_CASE("AdapterIpcSession cancels only the listen-event request whose "
          "correlation id matches the cancellation") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    fixture.dispatcher.SetEventRegistered(7, true);
    fixture.dispatcher.SetEventRegistered(8, true);

    fixture.session.HandleMessage(
        IpcMessage{IpcListenEventMessage{.correlationId = 11, .eventKey = 7}});
    fixture.session.HandleMessage(
        IpcMessage{IpcListenEventMessage{.correlationId = 12, .eventKey = 8}});
    fixture.session.HandleMessage(
        IpcMessage{IpcCancelMessage{.correlationId = 11}});

    fixture.marshaller.RunAllPending();

    CHECK(fixture.dispatcher.DispatchedKeys() == std::vector<std::uint32_t>{8});
    CHECK(fixture.captureQueue.Enqueued().empty());
}

TEST_CASE("AdapterIpcSession's cancellation state is tied to admitted "
          "dispatches, so saturating the game-thread dispatch bound and "
          "cancelling every one of them cancels all of them") {
    //  Cancellation state lives in a map keyed by admitted correlation id
    //  (see AdapterIpcSession::gameThreadDispatchCancellation_), not an
    //  independently bounded tombstone history: its size is tied to
    //  kMaxPendingGameThreadDispatches by construction, since only an actually
    //  admitted dispatch can ever have an entry. This saturates the dispatch
    //  bound and cancels every one of them, proving every single one is still
    //  cancelled, not merely most of them.
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);

    for (std::uint32_t eventKey = 1; eventKey <= kMaxPendingGameThreadDispatches;
         ++eventKey) {
        fixture.session.HandleMessage(IpcMessage{IpcListenEventMessage{
            .correlationId = eventKey, .eventKey = eventKey}});
        fixture.dispatcher.SetEventRegistered(eventKey, true);
    }
    REQUIRE(fixture.marshaller.PendingCount() == kMaxPendingGameThreadDispatches);

    for (std::uint64_t correlationId = 1;
         correlationId <= kMaxPendingGameThreadDispatches; ++correlationId) {
        fixture.session.HandleMessage(
            IpcMessage{IpcCancelMessage{.correlationId = correlationId}});
    }

    fixture.marshaller.RunAllPending();

    //  None of the queued dispatches touched Skyrim-facing state.
    CHECK(fixture.dispatcher.DispatchedKeys().empty());
    CHECK(fixture.captureQueue.Enqueued().empty());
}

TEST_CASE("AdapterIpcSession's cancellation state is unaffected by a flood "
          "of unknown correlation ids") {
    //  An unknown correlation id finds no entry in the correlation-id-keyed
    //  cancellation map and never inserts one, so it cannot displace the
    //  cancellation state for a genuinely still-queued dispatch.
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    fixture.dispatcher.SetEventRegistered(7, true);

    fixture.session.HandleMessage(
        IpcMessage{IpcListenEventMessage{.correlationId = 1, .eventKey = 7}});
    fixture.session.HandleMessage(
        IpcMessage{IpcCancelMessage{.correlationId = 1}});

    //  Flood many more unknown cancellations than any dispatch could ever be
    //  admitted under, targeting correlation ids no dispatch was ever admitted
    //  under.
    for (std::uint64_t correlationId = 1000;
         correlationId < 1000 + 4 * kMaxPendingGameThreadDispatches;
         ++correlationId) {
        fixture.session.HandleMessage(
            IpcMessage{IpcCancelMessage{.correlationId = correlationId}});
    }

    fixture.marshaller.RunAllPending();

    //  Correlation id 1's genuine cancellation survived the flood.
    CHECK(fixture.dispatcher.DispatchedKeys().empty());
    CHECK(fixture.captureQueue.Enqueued().empty());
}

TEST_CASE("AdapterIpcSession's cancellation state is unaffected by a flood "
          "of duplicate cancellations for the same correlation id") {
    //  A sharper variant of the unknown-id flood: repeated cancellations for
    //  the SAME still-pending correlation id must not grow the underlying
    //  state at all (the map holds at most one entry per correlation id), so
    //  duplicates can never crowd out an unrelated dispatch's own cancellation
    //  either.
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    fixture.dispatcher.SetEventRegistered(7, true);
    fixture.dispatcher.SetEventRegistered(8, true);

    fixture.session.HandleMessage(
        IpcMessage{IpcListenEventMessage{.correlationId = 1, .eventKey = 7}});
    fixture.session.HandleMessage(
        IpcMessage{IpcListenEventMessage{.correlationId = 2, .eventKey = 8}});

    //  Repeatedly cancel correlation id 1 -- idempotent, and must never affect
    //  correlation id 2's own, separate (and here, absent) cancellation state.
    for (int i = 0; i < 4 * static_cast<int>(kMaxPendingGameThreadDispatches);
         ++i) {
        CHECK(fixture.session.HandleMessage(IpcMessage{IpcCancelMessage{
                  .correlationId = 1}}) == AdapterIpcMessageDisposition::kContinue);
    }

    fixture.marshaller.RunAllPending();

    //  Correlation id 1 stayed cancelled; correlation id 2 was never touched
    //  and dispatched normally.
    CHECK(fixture.dispatcher.DispatchedKeys() == std::vector<std::uint32_t>{8});
    CHECK(fixture.captureQueue.Enqueued().empty());
}

TEST_CASE("AdapterIpcSession's cancellation registration is not leaked when "
          "RunOnGameThread itself rejects the dispatch") {
    //  ScheduleGameThreadDispatch registers a cancellable correlation id
    //  before it knows whether the marshaler will actually accept the task;
    //  if RunOnGameThread throws, the registration must be erased in the same
    //  call rather than surviving to falsely mark a later, unrelated dispatch
    //  that reuses the same correlation id as pre-cancelled.
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    fixture.dispatcher.SetEventRegistered(7, true);

    fixture.marshaller.ThrowOnNextSchedule();
    fixture.session.HandleMessage(
        IpcMessage{IpcListenEventMessage{.correlationId = 1, .eventKey = 7}});
    REQUIRE(fixture.marshaller.PendingCount() == 0);

    //  A cancellation for the failed dispatch's correlation id, arriving
    //  after the fact, finds no registration and is a harmless no-op.
    CHECK(fixture.session.HandleMessage(IpcMessage{IpcCancelMessage{
              .correlationId = 1}}) == AdapterIpcMessageDisposition::kContinue);

    //  A later, unrelated dispatch reusing the same correlation id admits and
    //  runs normally -- it is not treated as pre-cancelled by a leaked
    //  registration from the failed admission above.
    fixture.session.HandleMessage(
        IpcMessage{IpcListenEventMessage{.correlationId = 1, .eventKey = 7}});
    fixture.marshaller.RunAllPending();

    CHECK(fixture.dispatcher.DispatchedKeys() == std::vector<std::uint32_t>{7});
    CHECK(fixture.captureQueue.Enqueued().empty());
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
    fixture.dispatcher.SetSampleResult(8, {std::byte{2}});

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
        auto* ack =
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
    auto* ack =
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
    bool Display(const std::string&, PairingDisplayMode) override {
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
    FakeAdapterNativeCaptureRouter dispatcher;
    FakeAdapterCaptureHandoffQueue captureQueue;
    AdapterPlayContextState playContextState;
    FakeAdapterIpcConnection connection;
    AdapterIpcSession session{SampleInstanceId(), SampleOwnerLifetimeId(),
                              marshaller, dispatcher,
                              captureQueue, throwingSink, playContextState};
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
std::pair<std::function<void(TrustAdminRequestResult)>,
          std::future<TrustAdminRequestResult>>
CaptureTrustAdminResult() {
    auto promise = std::make_shared<std::promise<TrustAdminRequestResult>>();
    std::future<TrustAdminRequestResult> future = promise->get_future();
    return {[promise](TrustAdminRequestResult result) {
                promise->set_value(std::move(result));
            },
            std::move(future)};
}

} //  namespace

TEST_CASE("AdapterIpcSession::SendTrustAdminRequest delivers kUnavailable, "
          "without sending, once the game thread runs its callback, when no "
          "authenticated connection is available") {
    SessionFixture fixture;

    auto [onResult, resultFuture] = CaptureTrustAdminResult();
    fixture.session.SendTrustAdminRequest(TrustAdminOperation::kHelp,
                                          std::nullopt, std::nullopt,
                                          std::nullopt, onResult);
    fixture.marshaller.RunAllPending();
    REQUIRE(resultFuture.wait_for(std::chrono::seconds(0)) ==
            std::future_status::ready);
    CHECK(resultFuture.get().outcome == TrustAdminRequestOutcome::kUnavailable);

    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    //  Attached but not yet authenticated.
    auto [onResultUnauthenticated, resultFutureUnauthenticated] =
        CaptureTrustAdminResult();
    fixture.session.SendTrustAdminRequest(TrustAdminOperation::kHelp,
                                          std::nullopt, std::nullopt,
                                          std::nullopt, onResultUnauthenticated);
    fixture.marshaller.RunAllPending();
    REQUIRE(resultFutureUnauthenticated.wait_for(std::chrono::seconds(0)) ==
            std::future_status::ready);
    CHECK(resultFutureUnauthenticated.get().outcome ==
          TrustAdminRequestOutcome::kUnavailable);
    CHECK(connection.Sent().empty());
}

TEST_CASE("AdapterIpcSession's trust-admin completion is accepted and "
          "delivered even while kMaxPendingGameThreadDispatches ordinary "
          "dispatches already saturate the bound they share") {
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
    REQUIRE(fixture.rejectedDispatchCount == 0);
    //  Confirms the bound really is saturated: one more ordinary dispatch is
    //  rejected here.
    fixture.session.HandleMessage(IpcMessage{IpcListenEventMessage{
        .correlationId = kMaxPendingGameThreadDispatches + 1,
        .eventKey = kMaxPendingGameThreadDispatches + 1}});
    REQUIRE(fixture.rejectedDispatchCount == 1);

    auto [onResult, resultFuture] = CaptureTrustAdminResult();
    fixture.session.SendTrustAdminRequest(TrustAdminOperation::kHelp,
                                          std::nullopt, std::nullopt,
                                          std::nullopt, onResult);
    auto* sentRequest =
        std::get_if<IpcTrustAdminRequestMessage>(&connection.Sent().back());
    REQUIRE(sentRequest != nullptr);

    //  A genuine host response arrives while the ordinary bound is still fully
    //  saturated and undrained.
    fixture.session.HandleMessage(IpcMessage{IpcTrustAdminResultMessage{
        .correlationId = sentRequest->correlationId, .resultText = "ok"}});

    //  Not counted against, or dropped by, the saturated ordinary bound: no
    //  new rejection, and the marshaller's pending count grows by exactly one
    //  more than the saturated ordinary bound -- the trust-admin completion's
    //  own dedicated dispatch, admitted independently of
    //  pendingGameThreadDispatchCount_.
    CHECK(fixture.rejectedDispatchCount == 1);
    CHECK(fixture.marshaller.PendingCount() ==
          kMaxPendingGameThreadDispatches + 1);

    //  Drains everything; the trust-admin completion still runs exactly once,
    //  undropped, once the game thread's queue empties.
    fixture.marshaller.RunAllPending();
    REQUIRE(resultFuture.wait_for(std::chrono::seconds(0)) ==
            std::future_status::ready);
    TrustAdminRequestResult result = resultFuture.get();
    CHECK(result.outcome == TrustAdminRequestOutcome::kCompleted);
    REQUIRE(result.resultText.has_value());
    CHECK(*result.resultText == "ok");
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
    auto* sentRequest =
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
    fixture.marshaller.RunAllPending();
    REQUIRE(resultFuture.wait_for(std::chrono::seconds(1)) ==
            std::future_status::ready);
    TrustAdminRequestResult result = resultFuture.get();
    CHECK(result.outcome == TrustAdminRequestOutcome::kCompleted);
    REQUIRE(result.resultText.has_value());
    CHECK(*result.resultText == "ok");
}

TEST_CASE("AdapterIpcSession::SendTrustAdminRequest returns kUnavailable "
          "when the connection rejects the send") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    connection.RejectNextSend();

    auto [onResult, resultFuture] = CaptureTrustAdminResult();
    fixture.session.SendTrustAdminRequest(TrustAdminOperation::kHelp,
                                          std::nullopt, std::nullopt,
                                          std::nullopt, onResult);

    fixture.marshaller.RunAllPending();
    REQUIRE(resultFuture.wait_for(std::chrono::seconds(0)) ==
            std::future_status::ready);
    CHECK(resultFuture.get().outcome == TrustAdminRequestOutcome::kUnavailable);
    CHECK(connection.Sent().empty());
}

TEST_CASE("AdapterIpcSession::SendTrustAdminRequest contains an exception "
          "TrySend itself throws, resolving the request with nullopt "
          "exactly once, sending nothing, and releasing its slot") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    connection.ThrowOnNextSend();

    auto invocationCount = std::make_shared<std::atomic<int>>(0);
    REQUIRE_NOTHROW(fixture.session.SendTrustAdminRequest(
        TrustAdminOperation::kHelp, std::nullopt, std::nullopt, std::nullopt,
        [invocationCount](TrustAdminRequestResult result) {
            CHECK(result.outcome == TrustAdminRequestOutcome::kUnavailable);
            invocationCount->fetch_add(1);
        }));

    fixture.marshaller.RunAllPending();
    CHECK(invocationCount->load() == 1);
    CHECK(connection.Sent().empty());

    //  No pending entry survives a thrown TrySend: if it had leaked, this
    //  force-abandonment sweep would find it and invoke its callback a second
    //  time, taking invocationCount to 2, once drained.
    fixture.session.HandleClosing();
    fixture.marshaller.RunAllPending();
    CHECK(invocationCount->load() == 1);

    //  No timeout worker was spawned for the thrown send: fixture.session's
    //  destructor, reached when this scope ends, would otherwise hang waiting
    //  for activeTrustAdminWaiters_ to reach zero rather than completing
    //  immediately.
}

TEST_CASE("AdapterIpcSession::SendTrustAdminRequest contains a non-"
          "std::exception TrySend throws, resolving the request with "
          "kUnavailable exactly once") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);
    connection.ThrowNonStandardOnNextSend();

    auto invocationCount = std::make_shared<std::atomic<int>>(0);
    REQUIRE_NOTHROW(fixture.session.SendTrustAdminRequest(
        TrustAdminOperation::kHelp, std::nullopt, std::nullopt, std::nullopt,
        [invocationCount](TrustAdminRequestResult result) {
            CHECK(result.outcome == TrustAdminRequestOutcome::kUnavailable);
            invocationCount->fetch_add(1);
        }));

    fixture.marshaller.RunAllPending();
    CHECK(invocationCount->load() == 1);
    CHECK(connection.Sent().empty());
}

TEST_CASE("AdapterIpcSession::SendTrustAdminRequest returns immediately and "
          "its callback fires with kTimedOut only once the bound elapses, "
          "sending a matching IpcCancelMessage and never blocking the "
          "calling thread") {
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
    FakeAdapterNativeCaptureRouter dispatcher;
    FakeAdapterCaptureHandoffQueue captureQueue;
    FakeAdapterPairingNotificationSink pairingNotificationSink;
    AdapterPlayContextState playContextState;
    FakeAdapterIpcConnection connection;
    AdapterIpcSession session{SampleInstanceId(),
                              SampleOwnerLifetimeId(),
                              marshaller,
                              dispatcher,
                              captureQueue,
                              pairingNotificationSink,
                              playContextState,
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

    //  Waits for the timeout worker to actually schedule the game-thread
    //  completion, then runs it -- deterministic even though the worker itself
    //  wakes on a real clock.
    marshaller.WaitForPendingAndRunAll();
    REQUIRE(resultFuture.wait_for(std::chrono::seconds(0)) ==
            std::future_status::ready);
    CHECK(resultFuture.get().outcome == TrustAdminRequestOutcome::kTimedOut);

    REQUIRE(connection.Sent().size() == 2);
    std::uint64_t requestCorrelationId =
        std::get<IpcTrustAdminRequestMessage>(connection.Sent().front())
            .correlationId;
    auto* cancel = std::get_if<IpcCancelMessage>(&connection.Sent().back());
    REQUIRE(cancel != nullptr);
    CHECK(cancel->correlationId == requestCorrelationId);
}

TEST_CASE("AdapterIpcSession::SendTrustAdminRequest's timeout worker sends "
          "no cancellation for a request HandleMessage already resolved "
          "before the worker woke") {
    //  Reuses the deterministic destructor-wait signal from "onResult callback
    //  is invoked exactly once even when HandleMessage resolves the request
    //  before its timeout worker wakes" above: by the time this scope exits,
    //  the worker has already woken and found the entry gone, so if it were
    //  ever going to send a spurious cancellation for an already-completed
    //  request, it would have already done so here -- deterministically, not
    //  by outrunning a clock.
    FixedAdapterIpcPeerProofProvider peerProofProvider{
        {std::byte{9}, std::byte{8}, std::byte{7}}};
    AdapterIpcTarget target{
        .port = 58231,
        .proofToken = peerProofProvider.Token(),
        .hostProofKey = {std::byte{1}, std::byte{1}, std::byte{1}},
        .targetGeneration = 1,
    };
    FakeAdapterTaskMarshaller marshaller;
    FakeAdapterNativeCaptureRouter dispatcher;
    FakeAdapterCaptureHandoffQueue captureQueue;
    FakeAdapterPairingNotificationSink pairingNotificationSink;
    AdapterPlayContextState playContextState;
    FakeAdapterIpcConnection connection;
    {
        auto session = std::make_unique<AdapterIpcSession>(
            SampleInstanceId(), SampleOwnerLifetimeId(), marshaller, dispatcher,
            captureQueue, pairingNotificationSink, playContextState, [] {},
            std::chrono::milliseconds(30));
        session->AttachConnection(connection);
        Authenticate(*session, connection, target);

        auto [onResult, resultFuture] = CaptureTrustAdminResult();
        session->SendTrustAdminRequest(TrustAdminOperation::kHelp, std::nullopt,
                                       std::nullopt, std::nullopt, onResult);
        std::uint64_t correlationId =
            std::get<IpcTrustAdminRequestMessage>(connection.Sent().front())
                .correlationId;

        session->HandleMessage(IpcMessage{IpcTrustAdminResultMessage{
            .correlationId = correlationId, .resultText = "ok"}});
        marshaller.RunAllPending();
        REQUIRE(resultFuture.wait_for(std::chrono::seconds(0)) ==
                std::future_status::ready);
        CHECK(resultFuture.get().outcome == TrustAdminRequestOutcome::kCompleted);

        //  session's destructor blocks until this request's timeout worker has
        //  actually finished (see the destructor tests above).
    }

    //  Only the original request was ever sent; the timeout worker's own
    //  no-op did not send a stray cancellation for an already-completed
    //  request.
    REQUIRE(connection.Sent().size() == 1);
    CHECK(std::holds_alternative<IpcTrustAdminRequestMessage>(
        connection.Sent().front()));
}

TEST_CASE("AdapterIpcSession::SendTrustAdminRequest's timeout worker "
          "contains an exception sending its cancellation throws, still "
          "resolving the request with kTimedOut") {
    FixedAdapterIpcPeerProofProvider peerProofProvider{
        {std::byte{9}, std::byte{8}, std::byte{7}}};
    AdapterIpcTarget target{
        .port = 58231,
        .proofToken = peerProofProvider.Token(),
        .hostProofKey = {std::byte{1}, std::byte{1}, std::byte{1}},
        .targetGeneration = 1,
    };
    FakeAdapterTaskMarshaller marshaller;
    FakeAdapterNativeCaptureRouter dispatcher;
    FakeAdapterCaptureHandoffQueue captureQueue;
    FakeAdapterPairingNotificationSink pairingNotificationSink;
    AdapterPlayContextState playContextState;
    FakeAdapterIpcConnection connection;
    AdapterIpcSession session{SampleInstanceId(),
                              SampleOwnerLifetimeId(),
                              marshaller,
                              dispatcher,
                              captureQueue,
                              pairingNotificationSink,
                              playContextState,
                              [] {},
                              std::chrono::milliseconds(100)};
    session.AttachConnection(connection);
    Authenticate(session, connection, target);

    auto [onResult, resultFuture] = CaptureTrustAdminResult();
    session.SendTrustAdminRequest(TrustAdminOperation::kHelp, std::nullopt,
                                  std::nullopt, std::nullopt, onResult);
    REQUIRE(connection.Sent().size() == 1);
    //  Consumed by the timeout worker's own later cancellation attempt, not by
    //  the request already sent above.
    connection.ThrowOnNextSend();

    marshaller.WaitForPendingAndRunAll();
    REQUIRE(resultFuture.wait_for(std::chrono::seconds(0)) ==
            std::future_status::ready);
    CHECK(resultFuture.get().outcome == TrustAdminRequestOutcome::kTimedOut);
}

TEST_CASE("AdapterIpcSession::SendTrustAdminRequest resolves with the "
          "host's correlated result once the game thread runs its callback "
          "after HandleMessage delivers it") {
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

    fixture.marshaller.RunAllPending();
    REQUIRE(resultFuture.wait_for(std::chrono::seconds(0)) ==
            std::future_status::ready);
    TrustAdminRequestResult result = resultFuture.get();
    CHECK(result.outcome == TrustAdminRequestOutcome::kCompleted);
    REQUIRE(result.resultText.has_value());
    CHECK(*result.resultText == "Reset Trust complete (0 devices revoked).");
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

    fixture.marshaller.RunAllPending();
    REQUIRE(firstResultFuture.wait_for(std::chrono::seconds(0)) ==
            std::future_status::ready);
    REQUIRE(secondResultFuture.wait_for(std::chrono::seconds(0)) ==
            std::future_status::ready);
    TrustAdminRequestResult first = firstResultFuture.get();
    TrustAdminRequestResult second = secondResultFuture.get();
    CHECK(first.outcome == TrustAdminRequestOutcome::kCompleted);
    CHECK(second.outcome == TrustAdminRequestOutcome::kCompleted);
    REQUIRE(first.resultText.has_value());
    REQUIRE(second.resultText.has_value());
    CHECK(*first.resultText == "first");
    CHECK(*second.resultText == "second");
}

TEST_CASE("AdapterIpcSession::SendTrustAdminRequest's onResult callback is "
          "invoked exactly once even when HandleMessage resolves the "
          "request before its timeout worker wakes") {
    //  A short injected timeout keeps this test fast. Rather than sleeping
    //  past it, the destructor's own wait for every timeout worker to finish
    //  (proven separately) is reused here as the deterministic signal that
    //  this request's worker has actually woken, found the entry already
    //  resolved by HandleMessage below, and observed it as a no-op, without
    //  this test relying on a timing sleep to prove that.
    FixedAdapterIpcPeerProofProvider peerProofProvider{
        {std::byte{9}, std::byte{8}, std::byte{7}}};
    AdapterIpcTarget target{
        .port = 58231,
        .proofToken = peerProofProvider.Token(),
        .hostProofKey = {std::byte{1}, std::byte{1}, std::byte{1}},
        .targetGeneration = 1,
    };
    FakeAdapterTaskMarshaller marshaller;
    FakeAdapterNativeCaptureRouter dispatcher;
    FakeAdapterCaptureHandoffQueue captureQueue;
    FakeAdapterPairingNotificationSink pairingNotificationSink;
    AdapterPlayContextState playContextState;
    FakeAdapterIpcConnection connection;
    auto invocationCount = std::make_shared<std::atomic<int>>(0);
    {
        auto session = std::make_unique<AdapterIpcSession>(
            SampleInstanceId(), SampleOwnerLifetimeId(), marshaller, dispatcher,
            captureQueue, pairingNotificationSink, playContextState, [] {},
            std::chrono::milliseconds(30));
        session->AttachConnection(connection);
        Authenticate(*session, connection, target);

        session->SendTrustAdminRequest(TrustAdminOperation::kHelp, std::nullopt,
                                       std::nullopt, std::nullopt,
                                       [invocationCount](TrustAdminRequestResult) {
                                           invocationCount->fetch_add(1);
                                       });
        std::uint64_t correlationId =
            std::get<IpcTrustAdminRequestMessage>(connection.Sent().front())
                .correlationId;

        session->HandleMessage(IpcMessage{IpcTrustAdminResultMessage{
            .correlationId = correlationId, .resultText = "ok"}});
        marshaller.RunAllPending();
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
        [](TrustAdminRequestResult) {
            throw std::runtime_error("onResult failed");
        });
    std::uint64_t correlationId =
        std::get<IpcTrustAdminRequestMessage>(connection.Sent().front())
            .correlationId;

    CHECK(fixture.session.HandleMessage(IpcMessage{IpcTrustAdminResultMessage{
              .correlationId = correlationId, .resultText = "ok"}}) ==
          AdapterIpcMessageDisposition::kContinue);
    //  The callback itself only actually runs once the game-thread dispatch is
    //  drained; that is where its exception must be contained now.
    REQUIRE_NOTHROW(fixture.marshaller.RunAllPending());
}

TEST_CASE("AdapterIpcSession contains an exception a trust-admin onResult "
          "callback throws when invoked immediately for an unauthenticated "
          "session") {
    SessionFixture fixture;

    fixture.session.SendTrustAdminRequest(
        TrustAdminOperation::kHelp, std::nullopt, std::nullopt, std::nullopt,
        [](TrustAdminRequestResult) {
            throw std::runtime_error("onResult failed");
        });
    //  Even an immediate, no-connection outcome is delivered through the
    //  game-thread dispatch, not invoked directly here -- its exception must
    //  be contained once that dispatch is drained.
    REQUIRE_NOTHROW(fixture.marshaller.RunAllPending());
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
          "kTimedOut, once the game thread runs its callback, when "
          "disconnect ends the connection while the request is outstanding") {
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

    fixture.marshaller.RunAllPending();
    REQUIRE(resultFuture.wait_for(std::chrono::seconds(0)) ==
            std::future_status::ready);
    //  Already sent when the connection ended, so its submission to the host
    //  cannot be ruled out: kTimedOut, not kUnavailable.
    CHECK(resultFuture.get().outcome == TrustAdminRequestOutcome::kTimedOut);
}

TEST_CASE("AdapterIpcSession::SendTrustAdminRequest's callback delivers "
          "kTimedOut, once the game thread runs its callback, when logical "
          "closing ends the connection while the request is outstanding") {
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

    fixture.marshaller.RunAllPending();
    REQUIRE(resultFuture.wait_for(std::chrono::seconds(0)) ==
            std::future_status::ready);
    CHECK(resultFuture.get().outcome == TrustAdminRequestOutcome::kTimedOut);
}

TEST_CASE("AdapterIpcSession::HandleClosing does not deadlock when its "
          "abandoned request's onResult callback reenters IsHostAvailable, "
          "and the callback fires exactly once") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);

    auto invocationCount = std::make_shared<std::atomic<int>>(0);
    fixture.session.SendTrustAdminRequest(
        TrustAdminOperation::kHelp, std::nullopt, std::nullopt, std::nullopt,
        [&fixture, invocationCount](TrustAdminRequestResult) {
            invocationCount->fetch_add(1);
            //  Runs later, once the game-thread dispatch is drained -- entirely
            //  outside HandleClosing's own call frame -- so this reentrant call
            //  can never observe availableMutex_ still held by it.
            (void)fixture.session.IsHostAvailable();
        });
    REQUIRE(connection.Sent().size() == 1);

    fixture.session.HandleClosing();

    fixture.marshaller.RunAllPending();
    CHECK(invocationCount->load() == 1);
}

TEST_CASE("AdapterIpcSession::HandleDisconnected does not deadlock when its "
          "abandoned request's onResult callback reenters IsHostAvailable, "
          "and the callback fires exactly once") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);

    auto invocationCount = std::make_shared<std::atomic<int>>(0);
    fixture.session.SendTrustAdminRequest(
        TrustAdminOperation::kHelp, std::nullopt, std::nullopt, std::nullopt,
        [&fixture, invocationCount](TrustAdminRequestResult) {
            invocationCount->fetch_add(1);
            //  Runs later, once the game-thread dispatch is drained -- entirely
            //  outside HandleDisconnected's own call frame -- so this reentrant
            //  call can never observe availableMutex_ still held by it.
            (void)fixture.session.IsHostAvailable();
        });
    REQUIRE(connection.Sent().size() == 1);

    fixture.session.HandleDisconnected();

    fixture.marshaller.RunAllPending();
    CHECK(invocationCount->load() == 1);
}

TEST_CASE("AdapterIpcSession::HandleClosing does not deadlock when its "
          "abandoned request's onResult callback reenters "
          "SendTrustAdminRequest, and each callback fires exactly once") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);

    auto invocationCount = std::make_shared<std::atomic<int>>(0);
    auto reentrantInvocationCount = std::make_shared<std::atomic<int>>(0);
    fixture.session.SendTrustAdminRequest(
        TrustAdminOperation::kHelp, std::nullopt, std::nullopt, std::nullopt,
        [&fixture, invocationCount,
         reentrantInvocationCount](TrustAdminRequestResult) {
            invocationCount->fetch_add(1);
            //  Runs later, once the game-thread dispatch is drained -- entirely
            //  outside HandleClosing's own call frame -- so this reentrant call
            //  can never observe availableMutex_ still held by it. It still
            //  observes the generation already closed and resolves with
            //  kUnavailable, itself delivered through another queued dispatch
            //  that FakeAdapterTaskMarshaller::RunAllPending keeps draining
            //  until no task remains.
            fixture.session.SendTrustAdminRequest(
                TrustAdminOperation::kHelp, std::nullopt, std::nullopt,
                std::nullopt,
                [reentrantInvocationCount](TrustAdminRequestResult result) {
                    CHECK(result.outcome == TrustAdminRequestOutcome::kUnavailable);
                    reentrantInvocationCount->fetch_add(1);
                });
        });
    REQUIRE(connection.Sent().size() == 1);

    fixture.session.HandleClosing();

    fixture.marshaller.RunAllPending();
    CHECK(invocationCount->load() == 1);
    CHECK(reentrantInvocationCount->load() == 1);
    //  The reentrant call observed the generation already closed, so it never
    //  sent a new request.
    CHECK(connection.Sent().size() == 1);
}

TEST_CASE("AdapterIpcSession::HandleDisconnected does not deadlock when its "
          "abandoned request's onResult callback reenters "
          "SendTrustAdminRequest, and each callback fires exactly once") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);

    auto invocationCount = std::make_shared<std::atomic<int>>(0);
    auto reentrantInvocationCount = std::make_shared<std::atomic<int>>(0);
    fixture.session.SendTrustAdminRequest(
        TrustAdminOperation::kHelp, std::nullopt, std::nullopt, std::nullopt,
        [&fixture, invocationCount,
         reentrantInvocationCount](TrustAdminRequestResult) {
            invocationCount->fetch_add(1);
            //  Runs later, once the game-thread dispatch is drained -- entirely
            //  outside HandleDisconnected's own call frame -- so this reentrant
            //  call can never observe availableMutex_ still held by it. It
            //  still observes the generation already closed and resolves with
            //  kUnavailable, itself delivered through another queued dispatch
            //  that FakeAdapterTaskMarshaller::RunAllPending keeps draining
            //  until no task remains.
            fixture.session.SendTrustAdminRequest(
                TrustAdminOperation::kHelp, std::nullopt, std::nullopt,
                std::nullopt,
                [reentrantInvocationCount](TrustAdminRequestResult result) {
                    CHECK(result.outcome == TrustAdminRequestOutcome::kUnavailable);
                    reentrantInvocationCount->fetch_add(1);
                });
        });
    REQUIRE(connection.Sent().size() == 1);

    fixture.session.HandleDisconnected();

    fixture.marshaller.RunAllPending();
    CHECK(invocationCount->load() == 1);
    CHECK(reentrantInvocationCount->load() == 1);
    //  The reentrant call observed the generation already closed, so it never
    //  sent a new request.
    CHECK(connection.Sent().size() == 1);
}

TEST_CASE("AdapterIpcSession's destructor waits for an outstanding "
          "trust-admin request's timeout worker to finish, resolving it "
          "with kTimedOut") {
    FixedAdapterIpcPeerProofProvider peerProofProvider{
        {std::byte{9}, std::byte{8}, std::byte{7}}};
    AdapterIpcTarget target{
        .port = 58231,
        .proofToken = peerProofProvider.Token(),
        .hostProofKey = {std::byte{1}, std::byte{1}, std::byte{1}},
        .targetGeneration = 1,
    };
    FakeAdapterTaskMarshaller marshaller;
    FakeAdapterNativeCaptureRouter dispatcher;
    FakeAdapterCaptureHandoffQueue captureQueue;
    FakeAdapterPairingNotificationSink pairingNotificationSink;
    AdapterPlayContextState playContextState;
    FakeAdapterIpcConnection connection;
    std::future<TrustAdminRequestResult> resultFuture;
    {
        auto session = std::make_unique<AdapterIpcSession>(
            SampleInstanceId(), SampleOwnerLifetimeId(), marshaller, dispatcher,
            captureQueue, pairingNotificationSink, playContextState, [] {},
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

    //  The destructor's own force-abandonment queues this result onto
    //  marshaller -- a variable outside the destroyed session's own lifetime
    //  -- rather than losing it; draining marshaller here, after the session
    //  no longer exists, is the proof that the queued completion never
    //  dereferences it.
    marshaller.RunAllPending();
    //  A defined kTimedOut (from the destructor's own force-abandonment)
    //  rather than a crash or hang proves the destructor actually waited.
    REQUIRE(resultFuture.wait_for(std::chrono::seconds(0)) ==
            std::future_status::ready);
    CHECK(resultFuture.get().outcome == TrustAdminRequestOutcome::kTimedOut);
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
    FakeAdapterNativeCaptureRouter dispatcher;
    FakeAdapterCaptureHandoffQueue captureQueue;
    FakeAdapterPairingNotificationSink pairingNotificationSink;
    AdapterPlayContextState playContextState;
    FakeAdapterIpcConnection connection;
    auto session = std::make_unique<AdapterIpcSession>(
        SampleInstanceId(), SampleOwnerLifetimeId(), marshaller, dispatcher,
        captureQueue, pairingNotificationSink, playContextState, [] {},
        std::chrono::milliseconds(50));
    session->AttachConnection(connection);
    Authenticate(*session, connection, target);

    std::future<void> sendEntered = connection.BlockNextSend();
    auto [onResult, resultFuture] = CaptureTrustAdminResult();
    AdapterIpcSession* sessionPtr = session.get();
    std::future<void> sendCall = std::async(std::launch::async, [sessionPtr,
                                                                 onResult] {
        sessionPtr->SendTrustAdminRequest(TrustAdminOperation::kHelp, std::nullopt,
                                          std::nullopt, std::nullopt, onResult);
    });
    REQUIRE(sendEntered.wait_for(std::chrono::seconds(1)) ==
            std::future_status::ready);

    //  SendTrustAdminRequest is blocked inside TrySend, on another thread,
    //  strictly after registering its pending entry and incrementing
    //  activeTrustAdminWaiters_ (both happen, under the same lock, before
    //  TrySend is ever called). Destroying the session concurrently here must
    //  wait for this in-flight call to finish touching
    //  trustAdminMutex_-guarded state before tearing it down: the destructor
    //  must never observe zero in-flight requests and destroy
    //  trustAdminMutex_/trustAdminCondition_ while this blocked call is still
    //  about to lock them.
    std::thread destroyer([&session] { session.reset(); });
    connection.ReleaseBlockedSend();

    sendCall.get();
    destroyer.join();

    marshaller.WaitForPendingAndRunAll();
    REQUIRE(resultFuture.wait_for(std::chrono::seconds(0)) ==
            std::future_status::ready);
    CHECK(resultFuture.get().outcome == TrustAdminRequestOutcome::kTimedOut);
}

TEST_CASE("AdapterIpcSession::SendTrustAdminRequest's admission is atomic "
          "with HandleClosing: a request already inside TrySend is admitted "
          "and abandoned by the close, never left pending outside its "
          "generation") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);

    std::future<void> sendEntered = connection.BlockNextSend();
    auto [onResult, resultFuture] = CaptureTrustAdminResult();
    AdapterIpcSession* sessionPtr = &fixture.session;
    std::future<void> sendCall = std::async(std::launch::async, [sessionPtr,
                                                                 onResult] {
        sessionPtr->SendTrustAdminRequest(TrustAdminOperation::kHelp, std::nullopt,
                                          std::nullopt, std::nullopt, onResult);
    });
    REQUIRE(sendEntered.wait_for(std::chrono::seconds(1)) ==
            std::future_status::ready);

    //  SendTrustAdminRequest is blocked inside TrySend while still holding
    //  availableMutex_ (registration and the activeTrustAdminWaiters_
    //  increment both already happened under that same lock). HandleClosing
    //  needs that same lock for CloseCurrentGenerationLocked, so this
    //  concurrent call cannot observe or act on the session until the blocked
    //  call above releases it -- proving the two can never interleave.
    std::thread closer([&fixture] { fixture.session.HandleClosing(); });
    connection.ReleaseBlockedSend();

    sendCall.get();
    closer.join();

    //  A bounded wait, well inside SessionFixture's default 5-second
    //  kTrustAdminRequestTimeout, proves this kTimedOut came from HandleClosing's
    //  own force-abandonment rather than the timeout coincidentally landing
    //  first.
    fixture.marshaller.WaitForPendingAndRunAll();
    REQUIRE(resultFuture.wait_for(std::chrono::seconds(0)) ==
            std::future_status::ready);
    CHECK(resultFuture.get().outcome == TrustAdminRequestOutcome::kTimedOut);
}

TEST_CASE("AdapterIpcSession::SendTrustAdminRequest delivers kUnavailable "
          "and sends nothing when HandleClosing has already closed the "
          "generation") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);

    fixture.session.HandleClosing();

    auto [onResult, resultFuture] = CaptureTrustAdminResult();
    fixture.session.SendTrustAdminRequest(TrustAdminOperation::kHelp,
                                          std::nullopt, std::nullopt,
                                          std::nullopt, onResult);

    fixture.marshaller.RunAllPending();
    REQUIRE(resultFuture.wait_for(std::chrono::seconds(0)) ==
            std::future_status::ready);
    CHECK(resultFuture.get().outcome == TrustAdminRequestOutcome::kUnavailable);
    CHECK(connection.Sent().empty());
}

TEST_CASE("AdapterIpcSession::SendTrustAdminRequest's requests admitted "
          "before a close are each abandoned by their own exact correlation "
          "id, not merely 'some' pending request, and not by waiting out "
          "the timeout") {
    FixedAdapterIpcPeerProofProvider peerProofProvider{
        {std::byte{9}, std::byte{8}, std::byte{7}}};
    AdapterIpcTarget target{
        .port = 58231,
        .proofToken = peerProofProvider.Token(),
        .hostProofKey = {std::byte{1}, std::byte{1}, std::byte{1}},
        .targetGeneration = 1,
    };
    FakeAdapterTaskMarshaller marshaller;
    FakeAdapterNativeCaptureRouter dispatcher;
    FakeAdapterCaptureHandoffQueue captureQueue;
    FakeAdapterPairingNotificationSink pairingNotificationSink;
    AdapterPlayContextState playContextState;
    FakeAdapterIpcConnection connection;
    //  Deliberately much longer than this test's own short assertion wait
    //  below: if abandonment were ever delivered by the timeout worker instead
    //  of HandleClosing's own sweep, this test would time out rather than
    //  pass, rather than this test relying on a timing sleep to prove which
    //  path actually resolved it.
    AdapterIpcSession session(
        SampleInstanceId(), SampleOwnerLifetimeId(), marshaller, dispatcher,
        captureQueue, pairingNotificationSink, playContextState, [] {},
        std::chrono::minutes(10));
    session.AttachConnection(connection);
    Authenticate(session, connection, target);

    auto [firstOnResult, firstResultFuture] = CaptureTrustAdminResult();
    session.SendTrustAdminRequest(TrustAdminOperation::kHelp, std::nullopt,
                                  std::nullopt, std::nullopt, firstOnResult);
    auto [secondOnResult, secondResultFuture] = CaptureTrustAdminResult();
    session.SendTrustAdminRequest(TrustAdminOperation::kResetTrust, std::nullopt,
                                  std::nullopt, std::nullopt, secondOnResult);
    REQUIRE(connection.Sent().size() == 2);

    session.HandleClosing();
    marshaller.RunAllPending();

    //  Each request's own captured future is tied to its own correlation id by
    //  construction (a distinct pending-map entry and callback per call): if
    //  the close path ever cross-delivered or dropped one, the corresponding
    //  future below would never become ready rather than merely carrying the
    //  wrong value.
    REQUIRE(firstResultFuture.wait_for(std::chrono::seconds(0)) ==
            std::future_status::ready);
    REQUIRE(secondResultFuture.wait_for(std::chrono::seconds(0)) ==
            std::future_status::ready);
    CHECK(firstResultFuture.get().outcome == TrustAdminRequestOutcome::kTimedOut);
    CHECK(secondResultFuture.get().outcome ==
          TrustAdminRequestOutcome::kTimedOut);
}

TEST_CASE("AdapterIpcSession::SendTrustAdminRequest's callback is never "
          "invoked a second time by a stale result delivered after "
          "HandleClosing already abandoned the request") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);

    auto invocationCount = std::make_shared<std::atomic<int>>(0);
    fixture.session.SendTrustAdminRequest(
        TrustAdminOperation::kHelp, std::nullopt, std::nullopt, std::nullopt,
        [invocationCount](TrustAdminRequestResult) {
            invocationCount->fetch_add(1);
        });
    std::uint64_t correlationId =
        std::get<IpcTrustAdminRequestMessage>(connection.Sent().front())
            .correlationId;

    fixture.session.HandleClosing();
    fixture.marshaller.RunAllPending();
    CHECK(invocationCount->load() == 1);

    //  HandleDisconnected is a no-op for an already-closed generation (see
    //  "HandleClosing and HandleDisconnected cooperate safely" above), and a
    //  stale result for the same, already-abandoned correlation id must not
    //  resolve anything a second time.
    fixture.session.HandleDisconnected();
    CHECK(fixture.session.HandleMessage(IpcMessage{IpcTrustAdminResultMessage{
              .correlationId = correlationId, .resultText = "late"}}) ==
          AdapterIpcMessageDisposition::kClose);
    CHECK(invocationCount->load() == 1);
}

TEST_CASE("AdapterIpcSession::HandleClosing abandons only the trust-admin "
          "requests still pending, leaving one already resolved by "
          "HandleMessage untouched") {
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
    fixture.session.HandleMessage(IpcMessage{
        IpcTrustAdminResultMessage{.correlationId = firstCorrelationId,
                                   .resultText = "resolved-before-close"}});
    fixture.marshaller.RunAllPending();
    REQUIRE(firstResultFuture.wait_for(std::chrono::seconds(0)) ==
            std::future_status::ready);

    auto [secondOnResult, secondResultFuture] = CaptureTrustAdminResult();
    fixture.session.SendTrustAdminRequest(TrustAdminOperation::kResetTrust,
                                          std::nullopt, std::nullopt,
                                          std::nullopt, secondOnResult);

    //  Only the second request is still pending when the close sweep runs; the
    //  first must be left exactly as HandleMessage already resolved it, not
    //  re-abandoned or re-delivered.
    fixture.session.HandleClosing();
    fixture.marshaller.RunAllPending();

    TrustAdminRequestResult firstResult = firstResultFuture.get();
    CHECK(firstResult.outcome == TrustAdminRequestOutcome::kCompleted);
    REQUIRE(firstResult.resultText.has_value());
    CHECK(*firstResult.resultText == "resolved-before-close");
    REQUIRE(secondResultFuture.wait_for(std::chrono::seconds(0)) ==
            std::future_status::ready);
    CHECK(secondResultFuture.get().outcome ==
          TrustAdminRequestOutcome::kTimedOut);
}

TEST_CASE("AdapterIpcSession::SendTrustAdminRequest admits exactly "
          "kMaxPendingTrustAdminRequests outstanding requests and rejects "
          "the next one immediately, sending nothing for it") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);

    std::vector<std::future<TrustAdminRequestResult>> resultFutures;
    for (std::size_t i = 0; i < kMaxPendingTrustAdminRequests; ++i) {
        auto [onResult, resultFuture] = CaptureTrustAdminResult();
        fixture.session.SendTrustAdminRequest(TrustAdminOperation::kHelp,
                                              std::nullopt, std::nullopt,
                                              std::nullopt, onResult);
        resultFutures.push_back(std::move(resultFuture));
    }
    REQUIRE(connection.Sent().size() == kMaxPendingTrustAdminRequests);

    auto [rejectedOnResult, rejectedResultFuture] = CaptureTrustAdminResult();
    fixture.session.SendTrustAdminRequest(TrustAdminOperation::kHelp,
                                          std::nullopt, std::nullopt,
                                          std::nullopt, rejectedOnResult);

    //  Rejected at the bound, sending nothing, once the game thread runs its
    //  callback.
    fixture.marshaller.RunAllPending();
    REQUIRE(rejectedResultFuture.wait_for(std::chrono::seconds(0)) ==
            std::future_status::ready);
    CHECK(rejectedResultFuture.get().outcome ==
          TrustAdminRequestOutcome::kUnavailable);
    CHECK(connection.Sent().size() == kMaxPendingTrustAdminRequests);

    //  None of the already-admitted requests were evicted or disturbed by the
    //  rejection.
    for (auto& future : resultFutures) {
        CHECK(future.wait_for(std::chrono::seconds(0)) !=
              std::future_status::ready);
    }
}

TEST_CASE("AdapterIpcSession releases a timed-out trust-admin request's "
          "capacity slot as soon as its timeout worker finishes, decoupled "
          "from whether its queued game-thread completion has been drained "
          "yet") {
    //  The timeout worker only schedules the completion and returns
    //  immediately, so its TrustAdminWaiterGuard slot releases well before --
    //  and regardless of -- whenever the queued completion actually runs. This
    //  test proves that directly: every request below times out, but none of
    //  their queued completions are ever drained, and a new request is still
    //  admitted.
    FixedAdapterIpcPeerProofProvider peerProofProvider{
        {std::byte{9}, std::byte{8}, std::byte{7}}};
    AdapterIpcTarget target{
        .port = 58231,
        .proofToken = peerProofProvider.Token(),
        .hostProofKey = {std::byte{1}, std::byte{1}, std::byte{1}},
        .targetGeneration = 1,
    };
    FakeAdapterTaskMarshaller marshaller;
    FakeAdapterNativeCaptureRouter dispatcher;
    FakeAdapterCaptureHandoffQueue captureQueue;
    FakeAdapterPairingNotificationSink pairingNotificationSink;
    AdapterPlayContextState playContextState;
    FakeAdapterIpcConnection connection;
    //  Short enough to keep this test fast: every request below is resolved by
    //  its own timeout firing, not by HandleMessage.
    AdapterIpcSession session(
        SampleInstanceId(), SampleOwnerLifetimeId(), marshaller, dispatcher,
        captureQueue, pairingNotificationSink, playContextState, [] {},
        std::chrono::milliseconds(30));
    session.AttachConnection(connection);
    Authenticate(session, connection, target);

    std::vector<std::future<TrustAdminRequestResult>> resultFutures;
    for (std::size_t i = 0; i < kMaxPendingTrustAdminRequests; ++i) {
        auto [onResult, resultFuture] = CaptureTrustAdminResult();
        session.SendTrustAdminRequest(TrustAdminOperation::kHelp, std::nullopt,
                                      std::nullopt, std::nullopt, onResult);
        resultFutures.push_back(std::move(resultFuture));
    }
    REQUIRE(connection.Sent().size() == kMaxPendingTrustAdminRequests);

    //  Waits for every one of the kMaxPendingTrustAdminRequests timeout workers
    //  to have scheduled its completion -- proving each one has already erased
    //  its map entry, notified, and let its own TrustAdminWaiterGuard destruct
    //  -- without running any of those scheduled completions.
    marshaller.WaitForPendingCountAtLeast(kMaxPendingTrustAdminRequests);
    for (auto& resultFuture : resultFutures) {
        CHECK(resultFuture.wait_for(std::chrono::seconds(0)) !=
              std::future_status::ready);
    }

    //  Every slot is already released, even though not one completion above
    //  has been delivered yet: admitted, not rejected. Each of the
    //  kMaxPendingTrustAdminRequests timed-out requests also already sent its
    //  own best-effort cancellation by this point, so the baseline is double
    //  the admitted count, not merely it.
    std::size_t baselineSentCount = 2 * kMaxPendingTrustAdminRequests;
    auto [onResult, resultFuture] = CaptureTrustAdminResult();
    session.SendTrustAdminRequest(TrustAdminOperation::kHelp, std::nullopt,
                                  std::nullopt, std::nullopt, onResult);
    REQUIRE(connection.Sent().size() == baselineSentCount + 1);

    marshaller.RunAllPending();
    for (auto& resultFuture : resultFutures) {
        REQUIRE(resultFuture.wait_for(std::chrono::seconds(0)) ==
                std::future_status::ready);
        CHECK(resultFuture.get().outcome == TrustAdminRequestOutcome::kTimedOut);
    }
    //  The newly admitted request resolves on its own, later timeout; not
    //  cross-delivered with any of the earlier batch.
    CHECK(resultFuture.wait_for(std::chrono::seconds(0)) !=
          std::future_status::ready);
}

TEST_CASE("AdapterIpcSession's destructor completes safely and resolves "
          "every request when kMaxPendingTrustAdminRequests is fully "
          "occupied") {
    FixedAdapterIpcPeerProofProvider peerProofProvider{
        {std::byte{9}, std::byte{8}, std::byte{7}}};
    AdapterIpcTarget target{
        .port = 58231,
        .proofToken = peerProofProvider.Token(),
        .hostProofKey = {std::byte{1}, std::byte{1}, std::byte{1}},
        .targetGeneration = 1,
    };
    FakeAdapterTaskMarshaller marshaller;
    FakeAdapterNativeCaptureRouter dispatcher;
    FakeAdapterCaptureHandoffQueue captureQueue;
    FakeAdapterPairingNotificationSink pairingNotificationSink;
    AdapterPlayContextState playContextState;
    FakeAdapterIpcConnection connection;
    auto session = std::make_unique<AdapterIpcSession>(
        SampleInstanceId(), SampleOwnerLifetimeId(), marshaller, dispatcher,
        captureQueue, pairingNotificationSink, playContextState, [] {},
        std::chrono::milliseconds(50));
    session->AttachConnection(connection);
    Authenticate(*session, connection, target);

    std::vector<std::future<TrustAdminRequestResult>> resultFutures;
    for (std::size_t i = 0; i < kMaxPendingTrustAdminRequests; ++i) {
        auto [onResult, resultFuture] = CaptureTrustAdminResult();
        session->SendTrustAdminRequest(TrustAdminOperation::kHelp, std::nullopt,
                                       std::nullopt, std::nullopt, onResult);
        resultFutures.push_back(std::move(resultFuture));
    }
    REQUIRE(connection.Sent().size() == kMaxPendingTrustAdminRequests);

    session.reset();
    marshaller.RunAllPending();

    for (auto& future : resultFutures) {
        REQUIRE(future.wait_for(std::chrono::seconds(0)) ==
                std::future_status::ready);
        CHECK(future.get().outcome == TrustAdminRequestOutcome::kTimedOut);
    }
}

TEST_CASE("AdapterIpcSession contains an exception onResult throws, once "
          "the game thread runs it, when rejecting a request at the "
          "outstanding-request bound") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);

    for (std::size_t i = 0; i < kMaxPendingTrustAdminRequests; ++i) {
        fixture.session.SendTrustAdminRequest(
            TrustAdminOperation::kHelp, std::nullopt, std::nullopt, std::nullopt,
            [](TrustAdminRequestResult) {});
    }
    REQUIRE(connection.Sent().size() == kMaxPendingTrustAdminRequests);

    fixture.session.SendTrustAdminRequest(
        TrustAdminOperation::kHelp, std::nullopt, std::nullopt, std::nullopt,
        [](TrustAdminRequestResult) {
            throw std::runtime_error("onResult failure");
        });
    //  The rejection callback only actually runs, and could only actually
    //  throw, once the game-thread dispatch is drained.
    REQUIRE_NOTHROW(fixture.marshaller.RunAllPending());
}

TEST_CASE("AdapterIpcSession::HandleClosing resolves every outstanding "
          "request safely when kMaxPendingTrustAdminRequests is fully "
          "occupied") {
    SessionFixture fixture;
    FakeAdapterIpcConnection connection;
    fixture.session.AttachConnection(connection);
    Authenticate(fixture.session, connection, fixture.target);

    std::vector<std::future<TrustAdminRequestResult>> resultFutures;
    for (std::size_t i = 0; i < kMaxPendingTrustAdminRequests; ++i) {
        auto [onResult, resultFuture] = CaptureTrustAdminResult();
        fixture.session.SendTrustAdminRequest(TrustAdminOperation::kHelp,
                                              std::nullopt, std::nullopt,
                                              std::nullopt, onResult);
        resultFutures.push_back(std::move(resultFuture));
    }
    REQUIRE(connection.Sent().size() == kMaxPendingTrustAdminRequests);

    fixture.session.HandleClosing();
    fixture.marshaller.RunAllPending();

    for (auto& future : resultFutures) {
        REQUIRE(future.wait_for(std::chrono::seconds(0)) ==
                std::future_status::ready);
        CHECK(future.get().outcome == TrustAdminRequestOutcome::kTimedOut);
    }
}
