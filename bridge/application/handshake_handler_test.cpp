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

/// Builds a hello envelope with controllable token, message ID, protocol
/// version, offered supported versions, and an optional clientId.
Envelope BuildHelloEnvelope(const std::string& token, std::string messageId = "message-hello-1",
                             std::int64_t protocolVersion = 0,
                             std::vector<std::int64_t> supportedProtocolVersions = {1},
                             std::optional<std::string> clientId = std::nullopt) {
    boost::json::array versions;
    for (std::int64_t version : supportedProtocolVersions) {
        versions.push_back(version);
    }
    boost::json::object payload;
    payload["endpoint"] = "client";
    payload["supportedProtocolVersions"] = versions;
    if (clientId.has_value()) {
        payload["clientId"] = *clientId;
    }
    boost::json::object auth;
    auth["method"] = "one_time_local_token";
    auth["token"] = token;
    payload["auth"] = auth;

    return Envelope{
        .protocolVersion = protocolVersion,
        .messageType = "hello",
        .messageId = std::move(messageId),
        .sessionId = std::nullopt,
        .correlationId = std::nullopt,
        .payload = payload,
    };
}

}  // namespace

TEST_CASE("HandleHello accepts a valid token and supported version", "[application][handshake_handler]") {
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
    CHECK(ack->selectedProtocolVersion == 1);
    CHECK_FALSE(ack->clientIdentityKind.has_value());

    // v1 is unchanged by v2's introduction: protocolVersion stays 0 on
    // hello_ack itself, and no envelope identity field is populated.
    CHECK(result.negotiatedProtocolVersion == 1);
    CHECK(result.response.protocolVersion == 0);
    CHECK_FALSE(result.response.clientId.has_value());
    CHECK_FALSE(result.response.bridgeInstanceId.has_value());
    CHECK_FALSE(result.response.playContextId.has_value());

    // Proves MarkAuthenticated actually ran: the handshake deadline was
    // start+5s (security::kHandshakeTimeout), so without MarkAuthenticated
    // switching to the 60s idle deadline, start+6s would already be timed
    // out.
    CHECK_FALSE(timeout.IsTimedOut(start + std::chrono::seconds(6)));
}

TEST_CASE("HandleHello selects the highest mutually supported version when the client offers v1 and v2",
          "[application][handshake_handler]") {
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    auto hello = BuildHelloEnvelope(kValidHexToken, "message-hello-1", /*protocolVersion=*/0,
                                     /*supportedProtocolVersions=*/{1, 2}, /*clientId=*/std::string("client-1"));
    auto result = HandleHello(hello, tokenStore, throttle, sessions, /*connection=*/1, timeout, now);

    CHECK_FALSE(result.closeConnection);
    REQUIRE(result.sessionLease.has_value());
    CHECK(result.negotiatedProtocolVersion == 2);
    // hello_ack's own envelope uses the negotiated version once it's v2,
    // unlike v1's unchanged "stays 0" convention.
    CHECK(result.response.protocolVersion == 2);
    REQUIRE(result.response.clientId.has_value());
    CHECK(*result.response.clientId == "client-1");
    CHECK_FALSE(result.response.bridgeInstanceId.has_value());
    CHECK_FALSE(result.response.playContextId.has_value());

    auto ack = dovahlink::protocol::DecodeHelloAckPayload(result.response.payload);
    REQUIRE(ack.has_value());
    CHECK(ack->selectedProtocolVersion == 2);
    REQUIRE(ack->clientIdentityKind.has_value());
    CHECK(*ack->clientIdentityKind == "unpaired");
}

TEST_CASE("HandleHello stamps the supplied bridgeInstanceId onto a v2 response",
          "[application][handshake_handler]") {
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);
    ActivePlayContext activePlayContext;

    auto hello = BuildHelloEnvelope(kValidHexToken, "message-hello-1", /*protocolVersion=*/0,
                                     /*supportedProtocolVersions=*/{1, 2}, /*clientId=*/std::string("client-1"));
    auto result = HandleHello(hello, tokenStore, throttle, sessions, /*connection=*/1, timeout, now,
                              /*bridgeInstanceId=*/std::string("bridge-1"), activePlayContext);

    REQUIRE(result.response.bridgeInstanceId.has_value());
    CHECK(*result.response.bridgeInstanceId == "bridge-1");
    // No context has been begun, so playContextId stays null even though
    // bridgeInstanceId is present.
    CHECK_FALSE(result.response.playContextId.has_value());
}

TEST_CASE("HandleHello stamps the play context already active at connect time onto a v2 response",
          "[application][handshake_handler]") {
    // Proves a reconnect mid-game (or any connection made while a play
    // context is already loaded) reports it immediately in hello_ack,
    // matching protocol/fixtures/v2/connection/hello-ack-active-context.json.
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);
    ActivePlayContext activePlayContext;
    activePlayContext.Begin("context-1");

    auto hello = BuildHelloEnvelope(kValidHexToken, "message-hello-1", /*protocolVersion=*/0,
                                     /*supportedProtocolVersions=*/{1, 2}, /*clientId=*/std::string("client-1"));
    auto result = HandleHello(hello, tokenStore, throttle, sessions, /*connection=*/1, timeout, now,
                              /*bridgeInstanceId=*/std::string("bridge-1"), activePlayContext);

    REQUIRE(result.response.playContextId.has_value());
    CHECK(*result.response.playContextId == "context-1");
}

TEST_CASE("HandleHello selects v2 when the client offers only v2", "[application][handshake_handler]") {
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    auto hello = BuildHelloEnvelope(kValidHexToken, "message-hello-1", /*protocolVersion=*/0,
                                     /*supportedProtocolVersions=*/{2}, /*clientId=*/std::string("client-1"));
    auto result = HandleHello(hello, tokenStore, throttle, sessions, /*connection=*/1, timeout, now);

    CHECK_FALSE(result.closeConnection);
    CHECK(result.negotiatedProtocolVersion == 2);
}

TEST_CASE("HandleHello selects the highest version regardless of the offered array's order",
          "[application][handshake_handler]") {
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    auto hello = BuildHelloEnvelope(kValidHexToken, "message-hello-1", /*protocolVersion=*/0,
                                     /*supportedProtocolVersions=*/{2, 1}, /*clientId=*/std::string("client-1"));
    auto result = HandleHello(hello, tokenStore, throttle, sessions, /*connection=*/1, timeout, now);

    CHECK_FALSE(result.closeConnection);
    CHECK(result.negotiatedProtocolVersion == 2);
}

TEST_CASE("HandleHello rejects an empty supportedProtocolVersions offer as unsupported_version",
          "[application][handshake_handler]") {
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    auto hello = BuildHelloEnvelope(kValidHexToken, "message-hello-1", /*protocolVersion=*/0,
                                     /*supportedProtocolVersions=*/{});
    auto result = HandleHello(hello, tokenStore, throttle, sessions, /*connection=*/1, timeout, now);

    CHECK(result.closeConnection);
    CHECK(result.negotiatedProtocolVersion == 0);
    auto error = dovahlink::protocol::DecodeErrorPayload(result.response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "unsupported_version");
}

TEST_CASE("HandleHello does not echo a v1 hello's clientId onto the response",
          "[application][handshake_handler]") {
    // A v1-negotiated connection ignores clientId even if the client sent
    // one anyway; only a v2 negotiation echoes it.
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    auto hello = BuildHelloEnvelope(kValidHexToken, "message-hello-1", /*protocolVersion=*/0,
                                     /*supportedProtocolVersions=*/{1}, /*clientId=*/std::string("client-1"));
    auto result = HandleHello(hello, tokenStore, throttle, sessions, /*connection=*/1, timeout, now);

    CHECK(result.negotiatedProtocolVersion == 1);
    CHECK_FALSE(result.response.clientId.has_value());
}

TEST_CASE("HandleHello rejects a v2 offer that omits clientId", "[application][handshake_handler]") {
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    auto hello = BuildHelloEnvelope(kValidHexToken, "message-hello-1", /*protocolVersion=*/0,
                                     /*supportedProtocolVersions=*/{1, 2});
    auto result = HandleHello(hello, tokenStore, throttle, sessions, /*connection=*/1, timeout, now);

    CHECK(result.closeConnection);
    CHECK(result.negotiatedProtocolVersion == 0);
    auto error = dovahlink::protocol::DecodeErrorPayload(result.response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "malformed_message");
    // The token must not have been spent, and the failed-token throttle must
    // not have counted this rejection: it happens before either check runs.
    CHECK(tokenStore.IsAvailable());
    CHECK_FALSE(throttle.IsBlocked(now));
}

TEST_CASE("HandleHello rejects a non-zero protocolVersion as malformed", "[application][handshake_handler]") {
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    auto hello = BuildHelloEnvelope(kValidHexToken, "message-hello-1", /*protocolVersion=*/1);
    auto result = HandleHello(hello, tokenStore, throttle, sessions, 1, timeout, now);

    CHECK(result.closeConnection);
    CHECK(result.response.messageType == "error");
    CHECK_FALSE(result.response.sessionId.has_value());
    auto error = dovahlink::protocol::DecodeErrorPayload(result.response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "malformed_message");
}

TEST_CASE("HandleHello rejects a structurally malformed payload", "[application][handshake_handler]") {
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    Envelope hello{
        .protocolVersion = 0,
        .messageType = "hello",
        .messageId = "message-hello-1",
        .sessionId = std::nullopt,
        .correlationId = std::nullopt,
        .payload = boost::json::parse(R"({"endpoint": "client"})").get_object(),  // missing required fields
    };
    auto result = HandleHello(hello, tokenStore, throttle, sessions, 1, timeout, now);

    CHECK(result.closeConnection);
    auto error = dovahlink::protocol::DecodeErrorPayload(result.response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "malformed_message");
}

TEST_CASE("HandleHello rejects a hello that does not include the supported version",
          "[application][handshake_handler]") {
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    Envelope hello{
        .protocolVersion = 0,
        .messageType = "hello",
        .messageId = "message-hello-1",
        .sessionId = std::nullopt,
        .correlationId = std::nullopt,
        // 3 is offered by neither this test's client nor kSupportedProtocolVersions.
        .payload = boost::json::parse(R"({"endpoint": "client", "supportedProtocolVersions": [3],
            "auth": {"method": "one_time_local_token", "token": "abcd"}})")
                       .get_object(),
    };
    auto result = HandleHello(hello, tokenStore, throttle, sessions, 1, timeout, now);

    CHECK(result.closeConnection);
    CHECK(result.negotiatedProtocolVersion == 0);
    auto error = dovahlink::protocol::DecodeErrorPayload(result.response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "unsupported_version");
    // The token must not have been spent checking a version-incompatible client.
    CHECK(tokenStore.IsAvailable());
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
