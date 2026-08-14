#include "application/handshake_handler.hpp"

#include "protocol/messages.hpp"
#include "security/hex.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/object.hpp>
#include <boost/json/parse.hpp>

#include <chrono>
#include <cstdint>
#include <string>
#include <vector>

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

/// Builds a hello envelope with controllable token, message ID, and version fields.
Envelope BuildHelloEnvelope(const std::string& token, std::string messageId = "message-hello-1",
                             std::int64_t protocolVersion = 0) {
    boost::json::object payload =
        boost::json::parse(R"({"endpoint": "client", "supportedProtocolVersions": [1],
            "auth": {"method": "one_time_local_token", "token": ")" +
                            token + R"("}})")
            .get_object();
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

    // Proves MarkAuthenticated actually ran: the handshake deadline was
    // start+5s (security::kHandshakeTimeout), so without MarkAuthenticated
    // switching to the 60s idle deadline, start+6s would already be timed
    // out.
    CHECK_FALSE(timeout.IsTimedOut(start + std::chrono::seconds(6)));
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
        .payload = boost::json::parse(R"({"endpoint": "client", "supportedProtocolVersions": [2],
            "auth": {"method": "one_time_local_token", "token": "abcd"}})")
                       .get_object(),
    };
    auto result = HandleHello(hello, tokenStore, throttle, sessions, 1, timeout, now);

    CHECK(result.closeConnection);
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
