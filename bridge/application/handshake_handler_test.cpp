#include "application/handshake_handler.hpp"

#include "protocol/messages.hpp"
#include "security/hex.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/array.hpp>
#include <boost/json/object.hpp>
#include <boost/json/parse.hpp>

#include <chrono>
#include <cstdint>
#include <optional>
#include <string>
#include <vector>

using dovahlink::application::ActivePlayContext;
using dovahlink::application::ConnectionTimeoutTracker;
using dovahlink::application::HandleHello;
using dovahlink::application::SessionManager;
using dovahlink::protocol::Envelope;
using dovahlink::security::DecodeHex;
using dovahlink::security::FailedTokenThrottle;
using dovahlink::security::TokenStore;

namespace {

// 32 bytes (64 hex characters) each, matching the production token shape
// (security::kTokenBytes).
constexpr const char* kValidHexToken =
    "0123456789abcdefABCDEF00112233445566778899aabbccddeeff0011223344";
constexpr const char* kWrongHexToken =
    "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";

/// Decodes the canonical valid test token into the production byte format.
std::vector<std::uint8_t> ValidTokenBytes() { return *DecodeHex(kValidHexToken); }

/// Builds a hello envelope with a controllable token, message ID, and
/// clientId. `clientId` omits the payload key entirely when `std::nullopt`,
/// so callers can exercise the "clientId missing" rejection path.
Envelope BuildHelloEnvelope(const std::string& token, std::string messageId = "message-hello-1",
                             std::optional<std::string> clientId = std::string("client-1")) {
    boost::json::object payload;
    payload["endpoint"] = "client";
    if (clientId.has_value()) {
        payload["clientId"] = *clientId;
    }
    boost::json::object auth;
    auth["method"] = "one_time_local_token";
    auth["token"] = token;
    payload["auth"] = auth;

    return Envelope{
        .messageType = "hello",
        .messageId = std::move(messageId),
        .sessionId = std::nullopt,
        .correlationId = std::nullopt,
        .payload = payload,
    };
}

}  // namespace

TEST_CASE("HandleHello accepts a valid token and hello", "[application][handshake_handler]") {
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    SessionManager sessions;
    auto start = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(start);

    auto hello = BuildHelloEnvelope(kValidHexToken);
    auto result = HandleHello(hello, tokenStore, throttle, sessions, /*connection=*/1, timeout, start + std::chrono::seconds(1));

    CHECK_FALSE(result.closeConnection);
    REQUIRE(result.sessionLease.has_value());
    CHECK(result.response.messageType == "hello_ack");
    REQUIRE(result.response.sessionId.has_value());
    CHECK_FALSE(result.response.sessionId->empty());
    REQUIRE(result.response.correlationId.has_value());
    CHECK(*result.response.correlationId == "message-hello-1");
    CHECK_FALSE(result.response.messageId.empty());
    CHECK(sessions.IsValidForConnection(*result.response.sessionId, /*connection=*/1));

    auto ack = dovahlink::protocol::DecodeHelloAckPayload(result.response.payload);
    REQUIRE(ack.has_value());
    CHECK(ack->bridgeVersion == "0.0.0");  // HandleHello's default bridgeVersion for callers that omit it.
    CHECK(ack->clientIdentityKind == "unpaired");

    // Identity fields are always echoed now: clientId from the hello,
    // bridgeInstanceId only when the caller supplied one (not here).
    REQUIRE(result.response.clientId.has_value());
    CHECK(*result.response.clientId == "client-1");
    CHECK_FALSE(result.response.bridgeInstanceId.has_value());
    CHECK_FALSE(result.response.playContextId.has_value());

    // Proves MarkAuthenticated actually ran: the handshake deadline was
    // start+5s (security::kHandshakeTimeout), so without MarkAuthenticated
    // switching to the 60s idle deadline, start+6s would already be timed
    // out.
    CHECK_FALSE(timeout.IsTimedOut(start + std::chrono::seconds(6)));
}

TEST_CASE("HandleHello stamps the supplied bridgeInstanceId onto the response",
          "[application][handshake_handler]") {
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);
    ActivePlayContext activePlayContext;

    auto hello = BuildHelloEnvelope(kValidHexToken);
    auto result = HandleHello(hello, tokenStore, throttle, sessions, /*connection=*/1, timeout, now,
                              /*bridgeInstanceId=*/std::string("bridge-1"), activePlayContext);

    REQUIRE(result.response.bridgeInstanceId.has_value());
    CHECK(*result.response.bridgeInstanceId == "bridge-1");
    // No context has been begun, so playContextId stays null even though
    // bridgeInstanceId is present.
    CHECK_FALSE(result.response.playContextId.has_value());
}

TEST_CASE("HandleHello stamps the play context already active at connect time onto the response",
          "[application][handshake_handler]") {
    // Proves a reconnect mid-game (or any connection made while a play
    // context is already loaded) reports it immediately in hello_ack,
    // matching protocol/fixtures/connection/hello-ack-active-context.json.
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);
    ActivePlayContext activePlayContext;
    activePlayContext.Begin("context-1");

    auto hello = BuildHelloEnvelope(kValidHexToken);
    auto result = HandleHello(hello, tokenStore, throttle, sessions, /*connection=*/1, timeout, now,
                              /*bridgeInstanceId=*/std::string("bridge-1"), activePlayContext);

    REQUIRE(result.response.playContextId.has_value());
    CHECK(*result.response.playContextId == "context-1");
}

TEST_CASE("HandleHello uses the supplied bridgeVersion in hello_ack", "[application][handshake_handler]") {
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);
    ActivePlayContext activePlayContext;

    auto hello = BuildHelloEnvelope(kValidHexToken);
    auto result = HandleHello(hello, tokenStore, throttle, sessions, /*connection=*/1, timeout, now,
                              /*bridgeInstanceId=*/std::nullopt, activePlayContext,
                              /*bridgeVersion=*/"0.1.0");

    auto ack = dovahlink::protocol::DecodeHelloAckPayload(result.response.payload);
    REQUIRE(ack.has_value());
    CHECK(ack->bridgeVersion == "0.1.0");
}

TEST_CASE("HandleHello rejects a hello that omits clientId", "[application][handshake_handler]") {
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    auto hello = BuildHelloEnvelope(kValidHexToken, "message-hello-1", /*clientId=*/std::nullopt);
    auto result = HandleHello(hello, tokenStore, throttle, sessions, /*connection=*/1, timeout, now);

    CHECK(result.closeConnection);
    auto error = dovahlink::protocol::DecodeErrorPayload(result.response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "malformed_message");
    // The token must not have been spent, and the failed-token throttle must
    // not have counted this rejection: it happens before either check runs.
    CHECK(tokenStore.IsAvailable());
    CHECK_FALSE(throttle.IsBlocked(now));
}

TEST_CASE("HandleHello rejects a structurally malformed payload", "[application][handshake_handler]") {
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);
    ActivePlayContext activePlayContext;

    Envelope hello{
        .messageType = "hello",
        .messageId = "message-hello-1",
        .sessionId = std::nullopt,
        .correlationId = std::nullopt,
        .payload = boost::json::parse(R"({"endpoint": "client"})").get_object(),  // missing required fields
    };
    // This is the earliest Fail() call site in HandleHello -- runs before
    // the token or session checks below -- so it exercises the
    // bridgeInstanceId stamping at the structurally-first possible point.
    auto result = HandleHello(hello, tokenStore, throttle, sessions, 1, timeout, now,
                              /*bridgeInstanceId=*/std::string("bridge-1"), activePlayContext);

    CHECK(result.closeConnection);
    auto error = dovahlink::protocol::DecodeErrorPayload(result.response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "malformed_message");
    REQUIRE(result.response.bridgeInstanceId.has_value());
    CHECK(*result.response.bridgeInstanceId == "bridge-1");
}

TEST_CASE("HandleHello rejects a wrong token without consuming the real one",
          "[application][handshake_handler]") {
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    auto hello = BuildHelloEnvelope(kWrongHexToken);
    auto result = HandleHello(hello, tokenStore, throttle, sessions, 1, timeout, now);

    CHECK(result.closeConnection);
    auto error = dovahlink::protocol::DecodeErrorPayload(result.response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "unauthenticated");
    CHECK(tokenStore.IsAvailable());
}

TEST_CASE("HandleHello stamps a supplied bridgeInstanceId onto a rejected-token failure response",
          "[application][handshake_handler]") {
    // The bridge's own identity is known before any hello arrives (it is
    // generated once at startup), so a rejection during the handshake itself
    // -- unlike the narrow set of failures where identity generation itself
    // failed -- should still carry it.
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);
    ActivePlayContext activePlayContext;

    auto hello = BuildHelloEnvelope(kWrongHexToken);
    auto result = HandleHello(hello, tokenStore, throttle, sessions, /*connection=*/1, timeout, now,
                              /*bridgeInstanceId=*/std::string("bridge-1"), activePlayContext);

    CHECK(result.closeConnection);
    auto error = dovahlink::protocol::DecodeErrorPayload(result.response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "unauthenticated");
    REQUIRE(result.response.bridgeInstanceId.has_value());
    CHECK(*result.response.bridgeInstanceId == "bridge-1");
}

TEST_CASE("HandleHello rejects a non-hex presented token the same way as a wrong one",
          "[application][handshake_handler]") {
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    auto hello = BuildHelloEnvelope("not-valid-hex!!");
    auto result = HandleHello(hello, tokenStore, throttle, sessions, 1, timeout, now);

    CHECK(result.closeConnection);
    auto error = dovahlink::protocol::DecodeErrorPayload(result.response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "unauthenticated");
    CHECK(tokenStore.IsAvailable());
}

TEST_CASE("HandleHello reports rate_limited once the failed-token throttle trips",
          "[application][handshake_handler]") {
    FailedTokenThrottle throttle;  // shared across every attempt below, like a real deployment.
    auto now = std::chrono::steady_clock::now();

    // security::kMaxFailedTokenAttempts is 5: the first 5 failures are each
    // reported as unauthenticated. The 6th attempt is rejected before token
    // validation, even when it presents the correct token.
    dovahlink::protocol::ErrorPayload lastError;
    TokenStore tokenStore(ValidTokenBytes());
    for (int attempt = 0; attempt < 5; ++attempt) {
        SessionManager sessions;
        ConnectionTimeoutTracker timeout(now);
        auto hello = BuildHelloEnvelope(kWrongHexToken);
        auto result = HandleHello(hello, tokenStore, throttle, sessions, 1, timeout, now);
        auto error = dovahlink::protocol::DecodeErrorPayload(result.response.payload);
        REQUIRE(error.has_value());
        lastError = *error;
    }

    SessionManager sessions;
    ConnectionTimeoutTracker timeout(now);
    auto blocked = HandleHello(BuildHelloEnvelope(kValidHexToken), tokenStore, throttle, sessions,
                               /*connection=*/1, timeout, now);
    auto blockedError = dovahlink::protocol::DecodeErrorPayload(blocked.response.payload);
    REQUIRE(blockedError.has_value());
    lastError = *blockedError;

    CHECK(lastError.code == "rate_limited");
    CHECK(lastError.retryable);
    CHECK(tokenStore.IsAvailable());
}

TEST_CASE("rate-limited handshakes do not extend the failed-attempt window",
          "[application][handshake_handler]") {
    FailedTokenThrottle throttle;
    auto t0 = std::chrono::steady_clock::now();
    for (int attempt = 0; attempt < 5; ++attempt) {
        throttle.RecordFailure(t0);
    }

    TokenStore tokenStore(ValidTokenBytes());
    for (int attempt = 0; attempt < 5; ++attempt) {
        SessionManager blockedSessions;
        ConnectionTimeoutTracker blockedTimeout(t0);
        auto blocked = HandleHello(BuildHelloEnvelope(kValidHexToken, "message-blocked-" +
                                                                          std::to_string(attempt)),
                                   tokenStore, throttle, blockedSessions, /*connection=*/1,
                                   blockedTimeout, t0 + std::chrono::seconds(59));
        auto error = dovahlink::protocol::DecodeErrorPayload(blocked.response.payload);
        REQUIRE(error.has_value());
        CHECK(error->code == "rate_limited");
    }

    SessionManager sessions;
    ConnectionTimeoutTracker timeout(t0);
    auto retry = HandleHello(BuildHelloEnvelope(kValidHexToken), tokenStore, throttle, sessions,
                             /*connection=*/1, timeout, t0 + std::chrono::seconds(60));

    CHECK_FALSE(retry.closeConnection);
    CHECK(retry.sessionLease.has_value());
}

TEST_CASE("HandleHello rejects a second connection while a session is already active",
          "[application][handshake_handler]") {
    SessionManager sessions;
    auto existingLease = sessions.TryCreateSession(/*connection=*/1, "existing-session");
    REQUIRE(existingLease.has_value());

    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    auto hello = BuildHelloEnvelope(kValidHexToken);
    auto result = HandleHello(hello, tokenStore, throttle, sessions, /*connection=*/2, timeout, now);

    CHECK(result.closeConnection);
    auto error = dovahlink::protocol::DecodeErrorPayload(result.response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "unauthorized");
    CHECK(error->retryable);
    CHECK(tokenStore.IsAvailable());

    existingLease.reset();
    ConnectionTimeoutTracker retryTimeout(now);
    auto retry = HandleHello(hello, tokenStore, throttle, sessions, /*connection=*/2, retryTimeout, now);
    CHECK_FALSE(retry.closeConnection);
    CHECK(retry.sessionLease.has_value());
    CHECK_FALSE(tokenStore.IsAvailable());
}
