#include "application/message_dispatcher.hpp"

#include "application/character_state.hpp"
#include "protocol/messages.hpp"
#include "security/limits.hpp"

#include <catch2/catch_test_macros.hpp>

#include <chrono>
#include <cstddef>
#include <optional>
#include <string>

using dovahlink::application::CharacterSnapshot;
using dovahlink::application::CharacterStateProvider;
using dovahlink::application::ConnectionTimeoutTracker;
using dovahlink::application::DispatchResult;
using dovahlink::application::ProcessInboundMessage;
using dovahlink::application::ReplayGuard;
using dovahlink::application::RevisionTracker;
using dovahlink::application::SessionManager;
using dovahlink::security::InboundMessageRateLimiter;
using dovahlink::security::ViolationTracker;

namespace {

/// Clock type used for deterministic dispatcher timeout assertions.
using SteadyClock = std::chrono::steady_clock;

constexpr dovahlink::application::ConnectionId kConnection = 1;
constexpr const char* kSessionId = "session-1";

/// Supplies a deterministic character snapshot to dispatcher tests.
class FakeCharacterStateProvider : public CharacterStateProvider {
public:
    /// @copydoc CharacterStateProvider::CurrentCharacterSnapshot
    [[nodiscard]] CharacterSnapshot CurrentCharacterSnapshot() const override { return CharacterSnapshot{.level = 7}; }
};

/// Bundles an authenticated dispatcher session and its production collaborators.
struct Fixture {
    /// Counts every completed inbound message before decoding.
    std::size_t receivedMessageCount = 0;
    /// Tracks the session used by the dispatcher.
    SessionManager sessions;
    /// Owns the authenticated session for the fixture lifetime.
    std::optional<SessionManager::Lease> sessionLease;
    /// Rejects duplicate inbound message IDs.
    ReplayGuard replayGuard;
    /// Tracks protocol violations for the session.
    ViolationTracker violations;
    /// Limits inbound message rates for the session.
    InboundMessageRateLimiter rateLimiter;
    /// Tracks handshake and authenticated idle deadlines.
    ConnectionTimeoutTracker timeout{SteadyClock::now()};
    /// Supplies the deterministic character snapshot.
    FakeCharacterStateProvider stateProvider;
    /// Tracks revisions for state responses and events.
    RevisionTracker revisions;

    /// Creates the fixture with its test session already authenticated.
    Fixture() : sessionLease(sessions.TryCreateSession(kConnection, kSessionId)) {
        REQUIRE(sessionLease.has_value());
    }

    /// Processes a raw message using the fixture's authenticated session.
    DispatchResult Process(const std::string& rawMessage, SteadyClock::time_point steadyNow = SteadyClock::now()) {
        return ProcessInboundMessage(rawMessage, receivedMessageCount, kSessionId, kConnection, sessions,
                                     replayGuard, violations, rateLimiter, timeout, stateProvider, revisions,
                                     steadyNow, std::chrono::system_clock::now());
    }
};

/// Builds a valid ping envelope for the fixture's authenticated session.
std::string PingMessage(std::string messageId = "message-ping-1") {
    return R"({"protocolVersion": 1, "messageType": "ping", "messageId": ")" + messageId +
           R"(", "sessionId": ")" + kSessionId + R"(", "correlationId": null, "payload": {}})";
}

}  // namespace

TEST_CASE("ProcessInboundMessage answers ping with pong and resets the idle timeout",
          "[application][message_dispatcher]") {
    Fixture fixture;
    auto start = SteadyClock::now();
    fixture.timeout = ConnectionTimeoutTracker(start);
    fixture.timeout.MarkAuthenticated(start);

    auto result = fixture.Process(PingMessage(), start + std::chrono::seconds(30));

    CHECK_FALSE(result.closeConnection);
    REQUIRE(result.responses.size() == 1);
    CHECK(result.responses[0].messageType == "pong");
    REQUIRE(result.responses[0].correlationId.has_value());
    CHECK(*result.responses[0].correlationId == "message-ping-1");

    // The 60s idle deadline was reset to start+30s+60s=start+90s by
    // RecordActivity. Checking at start+65s: after the original deadline
    // (start+60s from MarkAuthenticated, which would already have expired
    // without RecordActivity) but still before the new one, so this can
    // only pass if RecordActivity actually moved the deadline.
    CHECK_FALSE(fixture.timeout.IsTimedOut(start + std::chrono::seconds(65)));
}

TEST_CASE("ProcessInboundMessage routes subscribe to a subscription_ack and a snapshot",
          "[application][message_dispatcher]") {
    Fixture fixture;
    std::string message =
        R"({"protocolVersion": 1, "messageType": "subscribe", "messageId": "message-sub-1", )"
        R"("sessionId": ")" +
        std::string(kSessionId) + R"(", "correlationId": null, "payload": {"stateAreas": ["character"]}})";

    auto result = fixture.Process(message);

    CHECK_FALSE(result.closeConnection);
    REQUIRE(result.responses.size() == 2);
    CHECK(result.responses[0].messageType == "subscription_ack");
    CHECK(result.responses[1].messageType == "state_snapshot");
}

TEST_CASE("ProcessInboundMessage accepts an empty capabilities list with no response",
          "[application][message_dispatcher]") {
    Fixture fixture;
    std::string message =
        R"({"protocolVersion": 1, "messageType": "capabilities", "messageId": "message-cap-1", )"
        R"("sessionId": ")" +
        std::string(kSessionId) + R"(", "correlationId": null, "payload": {"capabilities": []}})";

    auto result = fixture.Process(message);

    CHECK_FALSE(result.closeConnection);
    CHECK(result.responses.empty());
}

TEST_CASE("ProcessInboundMessage routes snapshot_request to a state_snapshot", "[application][message_dispatcher]") {
    Fixture fixture;
    std::string message =
        R"({"protocolVersion": 1, "messageType": "snapshot_request", "messageId": "message-snap-req-1", )"
        R"("sessionId": ")" +
        std::string(kSessionId) + R"(", "correlationId": null, "payload": {"stateArea": "character"}})";

    auto result = fixture.Process(message);

    CHECK_FALSE(result.closeConnection);
    REQUIRE(result.responses.size() == 1);
    CHECK(result.responses[0].messageType == "state_snapshot");
}

TEST_CASE("ProcessInboundMessage rejects text that is not valid JSON as malformed_message, uncorrelated",
          "[application][message_dispatcher]") {
    Fixture fixture;
    auto result = fixture.Process("not json at all {{{");

    CHECK_FALSE(result.closeConnection);
    REQUIRE(result.responses.size() == 1);
    CHECK_FALSE(result.responses[0].correlationId.has_value());
    auto error = dovahlink::protocol::DecodeErrorPayload(result.responses[0].payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "malformed_message");
}

TEST_CASE("ProcessInboundMessage closes immediately on an oversized frame, with no response",
          "[application][message_dispatcher]") {
    Fixture fixture;
    // Larger than security::kMaxInboundFrameBytes (1 MiB); the exact
    // content doesn't matter, only the size.
    std::string oversized(1024 * 1024 + 1, 'a');

    auto result = fixture.Process(oversized);

    CHECK(result.closeConnection);
    CHECK(result.responses.empty());
}

TEST_CASE("ProcessInboundMessage rejects hello on an authenticated connection as malformed_message",
          "[application][message_dispatcher]") {
    Fixture fixture;
    std::string message = R"({"protocolVersion": 0, "messageType": "hello", "messageId": "message-hello-2", )"
                           R"("sessionId": null, "correlationId": null, "payload": {"endpoint": "client", )"
                           R"("supportedProtocolVersions": [1], "auth": {"method": "one_time_local_token", )"
                           R"("token": "t"}}})";

    auto result = fixture.Process(message);

    REQUIRE(result.responses.size() == 1);
    auto error = dovahlink::protocol::DecodeErrorPayload(result.responses[0].payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "malformed_message");
}

TEST_CASE("ProcessInboundMessage rejects a foreign sessionId as stale_session",
          "[application][message_dispatcher]") {
    Fixture fixture;
    std::string message = R"({"protocolVersion": 1, "messageType": "ping", "messageId": "message-ping-1", )"
                           R"("sessionId": "some-other-session", "correlationId": null, "payload": {}})";

    auto result = fixture.Process(message);

    REQUIRE(result.responses.size() == 1);
    auto error = dovahlink::protocol::DecodeErrorPayload(result.responses[0].payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "stale_session");
}

TEST_CASE("ProcessInboundMessage rejects a replayed messageId", "[application][message_dispatcher]") {
    Fixture fixture;
    auto first = fixture.Process(PingMessage("message-ping-1"));
    REQUIRE(first.responses.size() == 1);
    REQUIRE(first.responses[0].messageType == "pong");

    auto second = fixture.Process(PingMessage("message-ping-1"));

    REQUIRE(second.responses.size() == 1);
    auto error = dovahlink::protocol::DecodeErrorPayload(second.responses[0].payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "replayed_message");
}

TEST_CASE("ProcessInboundMessage counts unique, replayed, and malformed messages toward the session cap",
          "[application][message_dispatcher]") {
    Fixture fixture;
    fixture.receivedMessageCount = dovahlink::security::kMaxMessagesPerSession - 4;

    auto unique = fixture.Process(PingMessage("message-ping-1"));
    REQUIRE(unique.responses.size() == 1);
    CHECK(unique.responses[0].messageType == "pong");

    auto replayed = fixture.Process(PingMessage("message-ping-1"));
    REQUIRE(replayed.responses.size() == 1);
    auto replayError = dovahlink::protocol::DecodeErrorPayload(replayed.responses[0].payload);
    REQUIRE(replayError.has_value());
    CHECK(replayError->code == "replayed_message");

    auto malformed = fixture.Process("not json {{{");
    REQUIRE(malformed.responses.size() == 1);
    auto malformedError = dovahlink::protocol::DecodeErrorPayload(malformed.responses[0].payload);
    REQUIRE(malformedError.has_value());
    CHECK(malformedError->code == "malformed_message");

    auto finalAllowed = fixture.Process(PingMessage("message-ping-2"));
    CHECK_FALSE(finalAllowed.closeConnection);
    REQUIRE(finalAllowed.responses.size() == 1);
    CHECK(finalAllowed.responses[0].messageType == "pong");
    CHECK(fixture.receivedMessageCount == dovahlink::security::kMaxMessagesPerSession);

    auto overLimit = fixture.Process(PingMessage("message-ping-3"));

    CHECK(overLimit.closeConnection);
    CHECK(overLimit.responses.empty());
    CHECK(fixture.receivedMessageCount == dovahlink::security::kMaxMessagesPerSession + 1);
    CHECK(fixture.replayGuard.Count() == 2);
}

TEST_CASE("ProcessInboundMessage rejects the 101st message within a second as rate_limited",
          "[application][message_dispatcher]") {
    Fixture fixture;
    auto now = SteadyClock::now();
    for (int i = 0; i < 100; ++i) {
        auto result = fixture.Process(PingMessage("message-ping-" + std::to_string(i)), now);
        REQUIRE(result.responses.size() == 1);
        REQUIRE(result.responses[0].messageType == "pong");
    }

    auto result = fixture.Process(PingMessage("message-ping-101"), now);

    REQUIRE(result.responses.size() == 1);
    auto error = dovahlink::protocol::DecodeErrorPayload(result.responses[0].payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "rate_limited");
    CHECK(error->retryable);
}

TEST_CASE("ProcessInboundMessage closes the connection on the 3rd protocol violation within 30 seconds",
          "[application][message_dispatcher]") {
    Fixture fixture;
    auto now = SteadyClock::now();

    auto first = fixture.Process("not json {{{", now);
    auto second = fixture.Process("not json {{{", now + std::chrono::seconds(1));
    auto third = fixture.Process("not json {{{", now + std::chrono::seconds(2));

    CHECK_FALSE(first.closeConnection);
    CHECK_FALSE(second.closeConnection);
    CHECK(third.closeConnection);
}

TEST_CASE("ProcessInboundMessage closes with no response once the connection's timeout window "
          "has already elapsed, even for an otherwise-valid message",
          "[application][message_dispatcher]") {
    Fixture fixture;
    auto start = SteadyClock::now();
    fixture.timeout = ConnectionTimeoutTracker(start);
    fixture.timeout.MarkAuthenticated(start);

    // 60s idle deadline from MarkAuthenticated(start) is start+60s; this
    // proves the check applies to a message that would otherwise be
    // perfectly valid (a fresh, unseen ping), not just to already-invalid
    // input.
    auto result = fixture.Process(PingMessage("message-ping-late"), start + std::chrono::seconds(61));

    CHECK(result.closeConnection);
    CHECK(result.responses.empty());
    // The early return must skip replay tracking too, not just skip sending
    // a response: a message from a connection that's about to be closed
    // anyway must not be recorded as seen.
    CHECK(fixture.replayGuard.Count() == 0);
}

TEST_CASE("ProcessInboundMessage still processes an otherwise-valid message just before the "
          "timeout deadline",
          "[application][message_dispatcher]") {
    Fixture fixture;
    auto start = SteadyClock::now();
    fixture.timeout = ConnectionTimeoutTracker(start);
    fixture.timeout.MarkAuthenticated(start);

    // 60s idle deadline from MarkAuthenticated(start) is start+60s;
    // start+59s is not yet timed out, the negation of the case above.
    auto result = fixture.Process(PingMessage("message-ping-on-time"), start + std::chrono::seconds(59));

    CHECK_FALSE(result.closeConnection);
    REQUIRE(result.responses.size() == 1);
    CHECK(result.responses[0].messageType == "pong");
}

TEST_CASE("ProcessInboundMessage counts a handler-produced error as a protocol violation, "
          "not just a dispatcher-level rejection",
          "[application][message_dispatcher]") {
    // Distinct from the malformed-JSON violation test above: this drives an
    // error through HandleSnapshotRequest itself (a valid envelope, rejected
    // for an unregistered state area), proving FromHandlerResponse's
    // "was this an error" inference actually feeds the same violation
    // counter, not just Reject()'s dispatcher-level path.
    Fixture fixture;
    auto now = SteadyClock::now();
    auto unknownAreaRequest = [](std::string messageId) {
        return R"({"protocolVersion": 1, "messageType": "snapshot_request", "messageId": ")" + messageId +
               R"(", "sessionId": ")" + std::string(kSessionId) +
               R"(", "correlationId": null, "payload": {"stateArea": "inventory"}})";
    };

    auto first = fixture.Process(unknownAreaRequest("message-snap-1"), now);
    auto second = fixture.Process(unknownAreaRequest("message-snap-2"), now + std::chrono::seconds(1));
    auto third = fixture.Process(unknownAreaRequest("message-snap-3"), now + std::chrono::seconds(2));

    REQUIRE(first.responses.size() == 1);
    auto error = dovahlink::protocol::DecodeErrorPayload(first.responses[0].payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "unsupported_capability");

    CHECK_FALSE(first.closeConnection);
    CHECK_FALSE(second.closeConnection);
    CHECK(third.closeConnection);
}
