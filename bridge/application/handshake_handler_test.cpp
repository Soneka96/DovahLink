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

std::vector<std::uint8_t> ValidTokenBytes() { return *DecodeHex(kValidHexToken); }

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
    CHECK(result.response.messageType == "hello_ack");
    REQUIRE(result.response.sessionId.has_value());
    CHECK_FALSE(result.response.sessionId->empty());
    REQUIRE(result.response.correlationId.has_value());
    CHECK(*result.response.correlationId == "message-hello-1");
    CHECK_FALSE(result.response.messageId.empty());

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
    // reported as unauthenticated, and only the 6th is rate_limited.
    dovahlink::protocol::ErrorPayload lastError;
    for (int attempt = 0; attempt < 6; ++attempt) {
        TokenStore tokenStore(ValidTokenBytes());
        SessionManager sessions;
        ConnectionTimeoutTracker timeout(now);
        auto hello = BuildHelloEnvelope(kWrongHexToken);
        auto result = HandleHello(hello, tokenStore, throttle, sessions, 1, timeout, now);
        auto error = dovahlink::protocol::DecodeErrorPayload(result.response.payload);
        REQUIRE(error.has_value());
        lastError = *error;
    }

    CHECK(lastError.code == "rate_limited");
    CHECK(lastError.retryable);
}

TEST_CASE("HandleHello rejects a second connection while a session is already active",
          "[application][handshake_handler]") {
    SessionManager sessions;
    REQUIRE(sessions.TryCreateSession(/*connection=*/1, "existing-session"));

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
    // Documented, accepted Phase 1 limitation: the atomic one-time token is
    // consumed even though the session could not be created, since
    // TryConsume cannot "peek" at session capacity before committing.
    CHECK_FALSE(tokenStore.IsAvailable());
}
