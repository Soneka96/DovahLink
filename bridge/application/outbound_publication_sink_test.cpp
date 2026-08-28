#include "application/outbound_publication_sink.hpp"

#include "application/application_test_support.hpp"
#include "protocol/envelope.hpp"
#include "protocol/state_event_payload.hpp"
#include "security/constants.hpp"
#include "test_support/source_text_test_support.hpp"

#include <catch2/catch_test_macros.hpp>
#include <gmock/gmock.h>

#include <boost/json/object.hpp>
#include <boost/json/parse.hpp>

#include <chrono>
#include <cstdint>
#include <fstream>
#include <functional>
#include <iterator>
#include <memory>
#include <mutex>
#include <optional>
#include <semaphore>
#include <stdexcept>
#include <string>
#include <string_view>
#include <thread>
#include <utility>
#include <vector>

using dovahlink::application::BoundedOutboundQueue;
using dovahlink::application::DisconnectReason;
using dovahlink::application::test_support::MockPublicationDiagnostics;
using dovahlink::protocol::Envelope;
using testing::NiceMock;
using testing::StrictMock;

namespace {

///  Allows a test focused on another diagnostic signal to ignore the
///  enqueue-latency observation emitted for every accepted submission.
void AllowEnqueueLatency(MockPublicationDiagnostics& diagnostics) {
    EXPECT_CALL(diagnostics,
                RecordEnqueueLatency(testing::Ge(
                    std::chrono::steady_clock::duration::zero())))
        .Times(testing::AnyNumber());
}

///  Reads the outbound queue source for structural lifetime and storage
///  assertions that do not expose private state to production.
std::string ReadOutboundPublicationSource() {
    std::ifstream source(DOVAHLINK_OUTBOUND_PUBLICATION_SOURCE_FILE);
    REQUIRE(source.good());
    return {std::istreambuf_iterator<char>(source),
            std::istreambuf_iterator<char>()};
}

///  Controllable, thread-safe fake `ISocket` that never completes a `Send`
///  on its own -- the test drives completion explicitly, matching
///  `ai/context/skse/testing.md`'s "controllable thread-safe fake" guidance
///  for behavior where timing and backpressure are what is under test.
class FakeOutboundSocket final : public dovahlink::transport::ISocket {
  public:
    ///  Records the shutdown request.
    void Shutdown() noexcept override {
        std::function<void()> reentrantAction;
        {
            std::lock_guard<std::mutex> lock(mutex_);
            shutdownCalled_ = true;
            reentrantAction = std::move(onShutdown_);
        }
        if (reentrantAction) {
            reentrantAction();
        }
    }

    ///  Records the shutdown request; the notification text itself is not
    ///  exercised by these tests.
    void ShutdownWithNotification(std::string) noexcept override {
        std::lock_guard<std::mutex> lock(mutex_);
        shutdownCalled_ = true;
    }

    ///  Records the message and retains its completion for the test to
    ///  trigger explicitly.
    void Send(std::string message,
              std::function<void(bool)> onComplete) noexcept override {
        std::function<void(bool)> synchronousCompletion;
        std::function<void()> reentrantAction;
        bool synchronousResult = false;
        {
            std::lock_guard<std::mutex> lock(mutex_);
            sent_.push_back(std::move(message));
            reentrantAction = std::move(onSend_);
            if (completeSynchronously_) {
                synchronousCompletion = std::move(onComplete);
                synchronousResult = synchronousResult_;
            } else {
                pendingCompletion_ = std::move(onComplete);
            }
        }
        if (reentrantAction) {
            reentrantAction();
        }
        if (synchronousCompletion) {
            synchronousCompletion(synchronousResult);
        }
    }

    ///  Runs one action after `Send` releases this fake's mutex, allowing a
    ///  test to re-enter the queue and prove the queue mutex is not held across
    ///  the external socket call.
    void SetOnSend(std::function<void()> action) {
        std::lock_guard<std::mutex> lock(mutex_);
        onSend_ = std::move(action);
    }

    ///  Runs one action after `Shutdown` releases this fake's mutex, allowing
    ///  a test to prove the queue does not hold its mutex across shutdown.
    void SetOnShutdown(std::function<void()> action) {
        std::lock_guard<std::mutex> lock(mutex_);
        onShutdown_ = std::move(action);
    }

    ///  Makes the next `Send` completion run synchronously after the fake
    ///  releases its own mutex, exercising re-entrant queue behavior.
    void CompleteSendsSynchronously(bool ok) {
        std::lock_guard<std::mutex> lock(mutex_);
        completeSynchronously_ = true;
        synchronousResult_ = ok;
    }

    ///  Invokes the outstanding `Send` completion with the given outcome.
    void CompletePendingSend(bool ok) {
        std::function<void(bool)> completion;
        {
            std::lock_guard<std::mutex> lock(mutex_);
            completion = std::move(pendingCompletion_);
            pendingCompletion_ = nullptr;
        }
        REQUIRE(completion != nullptr);
        completion(ok);
    }

    ///  Returns every message handed to `Send`, in delivery order.
    [[nodiscard]] std::vector<std::string> SentMessages() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return sent_;
    }

    ///  Returns whether `Shutdown` or `ShutdownWithNotification` was called.
    [[nodiscard]] bool ShutdownCalled() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return shutdownCalled_;
    }

  private:
    ///  Synchronizes access to every field below.
    mutable std::mutex mutex_;
    ///  Messages handed to `Send`, in delivery order.
    std::vector<std::string> sent_;
    ///  Outstanding completion for the most recent `Send` call.
    std::function<void(bool)> pendingCompletion_;
    ///  Whether future sends complete immediately after releasing `mutex_`.
    bool completeSynchronously_{false};
    ///  Result supplied to synchronous completions.
    bool synchronousResult_{false};
    ///  One re-entrant action run after a send is recorded.
    std::function<void()> onSend_;
    ///  One re-entrant action run after shutdown is recorded.
    std::function<void()> onShutdown_;
    ///  Whether a shutdown was requested.
    bool shutdownCalled_{false};
};

///  Builds a representative `state_snapshot` envelope with a filler payload
///  field sized to control its encoded byte size, independent of the actual
///  `sessionId` this queue will overwrite.
Envelope BuildSnapshotEnvelope(std::size_t fillerBytes = 0) {
    boost::json::object payload;
    if (fillerBytes > 0) {
        payload["filler"] = std::string(fillerBytes, 'x');
    }
    return Envelope{
        .messageType = "state_snapshot",
        .messageId = "message-1",
        .sessionId = std::string("stale-session"),
        .correlationId = std::nullopt,
        .payload = std::move(payload),
    };
}

///  Builds a representative `state_event` envelope with a filler payload
///  field sized to control its encoded byte size.
Envelope BuildEventEnvelope(std::size_t fillerBytes = 0) {
    boost::json::object payload;
    if (fillerBytes > 0) {
        payload["filler"] = std::string(fillerBytes, 'x');
    }
    return Envelope{
        .messageType = "state_event",
        .messageId = "message-1",
        .sessionId = std::nullopt,
        .correlationId = std::nullopt,
        .payload = std::move(payload),
    };
}

///  Decodes a message this queue handed to a socket back into an envelope.
///  Uses a plain, unbounded JSON parse rather than `ParseBoundedJson`: that
///  parser enforces inbound-only limits (e.g. a 4 KiB maximum string length)
///  meant for untrusted client input, which a deliberately oversized Heavy
///  test payload can legitimately exceed on the outbound side being decoded
///  here.
Envelope DecodeSent(const std::string& text) {
    boost::json::value parsed = boost::json::parse(text);
    auto envelope = dovahlink::protocol::DecodeEnvelope(parsed);
    REQUIRE(envelope.has_value());
    return std::move(*envelope);
}

///  A filler size that reliably classifies as Heavy: comfortably above
///  `kHeavyPublicationThresholdBytes` even before envelope overhead.
constexpr std::size_t kHeavyFillerBytes =
    dovahlink::security::kHeavyPublicationThresholdBytes + 200;

///  Builds a representative `state_event` envelope with a real, decodable
///  revision -- unlike `BuildEventEnvelope`'s minimal filler-only payload,
///  needed for the recovery-barrier tests to round-trip through
///  `DecodeStateEventPayload`.
Envelope BuildRevisionedEventEnvelope(const std::string& stateArea,
                                      std::int64_t revision,
                                      std::string messageId = "message-1") {
    return Envelope{
        .messageType = "state_event",
        .messageId = std::move(messageId),
        .sessionId = std::nullopt,
        .correlationId = std::nullopt,
        .payload = dovahlink::protocol::EncodeStateEventPayload(
            dovahlink::protocol::StateEventPayload{
                .stateArea = stateArea,
                .baseRevision = revision - 1,
                .revision = revision,
                .occurredAt = "2026-01-01T00:00:00Z",
                .data = {},
            }),
    };
}

///  Builds a representative recovery-snapshot envelope with a distinct
///  `messageId` for delivery-order assertions.
Envelope BuildRecoveryEnvelope(std::string messageId) {
    return Envelope{
        .messageType = "state_snapshot",
        .messageId = std::move(messageId),
        .sessionId = std::nullopt,
        .correlationId = std::nullopt,
        .payload = {},
    };
}

///  Builds a representative control-category envelope (e.g. an
///  acknowledgement or error) with a distinct `messageId`.
Envelope BuildControlEnvelope(std::string messageId) {
    return Envelope{
        .messageType = "error",
        .messageId = std::move(messageId),
        .sessionId = std::nullopt,
        .correlationId = std::nullopt,
        .payload = {},
    };
}

} //  namespace

TEST_CASE("PublishSnapshot stamps the queue's own sessionId, overriding any "
          "value already on the envelope",
          "[application][outbound_publication_sink]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-42");

    queue.PublishSnapshot("area", BuildSnapshotEnvelope());

    auto sent = socket.SentMessages();
    REQUIRE(sent.size() == 1);
    Envelope decoded = DecodeSent(sent.front());
    REQUIRE(decoded.sessionId.has_value());
    CHECK(*decoded.sessionId == "session-42");
}

TEST_CASE("a synchronous failed Send completion cannot deadlock the queue",
          "[application][outbound_publication_sink][lifetime]") {
    FakeOutboundSocket socket;
    socket.CompleteSendsSynchronously(false);
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    CHECK_NOTHROW(queue.PublishSnapshot("area", BuildSnapshotEnvelope()));
    CHECK(socket.ShutdownCalled());
}

TEST_CASE("the queue does not hold its mutex across an external Send call",
          "[application][outbound_publication_sink][lifetime]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");
    socket.SetOnSend([&queue] {
        queue.PublishControl(BuildControlEnvelope("reentrant-control"));
    });

    CHECK_NOTHROW(queue.PublishSnapshot("area", BuildSnapshotEnvelope()));
    REQUIRE(socket.SentMessages().size() == 1);
    socket.CompletePendingSend(true);
    REQUIRE(socket.SentMessages().size() == 2);
    CHECK(DecodeSent(socket.SentMessages().at(1)).messageId ==
          "reentrant-control");
}

TEST_CASE("the queue does not hold its mutex across shutdown",
          "[application][outbound_publication_sink][lifetime]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");
    socket.SetOnShutdown([&queue] {
        queue.PublishControl(BuildControlEnvelope("reentrant-control"));
    });

    for (std::size_t i = 0; i < dovahlink::security::kNormalDataSlots; ++i) {
        queue.PublishEvent("area", BuildEventEnvelope());
    }

    CHECK_NOTHROW(queue.PublishEvent("area", BuildEventEnvelope()));
    CHECK(socket.ShutdownCalled());
}

TEST_CASE("synchronous successful Send completions drain the next entry",
          "[application][outbound_publication_sink][lifetime]") {
    FakeOutboundSocket socket;
    socket.CompleteSendsSynchronously(true);
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");
    bool submittedReentrantEntry = false;
    socket.SetOnSend([&] {
        if (!submittedReentrantEntry) {
            submittedReentrantEntry = true;
            queue.PublishEvent("area", BuildEventEnvelope());
        }
    });

    queue.PublishEvent("area", BuildEventEnvelope());

    CHECK(socket.SentMessages().size() == 2);
    CHECK_FALSE(socket.ShutdownCalled());
}

TEST_CASE("synchronous successful reserved-lane completion is delivered",
          "[application][outbound_publication_sink][lifetime]") {
    FakeOutboundSocket socket;
    socket.CompleteSendsSynchronously(true);
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    queue.PublishRecoverySnapshot("area", BuildRecoveryEnvelope("recovery"),
                                  1);

    REQUIRE(socket.SentMessages().size() == 1);
    CHECK(DecodeSent(socket.SentMessages().front()).messageId == "recovery");
    CHECK_FALSE(socket.ShutdownCalled());
}

TEST_CASE("a Send completion arriving after queue destruction is ignored",
          "[application][outbound_publication_sink][lifetime]") {
    FakeOutboundSocket socket;
    {
        NiceMock<MockPublicationDiagnostics> diagnostics;
        BoundedOutboundQueue queue(socket, diagnostics, "session-1");
        queue.PublishSnapshot("area", BuildSnapshotEnvelope());
    }

    CHECK_NOTHROW(socket.CompletePendingSend(false));
    CHECK(socket.SentMessages().size() == 1);
}

TEST_CASE("queue destruction waits for a completion already using queue state",
          "[application][outbound_publication_sink][lifetime]") {
    using namespace std::chrono_literals;

    const std::string source = ReadOutboundPublicationSource();
    CHECK(dovahlink::test_support::ContainsSourceText(
        source, "completionState_->changed.wait("));
    CHECK(dovahlink::test_support::ContainsSourceText(
        source, "completionState_->callbacksInFlight == 0"));

    std::binary_semaphore completionEntered{0};
    std::binary_semaphore releaseCompletion{0};
    std::binary_semaphore destructionFinished{0};
    NiceMock<MockPublicationDiagnostics> diagnostics;
    EXPECT_CALL(diagnostics, RecordDequeueLatency(testing::_))
        .WillOnce(testing::Invoke([&](std::chrono::steady_clock::duration) {
            completionEntered.release();
            releaseCompletion.acquire();
        }));

    FakeOutboundSocket socket;
    auto queue = std::make_unique<BoundedOutboundQueue>(
        socket, diagnostics, "session-1");
    queue->PublishSnapshot("area", BuildSnapshotEnvelope());

    std::thread completion([&socket] { socket.CompletePendingSend(true); });
    completionEntered.acquire();

    std::thread destruction([&queue, &destructionFinished] {
        queue.reset();
        destructionFinished.release();
    });

    releaseCompletion.release();

    completion.join();
    destruction.join();
    CHECK(destructionFinished.try_acquire());
}

TEST_CASE("a cancelled Send completion after Shutdown is handled once",
          "[application][outbound_publication_sink][lifetime]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    queue.PublishSnapshot("area", BuildSnapshotEnvelope());
    socket.Shutdown();

    CHECK_NOTHROW(socket.CompletePendingSend(false));
    CHECK(socket.ShutdownCalled());
}

TEST_CASE("a throwing diagnostic cannot escape a Send completion",
          "[application][outbound_publication_sink][lifetime]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    EXPECT_CALL(diagnostics, RecordDequeueLatency(testing::_))
        .WillOnce(testing::Invoke([](std::chrono::steady_clock::duration) {
            throw std::runtime_error("diagnostic failure");
        }));
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    queue.PublishSnapshot("area", BuildSnapshotEnvelope());

    CHECK_NOTHROW(socket.CompletePendingSend(true));
    CHECK(socket.ShutdownCalled());
}

TEST_CASE("a throwing enqueue diagnostic cannot suppress transport progress",
          "[application][outbound_publication_sink][diagnostics]") {
    SECTION("selected send") {
        FakeOutboundSocket socket;
        socket.CompleteSendsSynchronously(true);
        NiceMock<MockPublicationDiagnostics> diagnostics;
        EXPECT_CALL(diagnostics, RecordEnqueueLatency(testing::_))
            .WillOnce(testing::Invoke(
                [](std::chrono::steady_clock::duration) {
                    throw std::runtime_error("diagnostic failure");
                }));
        BoundedOutboundQueue queue(socket, diagnostics, "session-1");

        CHECK_NOTHROW(queue.PublishSnapshot("area", BuildSnapshotEnvelope()));
        CHECK(socket.SentMessages().size() == 1);
        CHECK_FALSE(socket.ShutdownCalled());
    }

    SECTION("required shutdown") {
        FakeOutboundSocket socket;
        NiceMock<MockPublicationDiagnostics> diagnostics;
        EXPECT_CALL(diagnostics, RecordEnqueueLatency(testing::_))
            .Times(dovahlink::security::kReservedControlRecoverySlots + 1)
            .WillRepeatedly(testing::Invoke(
                [](std::chrono::steady_clock::duration) {
                    throw std::runtime_error("diagnostic failure");
                }));
        BoundedOutboundQueue queue(socket, diagnostics, "session-1");

        for (std::size_t i = 0;
             i < dovahlink::security::kReservedControlRecoverySlots + 1; ++i) {
            queue.PublishControl(BuildControlEnvelope("control-" +
                                                      std::to_string(i)));
        }

        CHECK(socket.ShutdownCalled());
    }
}

TEST_CASE("queue source contract detaches late completions and drops oversized payloads",
          "[application][outbound_publication_sink][lifetime]") {
    const std::string source = ReadOutboundPublicationSource();

    CHECK(dovahlink::test_support::ContainsSourceText(
        source, "completionState_->owner = nullptr;"));
    CHECK(dovahlink::test_support::ContainsSourceText(
        source, "completionState_->changed.wait("));
    CHECK(dovahlink::test_support::ContainsSourceText(
        source, "std::optional<std::string> retained"));
    CHECK(dovahlink::test_support::ContainsSourceText(
        source, "bytes <= security::kOutboundQueueByteBudget"));
    CHECK(dovahlink::test_support::ContainsSourceText(
        source, "if (!it->second.encoded.has_value())"));
}

TEST_CASE("oversized dirty Snapshots retain bounded metadata and are replaced "
          "by a later admissible value without disconnecting",
          "[application][outbound_publication_sink][lifetime]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    std::string oversizedFiller(
        dovahlink::security::kOutboundQueueByteBudget + 1024, 'x');
    for (int attempt = 0; attempt < 4; ++attempt) {
        boost::json::object payload;
        payload["filler"] = oversizedFiller;
        queue.PublishSnapshot(
            "area",
            Envelope{.messageType = "state_snapshot",
                     .messageId = "oversized-" + std::to_string(attempt),
                     .sessionId = std::nullopt,
                     .correlationId = std::nullopt,
                     .payload = std::move(payload)});
    }

    CHECK(socket.SentMessages().empty());
    CHECK_FALSE(socket.ShutdownCalled());

    queue.PublishSnapshot("area", BuildSnapshotEnvelope());
    REQUIRE(socket.SentMessages().size() == 1);
    socket.CompletePendingSend(true);
    CHECK(socket.SentMessages().size() == 1);
}

TEST_CASE("an oversized dirty Snapshot does not displace an in-flight value "
          "and is replaced by a later bounded value",
          "[application][outbound_publication_sink][lifetime]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    queue.PublishSnapshot("blocker", BuildSnapshotEnvelope());
    queue.PublishSnapshot("area", BuildSnapshotEnvelope());

    boost::json::object oversizedPayload;
    oversizedPayload["filler"] = std::string(
        dovahlink::security::kOutboundQueueByteBudget + 1024, 'x');
    queue.PublishSnapshot(
        "area", Envelope{.messageType = "state_snapshot",
                         .messageId = "oversized",
                         .sessionId = std::nullopt,
                         .correlationId = std::nullopt,
                         .payload = std::move(oversizedPayload)});

    boost::json::object boundedPayload;
    boundedPayload["value"] = "latest";
    queue.PublishSnapshot(
        "area", Envelope{.messageType = "state_snapshot",
                         .messageId = "bounded",
                         .sessionId = std::nullopt,
                         .correlationId = std::nullopt,
                         .payload = std::move(boundedPayload)});

    socket.CompletePendingSend(true);
    REQUIRE(socket.SentMessages().size() == 2);
    CHECK(DecodeSent(socket.SentMessages().at(1)).messageId == "bounded");
    socket.CompletePendingSend(true);
    CHECK(socket.SentMessages().size() == 2);
    CHECK_FALSE(socket.ShutdownCalled());
}

TEST_CASE("PublishSnapshot for a new state area is delivered immediately "
          "when nothing is in flight",
          "[application][outbound_publication_sink]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    queue.PublishSnapshot("area", BuildSnapshotEnvelope());

    CHECK(socket.SentMessages().size() == 1);
}

TEST_CASE("PublishSnapshot replaces a pending, not-yet-in-flight entry for "
          "the same state area rather than growing the queue",
          "[application][outbound_publication_sink]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    //  area-a occupies the one in-flight slot; area-b is admitted but sits
    //  behind it, not yet handed to Send.
    queue.PublishSnapshot("area-a", BuildSnapshotEnvelope());
    boost::json::object firstBPayload;
    firstBPayload["value"] = "first";
    queue.PublishSnapshot("area-b",
                          Envelope{.messageType = "state_snapshot",
                                   .messageId = "b-1",
                                   .sessionId = std::nullopt,
                                   .correlationId = std::nullopt,
                                   .payload = firstBPayload});
    boost::json::object secondBPayload;
    secondBPayload["value"] = "second";
    queue.PublishSnapshot("area-b",
                          Envelope{.messageType = "state_snapshot",
                                   .messageId = "b-2",
                                   .sessionId = std::nullopt,
                                   .correlationId = std::nullopt,
                                   .payload = secondBPayload});

    //  Only area-a has been handed to Send so far -- area-b's replacement
    //  never grew the queue into two entries.
    REQUIRE(socket.SentMessages().size() == 1);

    socket.CompletePendingSend(true);

    auto sent = socket.SentMessages();
    REQUIRE(sent.size() == 2);
    Envelope areaB = DecodeSent(sent.at(1));
    CHECK(areaB.payload.at("value").as_string() == "second");
}

TEST_CASE("PublishSnapshot for an already in-flight state area is deferred "
          "and delivered with its latest value once that send completes",
          "[application][outbound_publication_sink]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    boost::json::object v1;
    v1["value"] = "v1";
    queue.PublishSnapshot("area",
                          Envelope{.messageType = "state_snapshot",
                                   .messageId = "m-1",
                                   .sessionId = std::nullopt,
                                   .correlationId = std::nullopt,
                                   .payload = v1});
    REQUIRE(socket.SentMessages().size() == 1);

    //  Submitted while "area"'s only entry is already in flight -- must not
    //  mutate the bytes Send() already received.
    boost::json::object v2;
    v2["value"] = "v2";
    queue.PublishSnapshot("area",
                          Envelope{.messageType = "state_snapshot",
                                   .messageId = "m-2",
                                   .sessionId = std::nullopt,
                                   .correlationId = std::nullopt,
                                   .payload = v2});
    boost::json::object v3;
    v3["value"] = "v3";
    queue.PublishSnapshot("area",
                          Envelope{.messageType = "state_snapshot",
                                   .messageId = "m-3",
                                   .sessionId = std::nullopt,
                                   .correlationId = std::nullopt,
                                   .payload = v3});

    REQUIRE(socket.SentMessages().size() == 1);
    Envelope firstSent = DecodeSent(socket.SentMessages().front());
    CHECK(firstSent.payload.at("value").as_string() == "v1");

    socket.CompletePendingSend(true);

    //  The latest deferred value (v3) is what gets delivered next -- v2 is
    //  never sent as its own message.
    auto sent = socket.SentMessages();
    REQUIRE(sent.size() == 2);
    Envelope secondSent = DecodeSent(sent.at(1));
    CHECK(secondSent.payload.at("value").as_string() == "v3");
}

TEST_CASE("PublishEvent entries for the same state area are delivered in "
          "order and never coalesced",
          "[application][outbound_publication_sink]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    boost::json::object first;
    first["value"] = "first";
    boost::json::object second;
    second["value"] = "second";
    queue.PublishEvent("area", Envelope{.messageType = "state_event",
                                        .messageId = "e-1",
                                        .sessionId = std::nullopt,
                                        .correlationId = std::nullopt,
                                        .payload = first});
    queue.PublishEvent("area", Envelope{.messageType = "state_event",
                                        .messageId = "e-2",
                                        .sessionId = std::nullopt,
                                        .correlationId = std::nullopt,
                                        .payload = second});

    REQUIRE(socket.SentMessages().size() == 1);
    socket.CompletePendingSend(true);
    REQUIRE(socket.SentMessages().size() == 2);

    CHECK(DecodeSent(socket.SentMessages().at(0)).payload.at("value").as_string() ==
          "first");
    CHECK(DecodeSent(socket.SentMessages().at(1)).payload.at("value").as_string() ==
          "second");
}

TEST_CASE("a reliable Event that overflows the Normal data lane disconnects "
          "the session without blocking the caller",
          "[application][outbound_publication_sink]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    for (std::size_t i = 0; i < dovahlink::security::kNormalDataSlots; ++i) {
        queue.PublishEvent("area", BuildEventEnvelope());
    }
    CHECK_FALSE(socket.ShutdownCalled());

    queue.PublishEvent("area", BuildEventEnvelope());

    CHECK(socket.ShutdownCalled());
}

TEST_CASE("a state area publication is classified Heavy and rejected once "
          "the four-slot Heavy lane is full, while Normal-lane capacity "
          "stays unaffected",
          "[application][outbound_publication_sink]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    for (std::size_t i = 0; i < dovahlink::security::kHeavyDataSlots; ++i) {
        boost::json::object payload;
        payload["filler"] = std::string(kHeavyFillerBytes, 'x');
        queue.PublishSnapshot(
            "heavy-area-" + std::to_string(i),
            Envelope{.messageType = "state_snapshot",
                     .messageId = "heavy-" + std::to_string(i),
                     .sessionId = std::nullopt,
                     .correlationId = std::nullopt,
                     .payload = std::move(payload)});
    }
    //  One entry has already been handed to Send (the first admitted one);
    //  the other three sit admitted but not yet in flight.
    REQUIRE(socket.SentMessages().size() == 1);

    //  A fifth distinct Heavy publication has no free Heavy slot and is
    //  deferred rather than admitted.
    boost::json::object overflowPayload;
    overflowPayload["filler"] = std::string(kHeavyFillerBytes, 'x');
    queue.PublishSnapshot(
        "heavy-area-overflow",
        Envelope{.messageType = "state_snapshot",
                 .messageId = "heavy-overflow",
                 .sessionId = std::nullopt,
                 .correlationId = std::nullopt,
                 .payload = std::move(overflowPayload)});
    CHECK(socket.SentMessages().size() == 1);

    //  A small Normal publication still admits immediately behind the
    //  already-admitted Heavy entries -- Heavy pressure does not block the
    //  Normal lane.
    queue.PublishSnapshot("normal-area", BuildSnapshotEnvelope());
    REQUIRE(socket.SentMessages().size() == 1);

    //  Draining every admitted entry eventually reaches the deferred
    //  overflow Heavy value once a Heavy slot frees. The overflow value is
    //  promoted (and appended behind normal-area) as soon as the first Heavy
    //  completion frees a slot, but FIFO order means it is not itself handed
    //  to Send until the four already-admitted entries ahead of it (the
    //  remaining three Heavy entries, then normal-area) have each completed
    //  in turn -- five completions, not four.
    for (int i = 0; i < 5; ++i) {
        socket.CompletePendingSend(true);
    }

    bool overflowDelivered = false;
    for (const auto& message : socket.SentMessages()) {
        if (DecodeSent(message).messageId == "heavy-overflow") {
            overflowDelivered = true;
        }
    }
    CHECK(overflowDelivered);
}

TEST_CASE("a reliable Event classified Heavy that overflows the four-slot "
          "Heavy lane disconnects the session",
          "[application][outbound_publication_sink]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    for (std::size_t i = 0; i < dovahlink::security::kHeavyDataSlots; ++i) {
        queue.PublishSnapshot("heavy-area-" + std::to_string(i),
                              BuildSnapshotEnvelope(kHeavyFillerBytes));
    }
    CHECK_FALSE(socket.ShutdownCalled());

    queue.PublishEvent("area", BuildEventEnvelope(kHeavyFillerBytes));

    CHECK(socket.ShutdownCalled());
}

TEST_CASE("the queue-wide byte budget rejects a Snapshot too large to fit "
          "even with message-count capacity free",
          "[application][outbound_publication_sink]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    std::string oversizedFiller(
        dovahlink::security::kOutboundQueueByteBudget + 1024, 'x');
    boost::json::object payload;
    payload["filler"] = oversizedFiller;
    queue.PublishSnapshot(
        "area", Envelope{.messageType = "state_snapshot",
                         .messageId = "m-1",
                         .sessionId = std::nullopt,
                         .correlationId = std::nullopt,
                         .payload = std::move(payload)});

    //  Rejected purely on bytes -- no message-count pressure exists here.
    CHECK(socket.SentMessages().empty());
}

TEST_CASE("publications submitted after an Event overflow disconnect are "
          "silently dropped without a second Shutdown or further delivery",
          "[application][outbound_publication_sink]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    for (std::size_t i = 0; i < dovahlink::security::kNormalDataSlots; ++i) {
        queue.PublishEvent("area", BuildEventEnvelope());
    }
    queue.PublishEvent("area", BuildEventEnvelope());
    REQUIRE(socket.ShutdownCalled());
    std::size_t sentBeforeExtra = socket.SentMessages().size();

    queue.PublishSnapshot("area", BuildSnapshotEnvelope());
    queue.PublishEvent("area", BuildEventEnvelope());

    CHECK(socket.SentMessages().size() == sentBeforeExtra);
}

TEST_CASE("a failed Send stops the queue and disconnects the session",
          "[application][outbound_publication_sink]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    queue.PublishSnapshot("area", BuildSnapshotEnvelope());
    REQUIRE(socket.SentMessages().size() == 1);
    CHECK_FALSE(socket.ShutdownCalled());

    socket.CompletePendingSend(false);

    CHECK(socket.ShutdownCalled());

    //  Further publications are dropped once the queue has stopped.
    queue.PublishSnapshot("another-area", BuildSnapshotEnvelope());
    CHECK(socket.SentMessages().size() == 1);
}

TEST_CASE("PublishEvent stamps the queue's own sessionId, overriding any "
          "value already on the envelope",
          "[application][outbound_publication_sink]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-7");

    queue.PublishEvent("area", BuildEventEnvelope());

    auto sent = socket.SentMessages();
    REQUIRE(sent.size() == 1);
    Envelope decoded = DecodeSent(sent.front());
    REQUIRE(decoded.sessionId.has_value());
    CHECK(*decoded.sessionId == "session-7");
}

TEST_CASE("the queue-wide byte budget disconnects a reliable Event too "
          "large to fit even with message-count capacity free",
          "[application][outbound_publication_sink]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    queue.PublishEvent("area", BuildEventEnvelope(
                                   dovahlink::security::kOutboundQueueByteBudget +
                                   1024));

    CHECK(socket.SentMessages().empty());
    CHECK(socket.ShutdownCalled());
}

TEST_CASE("Snapshot and Event publications share the same Normal-lane "
          "capacity, so Snapshots filling it still disconnect a later Event",
          "[application][outbound_publication_sink]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    for (std::size_t i = 0; i < dovahlink::security::kNormalDataSlots; ++i) {
        queue.PublishSnapshot("area-" + std::to_string(i),
                              BuildSnapshotEnvelope());
    }
    CHECK_FALSE(socket.ShutdownCalled());

    queue.PublishEvent("event-area", BuildEventEnvelope());

    CHECK(socket.ShutdownCalled());
}

TEST_CASE("replacing a not-yet-in-flight Snapshot's value moves it between "
          "the Normal and Heavy lanes when its classification changes",
          "[application][outbound_publication_sink]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    //  "blocker" occupies the one in-flight slot so "area"'s own entry is
    //  never in flight for this test.
    queue.PublishSnapshot("blocker", BuildSnapshotEnvelope());
    REQUIRE(socket.SentMessages().size() == 1);

    queue.PublishSnapshot("area", BuildSnapshotEnvelope()); //  Normal
    boost::json::object heavyPayload;
    heavyPayload["filler"] = std::string(kHeavyFillerBytes, 'x');
    queue.PublishSnapshot(
        "area", Envelope{.messageType = "state_snapshot",
                         .messageId = "area-heavy",
                         .sessionId = std::nullopt,
                         .correlationId = std::nullopt,
                         .payload = std::move(heavyPayload)}); //  now Heavy

    //  Fill the remaining three Heavy slots with fresh distinct areas -- if
    //  "area"'s transition had not actually moved it into the Heavy lane's
    //  own accounting, a fourth distinct Heavy publication here would still
    //  admit as this session's fourth (not fifth) Heavy occupant.
    for (std::size_t i = 0; i < dovahlink::security::kHeavyDataSlots - 1; ++i) {
        boost::json::object payload;
        payload["filler"] = std::string(kHeavyFillerBytes, 'x');
        queue.PublishSnapshot(
            "heavy-" + std::to_string(i),
            Envelope{.messageType = "state_snapshot",
                     .messageId = "heavy-" + std::to_string(i),
                     .sessionId = std::nullopt,
                     .correlationId = std::nullopt,
                     .payload = std::move(payload)});
    }

    boost::json::object overflowPayload;
    overflowPayload["filler"] = std::string(kHeavyFillerBytes, 'x');
    queue.PublishSnapshot(
        "heavy-overflow",
        Envelope{.messageType = "state_snapshot",
                 .messageId = "heavy-overflow-2",
                 .sessionId = std::nullopt,
                 .correlationId = std::nullopt,
                 .payload = std::move(overflowPayload)});

    //  Draining blocker plus the four Heavy occupants (transitioned "area",
    //  heavy-0, heavy-1, heavy-2) reaches the deferred overflow only on the
    //  fifth completion -- proving the transitioned "area" entry genuinely
    //  occupies one of the four Heavy slots, not a phantom fifth one.
    for (int i = 0; i < 4; ++i) {
        socket.CompletePendingSend(true);
    }
    bool overflowDeliveredEarly = false;
    for (const auto& message : socket.SentMessages()) {
        if (DecodeSent(message).messageId == "heavy-overflow-2") {
            overflowDeliveredEarly = true;
        }
    }
    CHECK_FALSE(overflowDeliveredEarly);

    socket.CompletePendingSend(true);
    bool overflowDelivered = false;
    for (const auto& message : socket.SentMessages()) {
        if (DecodeSent(message).messageId == "heavy-overflow-2") {
            overflowDelivered = true;
        }
    }
    CHECK(overflowDelivered);
}

TEST_CASE("a dirty Snapshot promoted for an area that still has its own "
          "admitted, not-yet-sent slot replaces that slot instead of "
          "duplicating it",
          "[application][outbound_publication_sink]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    queue.PublishSnapshot("blocker", BuildSnapshotEnvelope());
    REQUIRE(socket.SentMessages().size() == 1);

    for (std::size_t i = 0; i < dovahlink::security::kHeavyDataSlots; ++i) {
        boost::json::object payload;
        payload["filler"] = std::string(kHeavyFillerBytes, 'x');
        queue.PublishSnapshot(
            "heavy-" + std::to_string(i),
            Envelope{.messageType = "state_snapshot",
                     .messageId = "heavy-" + std::to_string(i),
                     .sessionId = std::nullopt,
                     .correlationId = std::nullopt,
                     .payload = std::move(payload)});
    }

    boost::json::object v1;
    v1["value"] = "v1";
    queue.PublishSnapshot("area", Envelope{.messageType = "state_snapshot",
                                           .messageId = "area-v1",
                                           .sessionId = std::nullopt,
                                           .correlationId = std::nullopt,
                                           .payload = std::move(v1)});

    //  "area"'s own slot is admitted (Normal) but not in flight. Replacing
    //  it with a Heavy value now fails because the Heavy lane is already
    //  full -- deferred as dirty while "area"'s original slot stays intact.
    boost::json::object v2;
    v2["filler"] = std::string(kHeavyFillerBytes, 'x');
    queue.PublishSnapshot("area", Envelope{.messageType = "state_snapshot",
                                           .messageId = "area-v2-heavy",
                                           .sessionId = std::nullopt,
                                           .correlationId = std::nullopt,
                                           .payload = std::move(v2)});

    //  Draining blocker then one Heavy entry frees exactly one Heavy slot --
    //  enough for the dirty "area" replacement to be promoted in place while
    //  "area"'s own original slot is still sitting in the queue, reusing it
    //  rather than appending a duplicate.
    socket.CompletePendingSend(true); //  blocker
    socket.CompletePendingSend(true); //  heavy-0
    for (int i = 0; i < 3; ++i) {
        socket.CompletePendingSend(true); //  heavy-1, heavy-2, area
    }

    Envelope areaDelivered;
    bool areaFound = false;
    for (const auto& message : socket.SentMessages()) {
        Envelope decoded = DecodeSent(message);
        if (decoded.messageId == "area-v1" ||
            decoded.messageId == "area-v2-heavy") {
            REQUIRE_FALSE(areaFound);
            areaFound = true;
            areaDelivered = decoded;
        }
    }
    REQUIRE(areaFound);
    CHECK(areaDelivered.messageId == "area-v2-heavy");
}

TEST_CASE("PublishRecoverySnapshot is delivered through the reserved lane "
          "ahead of already-queued data-lane traffic",
          "[application][outbound_publication_sink]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    queue.PublishSnapshot("blocker", BuildSnapshotEnvelope());
    REQUIRE(socket.SentMessages().size() == 1);

    //  Admitted into the data lane, queued behind the in-flight blocker.
    queue.PublishSnapshot("area-2", BuildSnapshotEnvelope());

    queue.PublishRecoverySnapshot(
        "recovery-area", BuildRecoveryEnvelope("recovery-1"), 5);

    //  Completing the in-flight blocker must send the reserved recovery
    //  snapshot next, even though area-2 was admitted into the data lane
    //  first -- the reserved lane has priority.
    socket.CompletePendingSend(true);

    auto sent = socket.SentMessages();
    REQUIRE(sent.size() == 2);
    CHECK(DecodeSent(sent.at(1)).messageId == "recovery-1");
}

TEST_CASE("PublishRecoverySnapshot stamps the queue's own sessionId, "
          "overriding any value already on the envelope",
          "[application][outbound_publication_sink]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-99");

    queue.PublishRecoverySnapshot("area", BuildRecoveryEnvelope("recovery-1"),
                                  1);

    auto sent = socket.SentMessages();
    REQUIRE(sent.size() == 1);
    Envelope decoded = DecodeSent(sent.front());
    REQUIRE(decoded.sessionId.has_value());
    CHECK(*decoded.sessionId == "session-99");
}

TEST_CASE("PublishRecoverySnapshot supersedes already-queued Events for its "
          "state area at or below its revision, leaving newer ones intact",
          "[application][outbound_publication_sink]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    queue.PublishSnapshot("blocker", BuildSnapshotEnvelope());
    REQUIRE(socket.SentMessages().size() == 1);

    //  All three queued behind the in-flight blocker, not yet sent.
    queue.PublishEvent("area",
                       BuildRevisionedEventEnvelope("area", 3, "event-3"));
    queue.PublishEvent("area",
                       BuildRevisionedEventEnvelope("area", 4, "event-4"));
    queue.PublishEvent("area",
                       BuildRevisionedEventEnvelope("area", 5, "event-5"));

    //  Barrier at revision 4: "at or below" supersedes both event-3 and
    //  event-4, but not event-5.
    queue.PublishRecoverySnapshot("area", BuildRecoveryEnvelope("recovery-1"),
                                  4);

    socket.CompletePendingSend(true); //  blocker
    socket.CompletePendingSend(true); //  recovery snapshot (reserved, priority)

    auto sent = socket.SentMessages();
    bool event3Delivered = false;
    bool event4Delivered = false;
    bool event5Delivered = false;
    for (const auto& message : sent) {
        auto id = DecodeSent(message).messageId;
        if (id == "event-3")
            event3Delivered = true;
        if (id == "event-4")
            event4Delivered = true;
        if (id == "event-5")
            event5Delivered = true;
    }
    CHECK_FALSE(event3Delivered);
    CHECK_FALSE(event4Delivered);
    CHECK(event5Delivered);
    REQUIRE(sent.size() == 3); //  blocker, recovery, event-5 only
}

TEST_CASE("an Event submitted after a recovery barrier is established, with "
          "revision at or below it, is discarded rather than queued",
          "[application][outbound_publication_sink]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    queue.PublishRecoverySnapshot("area", BuildRecoveryEnvelope("recovery-1"),
                                  10);
    REQUIRE(socket.SentMessages().size() == 1); //  the recovery snapshot itself

    queue.PublishEvent(
        "area", BuildRevisionedEventEnvelope("area", 10, "stale-event"));
    queue.PublishEvent(
        "area", BuildRevisionedEventEnvelope("area", 11, "fresh-event"));

    //  Only the fresh Event (revision 11 > barrier 10) is ever admitted;
    //  nothing new to send yet since the recovery snapshot is still in
    //  flight.
    CHECK(socket.SentMessages().size() == 1);

    socket.CompletePendingSend(true); //  recovery snapshot

    auto sent = socket.SentMessages();
    bool staleDelivered = false;
    bool freshDelivered = false;
    for (const auto& message : sent) {
        auto id = DecodeSent(message).messageId;
        if (id == "stale-event")
            staleDelivered = true;
        if (id == "fresh-event")
            freshDelivered = true;
    }
    CHECK_FALSE(staleDelivered);
    CHECK(freshDelivered);
}

TEST_CASE("the reserved control/recovery lane disconnects the session once "
          "its capacity is exhausted",
          "[application][outbound_publication_sink]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    for (std::size_t i = 0;
         i < dovahlink::security::kReservedControlRecoverySlots; ++i) {
        queue.PublishRecoverySnapshot(
            "area-" + std::to_string(i),
            BuildRecoveryEnvelope("recovery-" + std::to_string(i)),
            static_cast<std::int64_t>(i) + 1);
    }
    CHECK_FALSE(socket.ShutdownCalled());

    queue.PublishRecoverySnapshot("overflow-area",
                                  BuildRecoveryEnvelope("recovery-overflow"),
                                  999);

    CHECK(socket.ShutdownCalled());
}

TEST_CASE("an Event already handed to Send is still delivered even though a "
          "recovery barrier established afterward would otherwise supersede "
          "it",
          "[application][outbound_publication_sink]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    queue.PublishEvent("area",
                       BuildRevisionedEventEnvelope("area", 3, "event-3"));
    REQUIRE(socket.SentMessages().size() == 1); //  already in flight

    queue.PublishRecoverySnapshot("area", BuildRecoveryEnvelope("recovery-1"),
                                  4);

    //  event-3 cannot be recalled -- it was already handed to Send before
    //  the barrier existed.
    CHECK(DecodeSent(socket.SentMessages().front()).messageId == "event-3");
}

TEST_CASE("an Event whose payload cannot be decoded into a revision is "
          "still delivered under an active recovery barrier",
          "[application][outbound_publication_sink]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    queue.PublishRecoverySnapshot("area", BuildRecoveryEnvelope("recovery-1"),
                                  10);
    REQUIRE(socket.SentMessages().size() == 1);

    //  BuildEventEnvelope's minimal payload has no stateArea/baseRevision/
    //  revision/occurredAt fields, so DecodeStateEventPayload fails and this
    //  Event's revision is unknown -- never treated as superseded.
    queue.PublishEvent("area", BuildEventEnvelope());

    socket.CompletePendingSend(true); //  recovery snapshot

    CHECK(socket.SentMessages().size() == 2);
}

TEST_CASE("a failed Send while draining the reserved lane stops the queue "
          "and disconnects the session",
          "[application][outbound_publication_sink]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    queue.PublishRecoverySnapshot("area", BuildRecoveryEnvelope("recovery-1"),
                                  1);
    REQUIRE(socket.SentMessages().size() == 1);
    CHECK_FALSE(socket.ShutdownCalled());

    socket.CompletePendingSend(false);

    CHECK(socket.ShutdownCalled());
}

TEST_CASE("the reserved lane admits a recovery snapshot regardless of the "
          "data-lane byte budget",
          "[application][outbound_publication_sink]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    boost::json::object hugePayload;
    hugePayload["filler"] = std::string(
        dovahlink::security::kOutboundQueueByteBudget + 1024, 'x');
    queue.PublishRecoverySnapshot(
        "area",
        Envelope{.messageType = "state_snapshot",
                 .messageId = "recovery-huge",
                 .sessionId = std::nullopt,
                 .correlationId = std::nullopt,
                 .payload = std::move(hugePayload)},
        1);

    auto sent = socket.SentMessages();
    REQUIRE(sent.size() == 1);
    CHECK(DecodeSent(sent.front()).messageId == "recovery-huge");
}

TEST_CASE("recovery snapshot admission into the reserved lane is unaffected "
          "by the data lanes being full",
          "[application][outbound_publication_sink]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    for (std::size_t i = 0; i < dovahlink::security::kNormalDataSlots; ++i) {
        queue.PublishSnapshot("area-" + std::to_string(i),
                              BuildSnapshotEnvelope());
    }
    CHECK_FALSE(socket.ShutdownCalled());

    queue.PublishRecoverySnapshot("recovery-area",
                                  BuildRecoveryEnvelope("recovery-1"), 1);

    CHECK_FALSE(socket.ShutdownCalled());
    //  Completing the in-flight data entry must send the reserved recovery
    //  snapshot next, ahead of any remaining data-lane backlog.
    socket.CompletePendingSend(true);
    auto sent = socket.SentMessages();
    REQUIRE(sent.size() == 2);
    CHECK(DecodeSent(sent.at(1)).messageId == "recovery-1");
}

TEST_CASE("PublishControl stamps the queue's own sessionId, overriding any "
          "value already on the envelope",
          "[application][outbound_publication_sink]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-55");

    queue.PublishControl(Envelope{.messageType = "error",
                                  .messageId = "control-1",
                                  .sessionId = std::string("stale"),
                                  .correlationId = std::nullopt,
                                  .payload = {}});

    auto sent = socket.SentMessages();
    REQUIRE(sent.size() == 1);
    Envelope decoded = DecodeSent(sent.front());
    REQUIRE(decoded.sessionId.has_value());
    CHECK(*decoded.sessionId == "session-55");
}

TEST_CASE("PublishControl is delivered through the reserved lane ahead of "
          "already-queued data-lane traffic",
          "[application][outbound_publication_sink]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    queue.PublishSnapshot("blocker", BuildSnapshotEnvelope());
    REQUIRE(socket.SentMessages().size() == 1);

    queue.PublishSnapshot("area-2", BuildSnapshotEnvelope());
    queue.PublishControl(BuildControlEnvelope("control-1"));

    socket.CompletePendingSend(true); //  blocker

    auto sent = socket.SentMessages();
    REQUIRE(sent.size() == 2);
    CHECK(DecodeSent(sent.at(1)).messageId == "control-1");
}

TEST_CASE("PublishControl and PublishRecoverySnapshot share the same "
          "reserved-lane capacity",
          "[application][outbound_publication_sink]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    //  One recovery snapshot plus fifteen control messages exactly fill the
    //  16-slot reserved lane.
    queue.PublishRecoverySnapshot("area", BuildRecoveryEnvelope("recovery-1"),
                                  1);
    for (std::size_t i = 0;
         i < dovahlink::security::kReservedControlRecoverySlots - 1; ++i) {
        queue.PublishControl(BuildControlEnvelope("control-" + std::to_string(i)));
    }
    CHECK_FALSE(socket.ShutdownCalled());

    queue.PublishControl(BuildControlEnvelope("control-overflow"));

    CHECK(socket.ShutdownCalled());
}

TEST_CASE("PublishControl overflow of the reserved lane disconnects the "
          "session",
          "[application][outbound_publication_sink]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    for (std::size_t i = 0;
         i < dovahlink::security::kReservedControlRecoverySlots; ++i) {
        queue.PublishControl(BuildControlEnvelope("control-" + std::to_string(i)));
    }
    CHECK_FALSE(socket.ShutdownCalled());

    queue.PublishControl(BuildControlEnvelope("control-overflow"));

    CHECK(socket.ShutdownCalled());
}

TEST_CASE("publications submitted after a PublishControl overflow "
          "disconnect are silently dropped without further delivery",
          "[application][outbound_publication_sink]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    for (std::size_t i = 0;
         i < dovahlink::security::kReservedControlRecoverySlots; ++i) {
        queue.PublishControl(BuildControlEnvelope("control-" + std::to_string(i)));
    }
    queue.PublishControl(BuildControlEnvelope("control-overflow"));
    REQUIRE(socket.ShutdownCalled());
    std::size_t sentBeforeExtra = socket.SentMessages().size();

    queue.PublishControl(BuildControlEnvelope("late-control"));
    queue.PublishRecoverySnapshot("area", BuildRecoveryEnvelope("late-recovery"),
                                  1);

    CHECK(socket.SentMessages().size() == sentBeforeExtra);
}

TEST_CASE("multiple PublishControl messages are delivered through the "
          "reserved lane in FIFO order",
          "[application][outbound_publication_sink]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    queue.PublishControl(BuildControlEnvelope("first"));
    queue.PublishControl(BuildControlEnvelope("second"));
    queue.PublishControl(BuildControlEnvelope("third"));

    REQUIRE(socket.SentMessages().size() == 1);
    socket.CompletePendingSend(true);
    REQUIRE(socket.SentMessages().size() == 2);
    socket.CompletePendingSend(true);
    REQUIRE(socket.SentMessages().size() == 3);

    auto sent = socket.SentMessages();
    CHECK(DecodeSent(sent.at(0)).messageId == "first");
    CHECK(DecodeSent(sent.at(1)).messageId == "second");
    CHECK(DecodeSent(sent.at(2)).messageId == "third");
}

TEST_CASE("PublishControl and PublishRecoverySnapshot interleave in the "
          "reserved lane in submission order",
          "[application][outbound_publication_sink]") {
    FakeOutboundSocket socket;
    NiceMock<MockPublicationDiagnostics> diagnostics;
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    queue.PublishControl(BuildControlEnvelope("control-1"));
    queue.PublishRecoverySnapshot("area", BuildRecoveryEnvelope("recovery-1"),
                                  1);
    queue.PublishControl(BuildControlEnvelope("control-2"));

    REQUIRE(socket.SentMessages().size() == 1);
    socket.CompletePendingSend(true);
    REQUIRE(socket.SentMessages().size() == 2);
    socket.CompletePendingSend(true);
    REQUIRE(socket.SentMessages().size() == 3);

    auto sent = socket.SentMessages();
    CHECK(DecodeSent(sent.at(0)).messageId == "control-1");
    CHECK(DecodeSent(sent.at(1)).messageId == "recovery-1");
    CHECK(DecodeSent(sent.at(2)).messageId == "control-2");
}

TEST_CASE("BoundedOutboundQueue reports queue depth after admission and "
          "after delivery completion",
          "[application][outbound_publication_sink][diagnostics]") {
    FakeOutboundSocket socket;
    StrictMock<MockPublicationDiagnostics> diagnostics;
    AllowEnqueueLatency(diagnostics);
    {
        testing::InSequence sequence;
        EXPECT_CALL(diagnostics,
                    RecordQueueDepth(1, 0, 0, testing::Gt(std::size_t{0})));
        EXPECT_CALL(diagnostics, RecordQueueDepth(0, 0, 0, std::size_t{0}));
    }
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    queue.PublishSnapshot("area", BuildSnapshotEnvelope());
    socket.CompletePendingSend(true);
}

TEST_CASE("PublishSnapshot reports coalescing when it replaces a pending, "
          "not-yet-in-flight entry for the same state area",
          "[application][outbound_publication_sink][diagnostics]") {
    FakeOutboundSocket socket;
    StrictMock<MockPublicationDiagnostics> diagnostics;
    AllowEnqueueLatency(diagnostics);
    EXPECT_CALL(diagnostics,
                RecordQueueDepth(testing::_, testing::_, testing::_, testing::_))
        .Times(testing::AnyNumber());
    EXPECT_CALL(diagnostics, RecordCoalesced(std::string_view("area-b")));
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    //  "area-a" occupies the one in-flight slot; "area-b" is admitted new,
    //  then replaced in place while still not in flight.
    queue.PublishSnapshot("area-a", BuildSnapshotEnvelope());
    queue.PublishSnapshot("area-b", BuildSnapshotEnvelope());
    queue.PublishSnapshot("area-b", BuildSnapshotEnvelope());
}

TEST_CASE("PublishSnapshot reports a non-negative dequeue latency once its "
          "delivery completes",
          "[application][outbound_publication_sink][diagnostics]") {
    FakeOutboundSocket socket;
    StrictMock<MockPublicationDiagnostics> diagnostics;
    AllowEnqueueLatency(diagnostics);
    EXPECT_CALL(diagnostics,
                RecordQueueDepth(testing::_, testing::_, testing::_, testing::_))
        .Times(testing::AnyNumber());
    EXPECT_CALL(diagnostics,
                RecordDequeueLatency(
                    testing::Ge(std::chrono::steady_clock::duration::zero())));
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    queue.PublishSnapshot("area", BuildSnapshotEnvelope());
    socket.CompletePendingSend(true);
}

TEST_CASE("PublishRecoverySnapshot reports the number of already-queued "
          "Events it supersedes",
          "[application][outbound_publication_sink][diagnostics]") {
    FakeOutboundSocket socket;
    StrictMock<MockPublicationDiagnostics> diagnostics;
    AllowEnqueueLatency(diagnostics);
    EXPECT_CALL(diagnostics,
                RecordQueueDepth(testing::_, testing::_, testing::_, testing::_))
        .Times(testing::AnyNumber());
    EXPECT_CALL(diagnostics, RecordRecovery("area", 4, std::size_t{2}));
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    queue.PublishSnapshot("blocker", BuildSnapshotEnvelope());
    queue.PublishEvent("area",
                       BuildRevisionedEventEnvelope("area", 3, "event-3"));
    queue.PublishEvent("area",
                       BuildRevisionedEventEnvelope("area", 4, "event-4"));
    queue.PublishEvent("area",
                       BuildRevisionedEventEnvelope("area", 5, "event-5"));

    //  Barrier at revision 4 supersedes event-3 and event-4 (2 entries), but
    //  not event-5.
    queue.PublishRecoverySnapshot("area", BuildRecoveryEnvelope("recovery-1"),
                                  4);
}

TEST_CASE("PublishRecoverySnapshot reports a non-negative dequeue latency "
          "once its reserved-lane delivery completes",
          "[application][outbound_publication_sink][diagnostics]") {
    FakeOutboundSocket socket;
    StrictMock<MockPublicationDiagnostics> diagnostics;
    AllowEnqueueLatency(diagnostics);
    EXPECT_CALL(diagnostics,
                RecordQueueDepth(testing::_, testing::_, testing::_, testing::_))
        .Times(testing::AnyNumber());
    EXPECT_CALL(diagnostics, RecordRecovery("area", 1, std::size_t{0}));
    EXPECT_CALL(diagnostics,
                RecordDequeueLatency(
                    testing::Ge(std::chrono::steady_clock::duration::zero())));
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    queue.PublishRecoverySnapshot("area", BuildRecoveryEnvelope("recovery-1"),
                                  1);
    socket.CompletePendingSend(true);
}

TEST_CASE("the reserved-lane-full disconnect reports kReservedLaneFull",
          "[application][outbound_publication_sink][diagnostics]") {
    FakeOutboundSocket socket;
    StrictMock<MockPublicationDiagnostics> diagnostics;
    AllowEnqueueLatency(diagnostics);
    EXPECT_CALL(diagnostics,
                RecordQueueDepth(testing::_, testing::_, testing::_, testing::_))
        .Times(testing::AnyNumber());
    EXPECT_CALL(diagnostics,
                RecordRecovery(testing::_, testing::_, testing::_))
        .Times(testing::AnyNumber());
    EXPECT_CALL(diagnostics,
                RecordDisconnect(DisconnectReason::kReservedLaneFull));
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    for (std::size_t i = 0;
         i < dovahlink::security::kReservedControlRecoverySlots; ++i) {
        queue.PublishRecoverySnapshot(
            "area-" + std::to_string(i),
            BuildRecoveryEnvelope("recovery-" + std::to_string(i)),
            static_cast<std::int64_t>(i) + 1);
    }

    queue.PublishRecoverySnapshot("overflow-area",
                                  BuildRecoveryEnvelope("recovery-overflow"),
                                  999);
}

TEST_CASE("an Event overflow disconnect reports kEventOverflow",
          "[application][outbound_publication_sink][diagnostics]") {
    FakeOutboundSocket socket;
    StrictMock<MockPublicationDiagnostics> diagnostics;
    AllowEnqueueLatency(diagnostics);
    EXPECT_CALL(diagnostics,
                RecordQueueDepth(testing::_, testing::_, testing::_, testing::_))
        .Times(testing::AnyNumber());
    EXPECT_CALL(diagnostics, RecordDisconnect(DisconnectReason::kEventOverflow));
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    for (std::size_t i = 0; i < dovahlink::security::kNormalDataSlots; ++i) {
        queue.PublishEvent("area", BuildEventEnvelope());
    }

    queue.PublishEvent("area", BuildEventEnvelope());
}

TEST_CASE("a failed Send disconnect reports kSendFailed, after still "
          "reporting the finished entry's dequeue latency and queue depth",
          "[application][outbound_publication_sink][diagnostics]") {
    FakeOutboundSocket socket;
    StrictMock<MockPublicationDiagnostics> diagnostics;
    AllowEnqueueLatency(diagnostics);
    EXPECT_CALL(diagnostics,
                RecordQueueDepth(testing::_, testing::_, testing::_, testing::_))
        .Times(testing::AnyNumber());
    //  OnSendComplete accounts for the finished entry before checking `ok`,
    //  so the failed delivery still reports its latency like a successful
    //  one would.
    EXPECT_CALL(diagnostics,
                RecordDequeueLatency(
                    testing::Ge(std::chrono::steady_clock::duration::zero())));
    EXPECT_CALL(diagnostics, RecordDisconnect(DisconnectReason::kSendFailed));
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    queue.PublishSnapshot("area", BuildSnapshotEnvelope());
    socket.CompletePendingSend(false);
}

TEST_CASE("PublishEvent reports a non-negative dequeue latency once its "
          "delivery completes",
          "[application][outbound_publication_sink][diagnostics]") {
    FakeOutboundSocket socket;
    StrictMock<MockPublicationDiagnostics> diagnostics;
    AllowEnqueueLatency(diagnostics);
    EXPECT_CALL(diagnostics,
                RecordQueueDepth(testing::_, testing::_, testing::_, testing::_))
        .Times(testing::AnyNumber());
    EXPECT_CALL(diagnostics,
                RecordDequeueLatency(
                    testing::Ge(std::chrono::steady_clock::duration::zero())));
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    queue.PublishEvent("area", BuildEventEnvelope());
    socket.CompletePendingSend(true);
}

TEST_CASE("AdmitReservedOrDisconnectLocked reports reserved-lane queue "
          "depth once a reserved-lane entry is admitted",
          "[application][outbound_publication_sink][diagnostics]") {
    FakeOutboundSocket socket;
    StrictMock<MockPublicationDiagnostics> diagnostics;
    AllowEnqueueLatency(diagnostics);
    EXPECT_CALL(diagnostics, RecordQueueDepth(0, 0, 1, std::size_t{0}));
    EXPECT_CALL(diagnostics,
                RecordRecovery(testing::_, testing::_, testing::_));
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    queue.PublishRecoverySnapshot("area", BuildRecoveryEnvelope("recovery-1"),
                                  1);
}

TEST_CASE("a dirty Snapshot promoted into an existing slot reports "
          "coalescing, the same as an immediate in-place replacement",
          "[application][outbound_publication_sink][diagnostics]") {
    FakeOutboundSocket socket;
    StrictMock<MockPublicationDiagnostics> diagnostics;
    AllowEnqueueLatency(diagnostics);
    EXPECT_CALL(diagnostics,
                RecordQueueDepth(testing::_, testing::_, testing::_, testing::_))
        .Times(testing::AnyNumber());
    EXPECT_CALL(diagnostics, RecordCoalesced(std::string_view("area")));
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    queue.PublishSnapshot("blocker", BuildSnapshotEnvelope());
    REQUIRE(socket.SentMessages().size() == 1);

    for (std::size_t i = 0; i < dovahlink::security::kHeavyDataSlots; ++i) {
        boost::json::object payload;
        payload["filler"] = std::string(kHeavyFillerBytes, 'x');
        queue.PublishSnapshot(
            "heavy-" + std::to_string(i),
            Envelope{.messageType = "state_snapshot",
                     .messageId = "heavy-" + std::to_string(i),
                     .sessionId = std::nullopt,
                     .correlationId = std::nullopt,
                     .payload = std::move(payload)});
    }

    boost::json::object v1;
    v1["value"] = "v1";
    queue.PublishSnapshot("area", Envelope{.messageType = "state_snapshot",
                                           .messageId = "area-v1",
                                           .sessionId = std::nullopt,
                                           .correlationId = std::nullopt,
                                           .payload = std::move(v1)});

    //  "area"'s own slot is admitted (Normal) but not in flight; replacing
    //  it with a Heavy value fails immediately because the Heavy lane is
    //  already full, deferring it as dirty instead.
    boost::json::object v2;
    v2["filler"] = std::string(kHeavyFillerBytes, 'x');
    queue.PublishSnapshot("area", Envelope{.messageType = "state_snapshot",
                                           .messageId = "area-v2-heavy",
                                           .sessionId = std::nullopt,
                                           .correlationId = std::nullopt,
                                           .payload = std::move(v2)});

    //  Draining blocker then one Heavy entry frees exactly one Heavy slot,
    //  promoting the dirty "area" replacement into its still-pending
    //  original slot -- the coalescing report this test proves.
    socket.CompletePendingSend(true); //  blocker
    socket.CompletePendingSend(true); //  heavy-0
}

TEST_CASE("BoundedOutboundQueue reports enqueue latency for admission, "
          "replacement, and bounded deferral",
          "[application][outbound_publication_sink][diagnostics]") {
    FakeOutboundSocket socket;
    StrictMock<MockPublicationDiagnostics> diagnostics;
    std::vector<std::chrono::steady_clock::duration> latencies;
    EXPECT_CALL(diagnostics,
                RecordEnqueueLatency(testing::Ge(
                    std::chrono::steady_clock::duration::zero())))
        .Times(8)
        .WillRepeatedly(testing::Invoke(
            [&latencies](std::chrono::steady_clock::duration latency) {
                latencies.push_back(latency);
            }));
    EXPECT_CALL(diagnostics,
                RecordQueueDepth(testing::_, testing::_, testing::_, testing::_))
        .Times(testing::AnyNumber());
    EXPECT_CALL(diagnostics, RecordCoalesced(testing::_))
        .Times(testing::AnyNumber());
    BoundedOutboundQueue queue(socket, diagnostics, "session-1");

    queue.PublishSnapshot("blocker", BuildSnapshotEnvelope());
    queue.PublishSnapshot("area", BuildSnapshotEnvelope());
    queue.PublishSnapshot("area", BuildSnapshotEnvelope());
    for (std::size_t i = 0; i < dovahlink::security::kHeavyDataSlots; ++i) {
        queue.PublishSnapshot("heavy-" + std::to_string(i),
                              BuildSnapshotEnvelope(kHeavyFillerBytes));
    }

    //  The existing Normal slot cannot change class while all Heavy slots
    //  are occupied, so this submission completes as a bounded dirty-marker
    //  decision rather than waiting or disconnecting.
    queue.PublishSnapshot("area", BuildSnapshotEnvelope(kHeavyFillerBytes));

    REQUIRE(latencies.size() == 8);
    for (const auto latency : latencies) {
        CHECK(latency >= std::chrono::steady_clock::duration::zero());
    }
}

TEST_CASE("BoundedOutboundQueue reports enqueue latency for Event and "
          "reserved-lane overflow decisions",
          "[application][outbound_publication_sink][diagnostics]") {
    SECTION("Event overflow") {
        FakeOutboundSocket socket;
        NiceMock<MockPublicationDiagnostics> diagnostics;
        EXPECT_CALL(diagnostics,
                    RecordEnqueueLatency(testing::Ge(
                        std::chrono::steady_clock::duration::zero())))
            .Times(dovahlink::security::kNormalDataSlots + 1);
        EXPECT_CALL(diagnostics,
                    RecordDisconnect(DisconnectReason::kEventOverflow));
        BoundedOutboundQueue queue(socket, diagnostics, "session-1");

        for (std::size_t i = 0;
             i < dovahlink::security::kNormalDataSlots + 1; ++i) {
            queue.PublishEvent("area", BuildEventEnvelope());
        }
    }

    SECTION("reserved-lane overflow") {
        FakeOutboundSocket socket;
        NiceMock<MockPublicationDiagnostics> diagnostics;
        EXPECT_CALL(diagnostics,
                    RecordEnqueueLatency(testing::Ge(
                        std::chrono::steady_clock::duration::zero())))
            .Times(dovahlink::security::kReservedControlRecoverySlots + 1);
        EXPECT_CALL(diagnostics,
                    RecordDisconnect(DisconnectReason::kReservedLaneFull));
        BoundedOutboundQueue queue(socket, diagnostics, "session-1");

        for (std::size_t i = 0;
             i < dovahlink::security::kReservedControlRecoverySlots + 1; ++i) {
            queue.PublishControl(BuildControlEnvelope("control-" +
                                                      std::to_string(i)));
        }
    }
}
