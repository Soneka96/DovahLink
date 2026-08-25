#include "application/message_dispatcher.hpp"

#include "application/application_test_support.hpp"
#include "application/handshake_handler.hpp"
#include "application/trust_mutation_coordinator.hpp"
#include "protocol/messages.hpp"
#include "security/hex.hpp"
#include "security/limits.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/parse.hpp>

#include <chrono>
#include <cstddef>
#include <optional>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

using dovahlink::application::ActivePlayContext;
using dovahlink::application::ConnectionTimeoutTracker;
using dovahlink::application::DispatchResult;
using dovahlink::application::PairingNotificationSink;
using dovahlink::application::ProcessInboundMessage;
using dovahlink::application::ReplayGuard;
using dovahlink::application::SessionAuthMethod;
using dovahlink::application::SessionManager;
using dovahlink::application::SessionTrustTier;
using dovahlink::application::TrustMutationCoordinator;
using dovahlink::application::test_support::BuildEnvelope;
using dovahlink::application::test_support::BuildPairingAckEnvelope;
using dovahlink::application::test_support::BuildPairingCancelEnvelope;
using dovahlink::application::test_support::BuildPairingConfirmEnvelope;
using dovahlink::application::test_support::BuildPairingRenotifyEnvelope;
using dovahlink::application::test_support::BuildPairingRequestEnvelope;
using dovahlink::application::test_support::BuildRenameRequestEnvelope;
using dovahlink::security::DecodeHex;
using dovahlink::security::InboundMessageRateLimiter;
using dovahlink::security::ITrustStorePersistence;
using dovahlink::security::PairingSession;
using dovahlink::security::TrustStore;
using dovahlink::security::TrustStoreSnapshot;
using dovahlink::security::ViolationTracker;

namespace {

///  Clock type used for deterministic dispatcher timeout assertions.
using SteadyClock = std::chrono::steady_clock;

constexpr dovahlink::application::ConnectionId kConnection = 1;
constexpr const char* kSessionId = "session-1";
constexpr const char* kClientId = "client-1";

///  `ITrustStorePersistence` double that always loads an empty snapshot -- these
///  tests build their own trust records directly through `TrustStore`'s API
///  rather than seeding a snapshot.
class EmptyPersistence : public ITrustStorePersistence {
  public:
    ///  Always reports a valid, empty snapshot.
    std::optional<TrustStoreSnapshot> Load() override {
        return TrustStoreSnapshot{};
    }

    ///  Always succeeds without recording anything.
    bool Save(const TrustStoreSnapshot&) override { return true; }
};

///  `PairingNotificationSink` double that records every code it is given.
class RecordingPairingNotificationSink : public PairingNotificationSink {
  public:
    ///  Appends `sixDigitCode` to `codes`.
    void NotifyPairingCodeAvailable(std::string_view sixDigitCode) override {
        codes.emplace_back(sixDigitCode);
    }

    ///  Appends `sixDigitCode` to `incorrectCodes`.
    void NotifyPairingCodeIncorrect(std::string_view sixDigitCode) override {
        incorrectCodes.emplace_back(sixDigitCode);
    }

    ///  Increments `attemptsExhaustedCount`.
    void NotifyPairingAttemptsExhausted() override { ++attemptsExhaustedCount; }

    ///  Every code this sink has been given, in order.
    std::vector<std::string> codes;

    ///  Every code this sink was asked to redisplay after a wrong attempt, in
    ///  order.
    std::vector<std::string> incorrectCodes;

    ///  Number of times the wrong-attempt hard limit was reached.
    int attemptsExhaustedCount = 0;
};

///  Bundles an authenticated dispatcher session and its production
///  collaborators.
struct Fixture {
    ///  Counts every completed inbound message before decoding.
    std::size_t receivedMessageCount = 0;
    ///  Tracks the session used by the dispatcher.
    SessionManager sessions;
    ///  Owns the authenticated session for the fixture lifetime.
    std::optional<SessionManager::Lease> sessionLease;
    ///  Rejects duplicate inbound message IDs.
    ReplayGuard replayGuard;
    ///  Tracks protocol violations for the session.
    ViolationTracker violations;
    ///  Limits inbound message rates for the session.
    InboundMessageRateLimiter rateLimiter;
    ///  Tracks handshake and authenticated idle deadlines.
    ConnectionTimeoutTracker timeout{SteadyClock::now()};
    ///  Source of the acquired play context; empty (kNoContext) until a test
    ///  begins one.
    ActivePlayContext activePlayContext;
    ///  Backing store for `trustStore`, empty at fixture construction.
    EmptyPersistence persistence;
    ///  Persistent trust store, for pairing_ack's idempotent-retry check and
    ///  commit.
    TrustStore trustStore = TrustStore::Load(persistence);
    ///  Pairing challenge/pending-credential state machine.
    PairingSession pairingSession;
    ///  Coordinates pairing finalization with administrative trust mutations.
    TrustMutationCoordinator mutationCoordinator{trustStore, pairingSession};
    ///  Records pairing codes displayed to the user.
    RecordingPairingNotificationSink pairingNotificationSink;
    ///  This bridge process's identity, stamped onto every response envelope.
    std::optional<std::string> bridgeInstanceId = "bridge-1";

    ///  Creates the fixture with its test session already authenticated at the
    ///  given trust tier and auth method. Both are required, not defaulted: most
    ///  call sites want `SessionTrustTier::kFull,
    ///  SessionAuthMethod::kTrustedDeviceCredential` (a real trusted-device
    ///  session is always kFull); a handful modeling a restricted pairing session
    ///  want `SessionTrustTier::kRestricted, SessionAuthMethod::kUnpaired` (the
    ///  only combination `HandleHello` ever actually produces for a restricted
    ///  session) -- a default here would let a call site silently create the same
    ///  impossible kRestricted/kTrustedDeviceCredential combination this
    ///  constructor used to hardcode (`ai/context/common.md`'s "Domain modeling").
    explicit Fixture(SessionTrustTier trustTier, SessionAuthMethod authMethod)
        : sessionLease(sessions.TryCreateSession(
              kConnection, kSessionId, kClientId, trustTier, authMethod)) {
        REQUIRE(sessionLease.has_value());
    }

    ///  Processes a raw message using the fixture's authenticated session.
    DispatchResult
    Process(const std::string& rawMessage,
            SteadyClock::time_point steadyNow = SteadyClock::now()) {
        return ProcessInboundMessage(
            rawMessage, receivedMessageCount, kSessionId, kConnection, sessions,
            replayGuard, violations, rateLimiter, timeout, activePlayContext,
            pairingSession, trustStore, mutationCoordinator,
            pairingNotificationSink, bridgeInstanceId, steadyNow);
    }
};

///  Builds a valid ping envelope for the fixture's authenticated session.
std::string PingMessage(std::string messageId = "message-ping-1") {
    return dovahlink::protocol::EncodeEnvelope(
        BuildEnvelope("ping", std::move(messageId), std::string(kSessionId)));
}

///  Builds a subscribe envelope requesting an example state area.
std::string SubscribeMessage(std::string messageId = "message-sub-1") {
    auto payload = boost::json::parse(
                       R"({"stateAreas": ["example_area"]})")
                       .get_object();
    return dovahlink::protocol::EncodeEnvelope(
        BuildEnvelope("subscribe", std::move(messageId),
                      std::string(kSessionId), std::nullopt, std::move(payload)));
}

///  Builds a snapshot_request envelope for an example state area.
std::string
SnapshotRequestMessage(std::string messageId = "message-snap-req-1") {
    auto payload = boost::json::parse(
                       R"({"stateArea": "example_area"})")
                       .get_object();
    return dovahlink::protocol::EncodeEnvelope(BuildEnvelope(
        "snapshot_request", std::move(messageId), std::string(kSessionId),
        std::nullopt, std::move(payload)));
}

///  Builds a pairing_request envelope (no payload).
std::string
PairingRequestMessage(std::string messageId = "message-pairing-request-1") {
    return dovahlink::protocol::EncodeEnvelope(
        BuildPairingRequestEnvelope(std::move(messageId)));
}

///  Builds a pairing_confirm envelope with the given code.
std::string
PairingConfirmMessage(const std::string& code,
                      std::string messageId = "message-pairing-confirm-1") {
    return dovahlink::protocol::EncodeEnvelope(BuildPairingConfirmEnvelope(
        code, std::nullopt, std::move(messageId)));
}

///  Builds a pairing_ack envelope echoing the given hex-encoded credential.
std::string PairingAckMessage(const std::string& hexCredential,
                              std::string messageId = "message-pairing-ack-1") {
    return dovahlink::protocol::EncodeEnvelope(
        BuildPairingAckEnvelope(hexCredential, std::move(messageId)));
}

///  Builds a pairing_renotify envelope (no payload).
std::string
PairingRenotifyMessage(std::string messageId = "message-pairing-renotify-1") {
    return dovahlink::protocol::EncodeEnvelope(
        BuildPairingRenotifyEnvelope(std::move(messageId)));
}

///  Builds a pairing_cancel envelope (no payload).
std::string
PairingCancelMessage(std::string messageId = "message-pairing-cancel-1") {
    return dovahlink::protocol::EncodeEnvelope(
        BuildPairingCancelEnvelope(std::move(messageId)));
}

///  Builds a `rename_request` envelope with the requested display name.
std::string RenameRequestMessage(const std::string& displayName,
                                 std::string messageId = "message-rename-1") {
    return dovahlink::protocol::EncodeEnvelope(
        BuildRenameRequestEnvelope(displayName, std::move(messageId)));
}

} //  namespace

TEST_CASE(
    "ProcessInboundMessage answers ping with pong and resets the idle timeout",
    "[application][message_dispatcher]") {
    Fixture fixture(SessionTrustTier::kFull,
                    SessionAuthMethod::kTrustedDeviceCredential);
    auto start = SteadyClock::now();
    fixture.timeout = ConnectionTimeoutTracker(start);
    fixture.timeout.MarkAuthenticated(start);

    auto result =
        fixture.Process(PingMessage(), start + std::chrono::seconds(30));

    CHECK_FALSE(result.closeConnection);
    REQUIRE(result.responses.size() == 1);
    CHECK(result.responses[0].messageType == "pong");
    REQUIRE(result.responses[0].correlationId.has_value());
    CHECK(*result.responses[0].correlationId == "message-ping-1");

    //  The 60s idle deadline was reset to start+30s+60s=start+90s by
    //  RecordActivity. Checking at start+65s: after the original deadline
    //  (start+60s from MarkAuthenticated, which would already have expired
    //  without RecordActivity) but still before the new one, so this can
    //  only pass if RecordActivity actually moved the deadline.
    CHECK_FALSE(fixture.timeout.IsTimedOut(start + std::chrono::seconds(65)));
}

TEST_CASE("ProcessInboundMessage routes subscribe to a subscription_ack that "
          "rejects every "
          "requested area",
          "[application][message_dispatcher]") {
    //  No state area is currently registered (protocol/schema/README.md's
    //  "Registered state areas"), so subscribe never produces a snapshot.
    Fixture fixture(SessionTrustTier::kFull,
                    SessionAuthMethod::kTrustedDeviceCredential);

    auto result = fixture.Process(SubscribeMessage());

    CHECK_FALSE(result.closeConnection);
    REQUIRE(result.responses.size() == 1);
    CHECK(result.responses[0].messageType == "subscription_ack");
    CHECK_FALSE(result.responses[0].clientId.has_value());
    auto ack = dovahlink::protocol::DecodeSubscriptionAckPayload(
        result.responses[0].payload);
    REQUIRE(ack.has_value());
    CHECK(ack->acceptedStateAreas.empty());
    CHECK(ack->rejectedStateAreas == std::vector<std::string>{"example_area"});
}

TEST_CASE("ProcessInboundMessage stamps bridgeInstanceId and playContextId "
          "onto a pong, never clientId",
          "[application][message_dispatcher]") {
    Fixture fixture(SessionTrustTier::kFull,
                    SessionAuthMethod::kTrustedDeviceCredential);
    fixture.activePlayContext.Begin("context-1");

    auto result = fixture.Process(PingMessage());

    REQUIRE(result.responses.size() == 1);
    REQUIRE(result.responses[0].bridgeInstanceId.has_value());
    CHECK(*result.responses[0].bridgeInstanceId == "bridge-1");
    REQUIRE(result.responses[0].playContextId.has_value());
    CHECK(*result.responses[0].playContextId == "context-1");
    //  The client identity bound to a session is session-owned state
    //  (SessionManager::ClientIdForConnection) once a session exists, not a
    //  value repeated on every response.
    CHECK_FALSE(result.responses[0].clientId.has_value());
}

TEST_CASE("ProcessInboundMessage stamps a null playContextId onto a pong when "
          "no context is active",
          "[application][message_dispatcher]") {
    Fixture fixture(SessionTrustTier::kFull,
                    SessionAuthMethod::kTrustedDeviceCredential);

    auto result = fixture.Process(PingMessage());

    REQUIRE(result.responses.size() == 1);
    REQUIRE(result.responses[0].bridgeInstanceId.has_value());
    CHECK(*result.responses[0].bridgeInstanceId == "bridge-1");
    CHECK_FALSE(result.responses[0].playContextId.has_value());
}

TEST_CASE("ProcessInboundMessage stamps bridgeInstanceId and playContextId "
          "onto an early "
          "dispatcher-level rejection too, never clientId",
          "[application][message_dispatcher]") {
    //  Reject()'s connection-hygiene errors (malformed shape, stale session,
    //  replay, rate limit) are still responses on an authenticated
    //  connection: protocol/schema/README.md requires bridgeInstanceId and
    //  playContextId on every Bridge-originated message once authenticated,
    //  with no carve-out for these. Matches protocol/fixtures/errors/
    //  error-rate-limited.json, error-replayed-message.json, and
    //  error-stale-session.json, which all carry a real bridgeInstanceId.
    Fixture fixture(SessionTrustTier::kFull,
                    SessionAuthMethod::kTrustedDeviceCredential);
    fixture.activePlayContext.Begin("context-1");

    auto result = fixture.Process("not json at all {{{");

    REQUIRE(result.responses.size() == 1);
    REQUIRE(result.responses[0].bridgeInstanceId.has_value());
    CHECK(*result.responses[0].bridgeInstanceId == "bridge-1");
    REQUIRE(result.responses[0].playContextId.has_value());
    CHECK(*result.responses[0].playContextId == "context-1");
    CHECK_FALSE(result.responses[0].clientId.has_value());
}

TEST_CASE("ProcessInboundMessage stamps a null playContextId onto an early "
          "dispatcher-level "
          "rejection when no context is active",
          "[application][message_dispatcher]") {
    Fixture fixture(SessionTrustTier::kFull,
                    SessionAuthMethod::kTrustedDeviceCredential);

    auto result = fixture.Process("not json at all {{{");

    REQUIRE(result.responses.size() == 1);
    REQUIRE(result.responses[0].bridgeInstanceId.has_value());
    CHECK(*result.responses[0].bridgeInstanceId == "bridge-1");
    CHECK_FALSE(result.responses[0].playContextId.has_value());
}

TEST_CASE("ProcessInboundMessage stamps a null bridgeInstanceId onto an early "
          "dispatcher-level "
          "rejection when generation failed at startup",
          "[application][message_dispatcher]") {
    Fixture fixture(SessionTrustTier::kFull,
                    SessionAuthMethod::kTrustedDeviceCredential);
    fixture.bridgeInstanceId = std::nullopt;

    auto result = fixture.Process("not json at all {{{");

    REQUIRE(result.responses.size() == 1);
    CHECK_FALSE(result.responses[0].bridgeInstanceId.has_value());
}

TEST_CASE("ProcessInboundMessage stamps a null bridgeInstanceId onto a "
          "response when generation "
          "failed at startup",
          "[application][message_dispatcher]") {
    Fixture fixture(SessionTrustTier::kFull,
                    SessionAuthMethod::kTrustedDeviceCredential);
    fixture.bridgeInstanceId = std::nullopt;

    auto result = fixture.Process(PingMessage());

    REQUIRE(result.responses.size() == 1);
    CHECK_FALSE(result.responses[0].bridgeInstanceId.has_value());
}

TEST_CASE("ProcessInboundMessage stamps bridgeInstanceId onto a "
          "HandleClientCapabilities error "
          "response too, not just ping/subscribe/snapshot_request",
          "[application][message_dispatcher]") {
    Fixture fixture(SessionTrustTier::kFull,
                    SessionAuthMethod::kTrustedDeviceCredential);
    std::string message =
        R"({"messageType": "capabilities", "messageId": "message-cap-1", "sessionId": ")" +
        std::string(kSessionId) +
        R"(", "correlationId": null, "payload": {"capabilities": [{"id": "state.inventory", "version": 1}]}, )"
        R"("bridgeInstanceId": null, "playContextId": null, "clientId": null})";

    auto result = fixture.Process(message);

    REQUIRE(result.responses.size() == 1);
    auto error =
        dovahlink::protocol::DecodeErrorPayload(result.responses[0].payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "unsupported_capability");
    REQUIRE(result.responses[0].bridgeInstanceId.has_value());
    CHECK(*result.responses[0].bridgeInstanceId == "bridge-1");
    CHECK_FALSE(result.responses[0].clientId.has_value());
}

TEST_CASE("ProcessInboundMessage routes snapshot_request to an "
          "unsupported_capability error",
          "[application][message_dispatcher]") {
    //  No state area is currently registered (protocol/schema/README.md's
    //  "Registered state areas"), so every snapshot_request is rejected.
    Fixture fixture(SessionTrustTier::kFull,
                    SessionAuthMethod::kTrustedDeviceCredential);

    auto result = fixture.Process(SnapshotRequestMessage());

    CHECK_FALSE(result.closeConnection);
    REQUIRE(result.responses.size() == 1);
    auto error =
        dovahlink::protocol::DecodeErrorPayload(result.responses[0].payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "unsupported_capability");
}

TEST_CASE("ProcessInboundMessage rejects text that is not valid JSON as "
          "malformed_message, uncorrelated",
          "[application][message_dispatcher]") {
    Fixture fixture(SessionTrustTier::kFull,
                    SessionAuthMethod::kTrustedDeviceCredential);
    auto result = fixture.Process("not json at all {{{");

    CHECK_FALSE(result.closeConnection);
    REQUIRE(result.responses.size() == 1);
    CHECK_FALSE(result.responses[0].correlationId.has_value());
    auto error =
        dovahlink::protocol::DecodeErrorPayload(result.responses[0].payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "malformed_message");
}

TEST_CASE("ProcessInboundMessage closes immediately on an oversized frame, "
          "with no response",
          "[application][message_dispatcher]") {
    Fixture fixture(SessionTrustTier::kFull,
                    SessionAuthMethod::kTrustedDeviceCredential);
    //  Larger than security::kMaxInboundFrameBytes (1 MiB); the exact
    //  content doesn't matter, only the size.
    std::string oversized(1024 * 1024 + 1, 'a');

    auto result = fixture.Process(oversized);

    CHECK(result.closeConnection);
    CHECK(result.responses.empty());
}

TEST_CASE("ProcessInboundMessage rejects hello on an authenticated connection "
          "as malformed_message",
          "[application][message_dispatcher]") {
    Fixture fixture(SessionTrustTier::kFull,
                    SessionAuthMethod::kTrustedDeviceCredential);
    std::string message =
        R"({"messageType": "hello", "messageId": "message-hello-2", "sessionId": null, "correlationId": null, )"
        R"("payload": {"endpoint": "client", "clientId": "client-1", )"
        R"("auth": {"method": "one_time_local_token", "token": "t"}}, )"
        R"("bridgeInstanceId": null, "playContextId": null, "clientId": null})";

    auto result = fixture.Process(message);

    REQUIRE(result.responses.size() == 1);
    auto error =
        dovahlink::protocol::DecodeErrorPayload(result.responses[0].payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "malformed_message");
}

TEST_CASE("ProcessInboundMessage rejects a foreign sessionId as stale_session",
          "[application][message_dispatcher]") {
    Fixture fixture(SessionTrustTier::kFull,
                    SessionAuthMethod::kTrustedDeviceCredential);
    std::string message =
        R"({"messageType": "ping", "messageId": "message-ping-1", "sessionId": "some-other-session", )"
        R"("correlationId": null, "payload": {}, )"
        R"("bridgeInstanceId": null, "playContextId": null, "clientId": null})";

    auto result = fixture.Process(message);

    REQUIRE(result.responses.size() == 1);
    auto error =
        dovahlink::protocol::DecodeErrorPayload(result.responses[0].payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "stale_session");
    //  Matches protocol/fixtures/errors/error-stale-session.json, which
    //  carries a real bridgeInstanceId despite being an early rejection.
    REQUIRE(result.responses[0].bridgeInstanceId.has_value());
    CHECK(*result.responses[0].bridgeInstanceId == "bridge-1");
}

TEST_CASE("ProcessInboundMessage rejects a replayed messageId",
          "[application][message_dispatcher]") {
    Fixture fixture(SessionTrustTier::kFull,
                    SessionAuthMethod::kTrustedDeviceCredential);
    auto first = fixture.Process(PingMessage("message-ping-1"));
    REQUIRE(first.responses.size() == 1);
    REQUIRE(first.responses[0].messageType == "pong");

    auto second = fixture.Process(PingMessage("message-ping-1"));

    REQUIRE(second.responses.size() == 1);
    auto error =
        dovahlink::protocol::DecodeErrorPayload(second.responses[0].payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "replayed_message");
    //  Matches protocol/fixtures/errors/error-replayed-message.json, which
    //  carries a real bridgeInstanceId despite being an early rejection.
    REQUIRE(second.responses[0].bridgeInstanceId.has_value());
    CHECK(*second.responses[0].bridgeInstanceId == "bridge-1");
}

TEST_CASE("ProcessInboundMessage counts unique, replayed, and malformed "
          "messages toward the session cap",
          "[application][message_dispatcher]") {
    Fixture fixture(SessionTrustTier::kFull,
                    SessionAuthMethod::kTrustedDeviceCredential);
    fixture.receivedMessageCount =
        dovahlink::security::kMaxMessagesPerSession - 4;

    auto unique = fixture.Process(PingMessage("message-ping-1"));
    REQUIRE(unique.responses.size() == 1);
    CHECK(unique.responses[0].messageType == "pong");

    auto replayed = fixture.Process(PingMessage("message-ping-1"));
    REQUIRE(replayed.responses.size() == 1);
    auto replayError =
        dovahlink::protocol::DecodeErrorPayload(replayed.responses[0].payload);
    REQUIRE(replayError.has_value());
    CHECK(replayError->code == "replayed_message");

    auto malformed = fixture.Process("not json {{{");
    REQUIRE(malformed.responses.size() == 1);
    auto malformedError =
        dovahlink::protocol::DecodeErrorPayload(malformed.responses[0].payload);
    REQUIRE(malformedError.has_value());
    CHECK(malformedError->code == "malformed_message");

    auto finalAllowed = fixture.Process(PingMessage("message-ping-2"));
    CHECK_FALSE(finalAllowed.closeConnection);
    REQUIRE(finalAllowed.responses.size() == 1);
    CHECK(finalAllowed.responses[0].messageType == "pong");
    CHECK(fixture.receivedMessageCount ==
          dovahlink::security::kMaxMessagesPerSession);

    auto overLimit = fixture.Process(PingMessage("message-ping-3"));

    CHECK(overLimit.closeConnection);
    CHECK(overLimit.responses.empty());
    CHECK(fixture.receivedMessageCount ==
          dovahlink::security::kMaxMessagesPerSession + 1);
    CHECK(fixture.replayGuard.Count() == 2);
}

TEST_CASE("ProcessInboundMessage rejects the 101st message within a second as "
          "rate_limited",
          "[application][message_dispatcher]") {
    Fixture fixture(SessionTrustTier::kFull,
                    SessionAuthMethod::kTrustedDeviceCredential);
    auto now = SteadyClock::now();
    for (int i = 0; i < 100; ++i) {
        auto result =
            fixture.Process(PingMessage("message-ping-" + std::to_string(i)), now);
        REQUIRE(result.responses.size() == 1);
        REQUIRE(result.responses[0].messageType == "pong");
    }

    auto result = fixture.Process(PingMessage("message-ping-101"), now);

    REQUIRE(result.responses.size() == 1);
    auto error =
        dovahlink::protocol::DecodeErrorPayload(result.responses[0].payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "rate_limited");
    CHECK(error->retryable);
    //  Matches protocol/fixtures/errors/error-rate-limited.json, which
    //  carries a real bridgeInstanceId despite being an early rejection.
    REQUIRE(result.responses[0].bridgeInstanceId.has_value());
    CHECK(*result.responses[0].bridgeInstanceId == "bridge-1");
}

TEST_CASE("ProcessInboundMessage closes the connection on the 3rd protocol "
          "violation within 30 seconds",
          "[application][message_dispatcher]") {
    Fixture fixture(SessionTrustTier::kFull,
                    SessionAuthMethod::kTrustedDeviceCredential);
    auto now = SteadyClock::now();

    auto first = fixture.Process("not json {{{", now);
    auto second = fixture.Process("not json {{{", now + std::chrono::seconds(1));
    auto third = fixture.Process("not json {{{", now + std::chrono::seconds(2));

    CHECK_FALSE(first.closeConnection);
    CHECK_FALSE(second.closeConnection);
    CHECK(third.closeConnection);
}

TEST_CASE("ProcessInboundMessage closes with no response once the connection's "
          "timeout window "
          "has already elapsed, even for an otherwise-valid message",
          "[application][message_dispatcher]") {
    Fixture fixture(SessionTrustTier::kFull,
                    SessionAuthMethod::kTrustedDeviceCredential);
    auto start = SteadyClock::now();
    fixture.timeout = ConnectionTimeoutTracker(start);
    fixture.timeout.MarkAuthenticated(start);

    //  60s idle deadline from MarkAuthenticated(start) is start+60s; this
    //  proves the check applies to a message that would otherwise be
    //  perfectly valid (a fresh, unseen ping), not just to already-invalid
    //  input.
    auto result = fixture.Process(PingMessage("message-ping-late"),
                                  start + std::chrono::seconds(61));

    CHECK(result.closeConnection);
    CHECK(result.responses.empty());
    //  The early return must skip replay tracking too, not just skip sending
    //  a response: a message from a connection that's about to be closed
    //  anyway must not be recorded as seen.
    CHECK(fixture.replayGuard.Count() == 0);
}

TEST_CASE("ProcessInboundMessage's idle deadline survives a stream of "
          "malformed and replayed "
          "messages: neither counts as activity",
          "[application][message_dispatcher]") {
    //  Distinct from the two tests above: this proves WHY the idle deadline needs
    //  its own application-level tracking at all, not just a transport-level "was
    //  there any I/O" timer. A client spamming malformed or replayed traffic keeps
    //  a raw I/O-activity timer perpetually reset without ever doing anything
    //  genuine; RecordActivity's placement after the replay/rate/ allowlist checks
    //  in ProcessInboundMessage (not before) is what stops that from working here.
    Fixture fixture(SessionTrustTier::kFull,
                    SessionAuthMethod::kTrustedDeviceCredential);
    auto start = SteadyClock::now();
    fixture.timeout = ConnectionTimeoutTracker(start);
    fixture.timeout.MarkAuthenticated(start);

    //  The only genuinely accepted message in this whole test: sets the idle
    //  deadline to start+20s+60s = start+80s.
    auto ping = fixture.Process(PingMessage("message-ping-1"),
                                start + std::chrono::seconds(20));
    REQUIRE_FALSE(ping.closeConnection);
    REQUIRE(ping.responses.size() == 1);
    CHECK(ping.responses[0].messageType == "pong");

    //  A replay of that same message: rejected before RecordActivity ever runs.
    auto replayed = fixture.Process(PingMessage("message-ping-1"),
                                    start + std::chrono::seconds(30));
    CHECK_FALSE(replayed.closeConnection);
    REQUIRE(replayed.responses.size() == 1);
    auto replayedError =
        dovahlink::protocol::DecodeErrorPayload(replayed.responses[0].payload);
    REQUIRE(replayedError.has_value());
    CHECK(replayedError->code == "replayed_message");

    //  Malformed JSON: rejected even earlier, before envelope decoding.
    auto malformed =
        fixture.Process("not json at all {{{", start + std::chrono::seconds(70));
    CHECK_FALSE(malformed.closeConnection);
    REQUIRE(malformed.responses.size() == 1);
    auto malformedError =
        dovahlink::protocol::DecodeErrorPayload(malformed.responses[0].payload);
    REQUIRE(malformedError.has_value());
    CHECK(malformedError->code == "malformed_message");

    //  start+81s is past start+80s -- the deadline set by the one genuinely
    //  accepted message above, unmoved by the replayed or malformed traffic in
    //  between -- even though an otherwise-valid ping arrives here.
    auto finalPing = fixture.Process(PingMessage("message-ping-2"),
                                     start + std::chrono::seconds(81));
    CHECK(finalPing.closeConnection);
    CHECK(finalPing.responses.empty());
}

TEST_CASE("ProcessInboundMessage still processes an otherwise-valid message "
          "just before the "
          "timeout deadline",
          "[application][message_dispatcher]") {
    Fixture fixture(SessionTrustTier::kFull,
                    SessionAuthMethod::kTrustedDeviceCredential);
    auto start = SteadyClock::now();
    fixture.timeout = ConnectionTimeoutTracker(start);
    fixture.timeout.MarkAuthenticated(start);

    //  60s idle deadline from MarkAuthenticated(start) is start+60s;
    //  start+59s is not yet timed out, the negation of the case above.
    auto result = fixture.Process(PingMessage("message-ping-on-time"),
                                  start + std::chrono::seconds(59));

    CHECK_FALSE(result.closeConnection);
    REQUIRE(result.responses.size() == 1);
    CHECK(result.responses[0].messageType == "pong");
}

TEST_CASE("ProcessInboundMessage counts a handler-produced error as a protocol "
          "violation, "
          "not just a dispatcher-level rejection",
          "[application][message_dispatcher]") {
    //  Distinct from the malformed-JSON violation test above: this drives an
    //  error through HandleSnapshotRequest itself (a valid envelope, rejected
    //  because no state area is currently registered), proving
    //  FromHandlerResponse's "was this an error" inference actually feeds the
    //  same violation counter, not just Reject()'s dispatcher-level path.
    Fixture fixture(SessionTrustTier::kFull,
                    SessionAuthMethod::kTrustedDeviceCredential);
    auto now = SteadyClock::now();
    auto unknownAreaRequest = [](std::string messageId) {
        return R"({"messageType": "snapshot_request", "messageId": ")" + messageId +
               R"(", "sessionId": ")" + std::string(kSessionId) +
               R"(", "correlationId": null, "payload": {"stateArea": "inventory"}, )"
               R"("bridgeInstanceId": null, "playContextId": null, "clientId": null})";
    };

    auto first = fixture.Process(unknownAreaRequest("message-snap-1"), now);
    auto second = fixture.Process(unknownAreaRequest("message-snap-2"),
                                  now + std::chrono::seconds(1));
    auto third = fixture.Process(unknownAreaRequest("message-snap-3"),
                                 now + std::chrono::seconds(2));

    REQUIRE(first.responses.size() == 1);
    auto error =
        dovahlink::protocol::DecodeErrorPayload(first.responses[0].payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "unsupported_capability");

    CHECK_FALSE(first.closeConnection);
    CHECK_FALSE(second.closeConnection);
    CHECK(third.closeConnection);
}

TEST_CASE("ProcessInboundMessage lets a Restricted session exchange pairing "
          "messages but rejects "
          "subscribe and snapshot_request",
          "[application][message_dispatcher]") {
    Fixture fixture(SessionTrustTier::kRestricted, SessionAuthMethod::kUnpaired);

    auto pairingRequest = fixture.Process(PairingRequestMessage());
    REQUIRE(pairingRequest.responses.size() == 1);
    CHECK(pairingRequest.responses[0].messageType == "pairing_status");

    auto subscribe = fixture.Process(SubscribeMessage());
    REQUIRE(subscribe.responses.size() == 1);
    auto subscribeError =
        dovahlink::protocol::DecodeErrorPayload(subscribe.responses[0].payload);
    REQUIRE(subscribeError.has_value());
    CHECK(subscribeError->code == "malformed_message");

    auto snapshotRequest = fixture.Process(SnapshotRequestMessage());
    REQUIRE(snapshotRequest.responses.size() == 1);
    auto snapshotError = dovahlink::protocol::DecodeErrorPayload(
        snapshotRequest.responses[0].payload);
    REQUIRE(snapshotError.has_value());
    CHECK(snapshotError->code == "malformed_message");
}

TEST_CASE("ProcessInboundMessage lets a Full session subscribe but rejects all "
          "three pairing message "
          "types",
          "[application][message_dispatcher]") {
    Fixture fixture(SessionTrustTier::kFull,
                    SessionAuthMethod::kTrustedDeviceCredential);

    auto subscribe = fixture.Process(SubscribeMessage());
    REQUIRE(subscribe.responses.size() == 1);
    CHECK(subscribe.responses[0].messageType == "subscription_ack");

    auto pairingRequest = fixture.Process(PairingRequestMessage());
    REQUIRE(pairingRequest.responses.size() == 1);
    auto requestError = dovahlink::protocol::DecodeErrorPayload(
        pairingRequest.responses[0].payload);
    REQUIRE(requestError.has_value());
    CHECK(requestError->code == "malformed_message");

    auto pairingConfirm = fixture.Process(PairingConfirmMessage("123456"));
    REQUIRE(pairingConfirm.responses.size() == 1);
    auto confirmError = dovahlink::protocol::DecodeErrorPayload(
        pairingConfirm.responses[0].payload);
    REQUIRE(confirmError.has_value());
    CHECK(confirmError->code == "malformed_message");

    auto pairingAck = fixture.Process(PairingAckMessage("aabbcc"));
    REQUIRE(pairingAck.responses.size() == 1);
    auto ackError =
        dovahlink::protocol::DecodeErrorPayload(pairingAck.responses[0].payload);
    REQUIRE(ackError.has_value());
    CHECK(ackError->code == "malformed_message");
}

TEST_CASE("ProcessInboundMessage lets a session upgraded to full trust by a "
          "successful pairing_ack "
          "subscribe on its very next message, with no reconnect",
          "[application][message_dispatcher]") {
    Fixture fixture(SessionTrustTier::kRestricted, SessionAuthMethod::kUnpaired);
    REQUIRE(fixture.trustStore.ResetTrust());
    const auto now = SteadyClock::now();

    auto pairingRequest = fixture.Process(PairingRequestMessage(), now);
    REQUIRE(pairingRequest.responses.size() == 1);
    REQUIRE(fixture.pairingNotificationSink.codes.size() == 1);
    std::string code = fixture.pairingNotificationSink.codes[0];

    auto pairingConfirm = fixture.Process(PairingConfirmMessage(code), now);
    REQUIRE(pairingConfirm.responses.size() == 1);
    auto confirmOutcome = dovahlink::protocol::DecodePairingOutcomePayload(
        pairingConfirm.responses[0].payload);
    REQUIRE(confirmOutcome.has_value());
    REQUIRE(confirmOutcome->outcome == "credential_issued");
    REQUIRE(confirmOutcome->credential.has_value());
    auto credential = DecodeHex(*confirmOutcome->credential);
    REQUIRE(credential.has_value());
    auto pending =
        fixture.pairingSession.PeekPending(kClientId, *credential, now);
    REQUIRE(pending.has_value());
    CHECK(pending->mutationGeneration ==
          fixture.trustStore.CurrentMutationGeneration(kClientId));

    auto pairingAck =
        fixture.Process(PairingAckMessage(*confirmOutcome->credential), now);
    REQUIRE(pairingAck.responses.size() == 1);
    auto ackOutcome = dovahlink::protocol::DecodePairingOutcomePayload(
        pairingAck.responses[0].payload);
    REQUIRE(ackOutcome.has_value());
    REQUIRE(ackOutcome->outcome == "trusted");

    //  Restricted before the ack; this proves the upgrade landed without a
    //  reconnect, since the fixture's SessionManager/connection/sessionId are
    //  unchanged from the pairing exchange above.
    auto subscribe = fixture.Process(SubscribeMessage());
    REQUIRE(subscribe.responses.size() == 1);
    CHECK(subscribe.responses[0].messageType == "subscription_ack");

    //  The allowlist re-evaluates on every message, not just once at session
    //  creation: the same connection that just became Full can no longer reach
    //  pairing_handler.
    auto pairingRequestAfterUpgrade =
        fixture.Process(PairingRequestMessage("message-pairing-request-2"));
    REQUIRE(pairingRequestAfterUpgrade.responses.size() == 1);
    auto afterUpgradeError = dovahlink::protocol::DecodeErrorPayload(
        pairingRequestAfterUpgrade.responses[0].payload);
    REQUIRE(afterUpgradeError.has_value());
    CHECK(afterUpgradeError->code == "malformed_message");
}

TEST_CASE("ProcessInboundMessage checks the trust-tier allowlist before "
          "session validity: a "
          "disallowed-for-tier message with a foreign sessionId is "
          "malformed_message, not "
          "stale_session",
          "[application][message_dispatcher]") {
    Fixture fixture(SessionTrustTier::kRestricted, SessionAuthMethod::kUnpaired);
    std::string message =
        R"({"messageType": "subscribe", "messageId": "message-sub-1", "sessionId": "some-other-session", )"
        R"("correlationId": null, "payload": {"stateAreas": ["example_area"]}, )"
        R"("bridgeInstanceId": null, "playContextId": null, "clientId": null})";

    auto result = fixture.Process(message);

    REQUIRE(result.responses.size() == 1);
    auto error =
        dovahlink::protocol::DecodeErrorPayload(result.responses[0].payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "malformed_message");
}

TEST_CASE("ProcessInboundMessage lets a Restricted session exchange "
          "pairing_renotify and pairing_cancel but "
          "rejects them on Full",
          "[application][message_dispatcher]") {
    Fixture restrictedFixture(SessionTrustTier::kRestricted,
                              SessionAuthMethod::kUnpaired);

    auto renotify = restrictedFixture.Process(PairingRenotifyMessage());
    REQUIRE(renotify.responses.size() == 1);
    CHECK(renotify.responses[0].messageType == "pairing_outcome");

    auto cancel = restrictedFixture.Process(PairingCancelMessage());
    REQUIRE(cancel.responses.size() == 1);
    CHECK(cancel.responses[0].messageType == "pairing_outcome");

    Fixture fullFixture(SessionTrustTier::kFull,
                        SessionAuthMethod::kTrustedDeviceCredential);

    auto renotifyFull = fullFixture.Process(PairingRenotifyMessage());
    REQUIRE(renotifyFull.responses.size() == 1);
    auto renotifyError = dovahlink::protocol::DecodeErrorPayload(
        renotifyFull.responses[0].payload);
    REQUIRE(renotifyError.has_value());
    CHECK(renotifyError->code == "malformed_message");
    REQUIRE(renotifyFull.responses[0].bridgeInstanceId.has_value());
    CHECK(*renotifyFull.responses[0].bridgeInstanceId == "bridge-1");

    auto cancelFull = fullFixture.Process(PairingCancelMessage());
    REQUIRE(cancelFull.responses.size() == 1);
    auto cancelError =
        dovahlink::protocol::DecodeErrorPayload(cancelFull.responses[0].payload);
    REQUIRE(cancelError.has_value());
    CHECK(cancelError->code == "malformed_message");
    REQUIRE(cancelFull.responses[0].bridgeInstanceId.has_value());
    CHECK(*cancelFull.responses[0].bridgeInstanceId == "bridge-1");
}

TEST_CASE("ProcessInboundMessage accepts rename_request on a Full session",
          "[application][message_dispatcher]") {
    Fixture fixture(SessionTrustTier::kFull,
                    SessionAuthMethod::kTrustedDeviceCredential);
    REQUIRE(
        fixture.trustStore
            .Persist(kClientId, std::vector<std::uint8_t>{1, 2, 3, 4}, "Old Name")
            .has_value());

    auto result = fixture.Process(RenameRequestMessage("New Name"));

    REQUIRE(result.responses.size() == 1);
    CHECK(result.responses[0].messageType == "rename_outcome");
    auto outcome = dovahlink::protocol::DecodeRenameOutcomePayload(
        result.responses[0].payload);
    REQUIRE(outcome.has_value());
    CHECK(outcome->outcome == "renamed");
    REQUIRE(outcome->displayName.has_value());
    CHECK(*outcome->displayName == "New Name");
    REQUIRE(result.responses[0].bridgeInstanceId.has_value());
    CHECK(*result.responses[0].bridgeInstanceId == "bridge-1");
    CHECK_FALSE(result.responses[0].clientId.has_value());
}

TEST_CASE(
    "ProcessInboundMessage rejects rename_request on a Restricted session",
    "[application][message_dispatcher]") {
    Fixture fixture(SessionTrustTier::kRestricted, SessionAuthMethod::kUnpaired);

    auto result = fixture.Process(RenameRequestMessage("New Name"));

    REQUIRE(result.responses.size() == 1);
    auto error =
        dovahlink::protocol::DecodeErrorPayload(result.responses[0].payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "malformed_message");
}

TEST_CASE("ProcessInboundMessage stamps bridgeInstanceId and playContextId on "
          "pairing_renotify and "
          "pairing_cancel responses too, never clientId",
          "[application][message_dispatcher]") {
    Fixture fixture(SessionTrustTier::kRestricted, SessionAuthMethod::kUnpaired);
    fixture.activePlayContext.Begin("context-1");

    auto renotify = fixture.Process(PairingRenotifyMessage());
    REQUIRE(renotify.responses.size() == 1);
    REQUIRE(renotify.responses[0].bridgeInstanceId.has_value());
    CHECK(*renotify.responses[0].bridgeInstanceId == "bridge-1");
    REQUIRE(renotify.responses[0].playContextId.has_value());
    CHECK(*renotify.responses[0].playContextId == "context-1");
    CHECK_FALSE(renotify.responses[0].clientId.has_value());

    auto cancel = fixture.Process(PairingCancelMessage());
    REQUIRE(cancel.responses.size() == 1);
    REQUIRE(cancel.responses[0].bridgeInstanceId.has_value());
    CHECK(*cancel.responses[0].bridgeInstanceId == "bridge-1");
    REQUIRE(cancel.responses[0].playContextId.has_value());
    CHECK(*cancel.responses[0].playContextId == "context-1");
    CHECK_FALSE(cancel.responses[0].clientId.has_value());
}

TEST_CASE("ProcessInboundMessage's capabilities handling is unaffected by "
          "play-context routing",
          "[application][message_dispatcher]") {
    //  HandleClientCapabilities never touches the play context, so it must behave
    //  identically whether or not one is active -- proven explicitly here rather
    //  than left as an unstated assumption.
    Fixture fixture(SessionTrustTier::kFull,
                    SessionAuthMethod::kTrustedDeviceCredential);
    fixture.activePlayContext.Begin("context-1");
    std::string message =
        R"({"messageType": "capabilities", "messageId": "message-cap-1", "sessionId": ")" +
        std::string(kSessionId) +
        R"(", "correlationId": null, "payload": {"capabilities": []}, )"
        R"("bridgeInstanceId": null, "playContextId": null, "clientId": null})";

    auto result = fixture.Process(message);

    CHECK_FALSE(result.closeConnection);
    CHECK(result.responses.empty());
}

TEST_CASE(
    "ProcessInboundMessage accepts an empty capabilities list with no response",
    "[application][message_dispatcher]") {
    Fixture fixture(SessionTrustTier::kFull,
                    SessionAuthMethod::kTrustedDeviceCredential);
    std::string message =
        R"({"messageType": "capabilities", "messageId": "message-cap-1", "sessionId": ")" +
        std::string(kSessionId) +
        R"(", "correlationId": null, "payload": {"capabilities": []}, )"
        R"("bridgeInstanceId": null, "playContextId": null, "clientId": null})";

    auto result = fixture.Process(message);

    CHECK_FALSE(result.closeConnection);
    CHECK(result.responses.empty());
}

TEST_CASE("ProcessInboundMessage accepts an empty capabilities list on a "
          "Restricted session too",
          "[application][message_dispatcher]") {
    //  capabilities is in kRestrictedAllowedMessageTypes -- the only one of the
    //  two capabilities tests above proven on a Restricted session, matching the
    //  "lets a Restricted session exchange pairing messages but rejects
    //  subscribe/snapshot_request" test's own tier scope.
    Fixture fixture(SessionTrustTier::kRestricted, SessionAuthMethod::kUnpaired);
    std::string message =
        R"({"messageType": "capabilities", "messageId": "message-cap-1", "sessionId": ")" +
        std::string(kSessionId) +
        R"(", "correlationId": null, "payload": {"capabilities": []}, )"
        R"("bridgeInstanceId": null, "playContextId": null, "clientId": null})";

    auto result = fixture.Process(message);

    CHECK_FALSE(result.closeConnection);
    CHECK(result.responses.empty());
}
