#include "application/active_play_context_reader.hpp"
#include "application/handshake_handler.hpp"

#include "application/application_test_support.hpp"
#include "protocol/messages.hpp"
#include "security/hex.hpp"
#include "security/test_token.hpp"

#include <catch2/catch_test_macros.hpp>
#include <gmock/gmock.h>

#include <boost/json/parse.hpp>

#include <chrono>
#include <cstdint>
#include <optional>
#include <stdexcept>
#include <string>
#include <vector>

using dovahlink::application::ConnectionTimeoutTracker;
using dovahlink::application::HandleHello;
using dovahlink::application::IActivePlayContextReader;
using dovahlink::application::SessionAuthMethod;
using dovahlink::application::SessionManager;
using dovahlink::application::SessionTrustTier;
using dovahlink::application::TrustLossAfterAdmission;
using dovahlink::application::test_support::BuildHelloEnvelope;
using dovahlink::application::test_support::MockActivePlayContext;
using dovahlink::protocol::Envelope;
using dovahlink::security::BlockOutcome;
using dovahlink::security::DecodeHex;
using dovahlink::security::FailedTokenThrottle;
using dovahlink::security::ITrustStorePersistence;
using dovahlink::security::kValidHexToken;
using dovahlink::security::TokenStore;
using dovahlink::security::TrustStore;
using dovahlink::security::TrustStoreSnapshot;
using testing::StrictMock;

namespace {

///  `ITrustStorePersistence` double that always loads an empty snapshot -- for
///  tests that only exercise the one_time_local_token/unpaired auth paths and
///  never actually touch the trust store.
class EmptyPersistence : public ITrustStorePersistence {
  public:
    ///  Always reports a valid, empty snapshot.
    std::optional<TrustStoreSnapshot> Load() override {
        return TrustStoreSnapshot{};
    }

    ///  Always succeeds without recording anything.
    bool Save(const TrustStoreSnapshot&) override { return true; }
};

//  32 bytes (64 hex characters), matching the production token shape
//  (security::kTokenBytes).
constexpr const char* kWrongHexToken =
    "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";

///  Decodes the canonical valid test token into the production byte format.
std::vector<std::uint8_t> ValidTokenBytes() {
    return *DecodeHex(kValidHexToken);
}

///  Builds a hello envelope using the bootstrap `unpaired` auth method (no
///  `auth.token` field).
Envelope BuildUnpairedHelloEnvelope(std::string messageId = "message-hello-1",
                                    std::string clientId = "client-1") {
    return dovahlink::application::test_support::BuildHelloEnvelope(
        std::nullopt, std::move(messageId), std::move(clientId), "unpaired");
}

///  Builds a hello envelope using the `trusted_device_credential` auth method.
Envelope
BuildTrustedCredentialHelloEnvelope(const std::string& credential,
                                    std::string messageId = "message-hello-1",
                                    std::string clientId = "client-1") {
    return dovahlink::application::test_support::BuildHelloEnvelope(
        std::string(credential), std::move(messageId), std::move(clientId),
        "trusted_device_credential");
}

///  GoogleMock trust-store double used to prove rate limiting happens before
///  credential validation.
class MockTrustStore : public dovahlink::security::ITrustStore {
  public:
    MOCK_METHOD(bool, WasCorruptOnLoad, (), (const, noexcept, override));
    MOCK_METHOD(dovahlink::security::TrustMutationGeneration,
                CurrentMutationGeneration, (const std::string&), (override));
    MOCK_METHOD(std::optional<dovahlink::security::KnownDeviceRecord>, Query,
                (const std::string&), (override));
    MOCK_METHOD(std::vector<dovahlink::security::KnownDeviceRecord>, ListTrusted,
                (), (override));
    MOCK_METHOD(std::vector<dovahlink::security::KnownDeviceRecord>, ListAll,
                (), (override));
    MOCK_METHOD(std::optional<dovahlink::security::KnownDeviceRecord>,
                FindByShortId, (std::string_view), (override));
    MOCK_METHOD(bool, IsRevoked, (const std::string&), (override));
    MOCK_METHOD(bool, IsBlocked, (const std::string&), (override));
    MOCK_METHOD(bool, Authenticate,
                (const std::string&, const std::vector<std::uint8_t>&),
                (override));
    MOCK_METHOD(std::optional<dovahlink::security::KnownDeviceRecord>, Persist,
                (std::string, std::vector<std::uint8_t>,
                 std::optional<std::string>),
                (override));
    MOCK_METHOD(std::optional<dovahlink::security::KnownDeviceRecord>,
                PersistIfGeneration,
                (dovahlink::security::TrustMutationGeneration, std::string,
                 std::vector<std::uint8_t>, std::optional<std::string>),
                (override));
    MOCK_METHOD(bool, Revoke, (const std::string&), (override));
    MOCK_METHOD(dovahlink::security::BlockOutcome, Block,
                (const std::string&), (override));
    MOCK_METHOD(dovahlink::security::UnblockOutcome, Unblock,
                (const std::string&), (override));
    MOCK_METHOD(dovahlink::security::ForgetOutcome, Forget,
                (const std::string&), (override));
    MOCK_METHOD(bool, Reset, (), (override));
    MOCK_METHOD(dovahlink::security::RenameOutcome, Rename,
                (const std::string&, std::string), (override));
    MOCK_METHOD(bool, ResetTrust, (), (override));
};

} //  namespace

TEST_CASE("HandleHello accepts a valid token and hello",
          "[application][handshake_handler]") {
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    FailedTokenThrottle credentialThrottle;
    SessionManager sessions;
    auto start = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(start);

    auto hello = BuildHelloEnvelope(kValidHexToken);
    auto result = HandleHello(
        hello, tokenStore, throttle, trustStore, credentialThrottle, sessions,
        /*connection=*/1, timeout, start + std::chrono::seconds(1));

    CHECK_FALSE(result.closeConnection);
    REQUIRE(result.sessionLease.has_value());
    CHECK(result.response.messageType == "hello_ack");
    REQUIRE(result.response.sessionId.has_value());
    CHECK_FALSE(result.response.sessionId->empty());
    REQUIRE(result.response.correlationId.has_value());
    CHECK(*result.response.correlationId == "message-hello-1");
    CHECK_FALSE(result.response.messageId.empty());
    CHECK(sessions.IsValidForConnection(*result.response.sessionId,
                                        /*connection=*/1));
    //  The client identity bound to a session is now session-owned state,
    //  derived from SessionManager rather than a repeated envelope field.
    auto sessionClientId = sessions.ClientIdForConnection(/*connection=*/1);
    REQUIRE(sessionClientId.has_value());
    CHECK(*sessionClientId == "client-1");
    //  Developer authentication (one_time_local_token) is always kFull trust
    //  tier, even though it reports clientIdentityKind "unpaired" below.
    CHECK(sessions.IsFullyTrusted(/*connection=*/1));

    auto ack =
        dovahlink::protocol::DecodeHelloAckPayload(result.response.payload);
    REQUIRE(ack.has_value());
    CHECK(
        ack->bridgeVersion ==
        "0.0.0"); //  HandleHello's default bridgeVersion for callers that omit it.
    CHECK(ack->clientIdentityKind == "unpaired");

    //  Identity fields are always echoed now: clientId from the hello,
    //  bridgeInstanceId only when the caller supplied one (not here).
    REQUIRE(result.response.clientId.has_value());
    CHECK(*result.response.clientId == "client-1");
    CHECK_FALSE(result.response.bridgeInstanceId.has_value());
    CHECK_FALSE(result.response.playContextId.has_value());

    //  Proves MarkAuthenticated actually ran: the handshake deadline was
    //  start+5s (security::kHandshakeTimeout), so without MarkAuthenticated
    //  switching to the 60s idle deadline, start+6s would already be timed
    //  out.
    CHECK_FALSE(timeout.IsTimedOut(start + std::chrono::seconds(6)));
}

TEST_CASE("HandleHello stamps the supplied bridgeInstanceId onto the response",
          "[application][handshake_handler]") {
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    FailedTokenThrottle credentialThrottle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);
    StrictMock<MockActivePlayContext> activePlayContext;
    EXPECT_CALL(activePlayContext, CurrentPlayContextId())
        .WillOnce(testing::Return(std::optional<std::string>{}));

    auto hello = BuildHelloEnvelope(kValidHexToken);
    auto result = HandleHello(
        hello, tokenStore, throttle, trustStore, credentialThrottle, sessions,
        /*connection=*/1, timeout, now,
        /*bridgeInstanceId=*/std::string("bridge-1"), activePlayContext);

    REQUIRE(result.response.bridgeInstanceId.has_value());
    CHECK(*result.response.bridgeInstanceId == "bridge-1");
    //  No context has been begun, so playContextId stays null even though
    //  bridgeInstanceId is present.
    CHECK_FALSE(result.response.playContextId.has_value());
}

TEST_CASE("HandleHello stamps the play context already active at connect time "
          "onto the response",
          "[application][handshake_handler]") {
    //  Proves a reconnect mid-game (or any connection made while a play
    //  context is already loaded) reports it immediately in hello_ack,
    //  matching protocol/fixtures/connection/hello-ack-active-context.json.
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    FailedTokenThrottle credentialThrottle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);
    StrictMock<MockActivePlayContext> activePlayContext;
    EXPECT_CALL(activePlayContext, CurrentPlayContextId())
        .WillOnce(testing::Return(std::optional<std::string>{"context-1"}));

    auto hello = BuildHelloEnvelope(kValidHexToken);
    auto result = HandleHello(
        hello, tokenStore, throttle, trustStore, credentialThrottle, sessions,
        /*connection=*/1, timeout, now,
        /*bridgeInstanceId=*/std::string("bridge-1"), activePlayContext);

    REQUIRE(result.response.playContextId.has_value());
    CHECK(*result.response.playContextId == "context-1");
}

TEST_CASE("HandleHello uses the supplied bridgeVersion in hello_ack",
          "[application][handshake_handler]") {
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    FailedTokenThrottle credentialThrottle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);
    auto hello = BuildHelloEnvelope(kValidHexToken);
    auto result = HandleHello(
        hello, tokenStore, throttle, trustStore, credentialThrottle, sessions,
        /*connection=*/1, timeout, now,
        /*bridgeInstanceId=*/std::nullopt, nullptr,
        /*bridgeVersion=*/"0.1.0");

    auto ack =
        dovahlink::protocol::DecodeHelloAckPayload(result.response.payload);
    REQUIRE(ack.has_value());
    CHECK(ack->bridgeVersion == "0.1.0");
}

TEST_CASE("HandleHello rejects a hello that omits clientId",
          "[application][handshake_handler]") {
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    FailedTokenThrottle credentialThrottle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    auto hello = BuildHelloEnvelope(kValidHexToken, "message-hello-1",
                                    /*clientId=*/std::nullopt);
    auto result =
        HandleHello(hello, tokenStore, throttle, trustStore, credentialThrottle,
                    sessions, /*connection=*/1, timeout, now);

    CHECK(result.closeConnection);
    auto error = dovahlink::protocol::DecodeErrorPayload(result.response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "malformed_message");
    //  The token must not have been spent, and the failed-token throttle must
    //  not have counted this rejection: it happens before either check runs.
    CHECK(tokenStore.IsAvailable());
    CHECK_FALSE(throttle.IsBlocked(now));
}

TEST_CASE("HandleHello rejects a structurally malformed payload",
          "[application][handshake_handler]") {
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    FailedTokenThrottle credentialThrottle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);
    Envelope hello{
        .messageType = "hello",
        .messageId = "message-hello-1",
        .sessionId = std::nullopt,
        .correlationId = std::nullopt,
        .payload = boost::json::parse(R"({"endpoint": "client"})")
                       .get_object(), //  missing required fields
    };
    //  This is the earliest Fail() call site in HandleHello -- runs before
    //  the token or session checks below -- so it exercises the
    //  bridgeInstanceId stamping at the structurally-first possible point.
    auto result = HandleHello(hello, tokenStore, throttle, trustStore,
                              credentialThrottle, sessions, 1, timeout, now,
                              /*bridgeInstanceId=*/std::string("bridge-1"));

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
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    FailedTokenThrottle credentialThrottle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    auto hello = BuildHelloEnvelope(kWrongHexToken);
    auto result = HandleHello(hello, tokenStore, throttle, trustStore,
                              credentialThrottle, sessions, 1, timeout, now);

    CHECK(result.closeConnection);
    auto error = dovahlink::protocol::DecodeErrorPayload(result.response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "unauthenticated");
    CHECK(tokenStore.IsAvailable());
}

TEST_CASE("HandleHello stamps a supplied bridgeInstanceId onto a "
          "rejected-token failure response",
          "[application][handshake_handler]") {
    //  The bridge's own identity is known before any hello arrives (it is
    //  generated once at startup), so a rejection during the handshake itself
    //  -- unlike the narrow set of failures where identity generation itself
    //  failed -- should still carry it.
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    FailedTokenThrottle credentialThrottle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);
    auto hello = BuildHelloEnvelope(kWrongHexToken);
    auto result = HandleHello(
        hello, tokenStore, throttle, trustStore, credentialThrottle, sessions,
        /*connection=*/1, timeout, now,
        /*bridgeInstanceId=*/std::string("bridge-1"));

    CHECK(result.closeConnection);
    auto error = dovahlink::protocol::DecodeErrorPayload(result.response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "unauthenticated");
    REQUIRE(result.response.bridgeInstanceId.has_value());
    CHECK(*result.response.bridgeInstanceId == "bridge-1");
}

TEST_CASE(
    "HandleHello rejects a non-hex presented token the same way as a wrong one",
    "[application][handshake_handler]") {
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    FailedTokenThrottle credentialThrottle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    auto hello = BuildHelloEnvelope("not-valid-hex!!");
    auto result = HandleHello(hello, tokenStore, throttle, trustStore,
                              credentialThrottle, sessions, 1, timeout, now);

    CHECK(result.closeConnection);
    auto error = dovahlink::protocol::DecodeErrorPayload(result.response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "unauthenticated");
    CHECK(tokenStore.IsAvailable());
}

TEST_CASE("HandleHello admits a restricted session for an unpaired hello",
          "[application][handshake_handler]") {
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    FailedTokenThrottle credentialThrottle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    auto hello = BuildUnpairedHelloEnvelope();
    auto result = HandleHello(hello, tokenStore, throttle, trustStore,
                              credentialThrottle, sessions, 1, timeout, now);

    CHECK_FALSE(result.closeConnection);
    REQUIRE(result.sessionLease.has_value());
    CHECK_FALSE(sessions.IsFullyTrusted(/*connection=*/1));

    auto ack =
        dovahlink::protocol::DecodeHelloAckPayload(result.response.payload);
    REQUIRE(ack.has_value());
    CHECK(ack->clientIdentityKind == "unpaired");
    //  No credential exists yet to spend or throttle for this bootstrap method.
    CHECK(tokenStore.IsAvailable());
    CHECK_FALSE(throttle.IsBlocked(now));
    CHECK_FALSE(credentialThrottle.IsBlocked(now));
}

TEST_CASE("HandleHello accepts a trusted_device_credential hello matching a "
          "persisted credential",
          "[application][handshake_handler]") {
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    std::vector<std::uint8_t> credentialBytes{1, 2, 3, 4};
    REQUIRE(trustStore.Persist("client-1", credentialBytes, std::nullopt)
                .has_value());

    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    FailedTokenThrottle credentialThrottle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    auto hello = BuildTrustedCredentialHelloEnvelope(
        dovahlink::security::EncodeHex(credentialBytes));
    auto result = HandleHello(hello, tokenStore, throttle, trustStore,
                              credentialThrottle, sessions, 1, timeout, now);

    CHECK_FALSE(result.closeConnection);
    REQUIRE(result.sessionLease.has_value());
    CHECK(sessions.IsFullyTrusted(/*connection=*/1));

    auto ack =
        dovahlink::protocol::DecodeHelloAckPayload(result.response.payload);
    REQUIRE(ack.has_value());
    CHECK(ack->clientIdentityKind == "paired");

    //  Authenticate is read-only: the same credential still authenticates
    //  afterward, proving HandleHello did not mutate or consume trustStore's state
    //  on success.
    CHECK(trustStore.Authenticate("client-1", credentialBytes));
}

TEST_CASE("HandleHello rejects a trusted_device_credential hello with a "
          "non-hex credential",
          "[application][handshake_handler]") {
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    REQUIRE(trustStore
                .Persist("client-1", std::vector<std::uint8_t>{1, 2, 3, 4},
                         std::nullopt)
                .has_value());

    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    FailedTokenThrottle credentialThrottle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    auto hello = BuildTrustedCredentialHelloEnvelope("not-valid-hex!!");
    auto result = HandleHello(hello, tokenStore, throttle, trustStore,
                              credentialThrottle, sessions, 1, timeout, now);

    CHECK(result.closeConnection);
    auto error = dovahlink::protocol::DecodeErrorPayload(result.response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "unauthenticated");
}

TEST_CASE("HandleHello rejects a trusted_device_credential hello with a wrong "
          "credential",
          "[application][handshake_handler]") {
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    REQUIRE(trustStore
                .Persist("client-1", std::vector<std::uint8_t>{1, 2, 3, 4},
                         std::nullopt)
                .has_value());

    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    FailedTokenThrottle credentialThrottle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    auto hello = BuildTrustedCredentialHelloEnvelope(
        dovahlink::security::EncodeHex(std::vector<std::uint8_t>{9, 9, 9, 9}));
    auto result = HandleHello(hello, tokenStore, throttle, trustStore,
                              credentialThrottle, sessions, 1, timeout, now);

    CHECK(result.closeConnection);
    auto error = dovahlink::protocol::DecodeErrorPayload(result.response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "unauthenticated");
    //  Guessing a device credential must not spend or block the unrelated
    //  one-time-token throttle.
    CHECK_FALSE(throttle.IsBlocked(now));
}

TEST_CASE("HandleHello reports revoked for a trusted_device_credential hello "
          "from a revoked clientId",
          "[application][handshake_handler]") {
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    std::vector<std::uint8_t> credentialBytes{1, 2, 3, 4};
    REQUIRE(trustStore.Persist("client-1", credentialBytes, std::nullopt)
                .has_value());
    REQUIRE(trustStore.Revoke("client-1"));

    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    FailedTokenThrottle credentialThrottle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    auto hello = BuildTrustedCredentialHelloEnvelope(
        dovahlink::security::EncodeHex(credentialBytes));
    auto result = HandleHello(hello, tokenStore, throttle, trustStore,
                              credentialThrottle, sessions, 1, timeout, now);

    CHECK(result.closeConnection);
    auto error = dovahlink::protocol::DecodeErrorPayload(result.response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "revoked");
    CHECK_FALSE(error->retryable);
    //  Revocation is unrelated to the one-time-token flow; it must not spend or
    //  block that throttle.
    CHECK_FALSE(throttle.IsBlocked(now));
}

TEST_CASE("HandleHello reports revoked for a revoked clientId even when a "
          "non-matching credential is presented",
          "[application][handshake_handler]") {
    //  TrustStore::Revoke erases the stored credential entirely
    //  (bridge/security/trust_store.cpp), so a real revoked device reconnecting
    //  presents whatever old credential it still has locally -- never bytes that
    //  happen to match a live record. The revoked outcome must not depend on
    //  guessing the original credential.
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    REQUIRE(trustStore
                .Persist("client-1", std::vector<std::uint8_t>{1, 2, 3, 4},
                         std::nullopt)
                .has_value());
    REQUIRE(trustStore.Revoke("client-1"));

    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    FailedTokenThrottle credentialThrottle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    auto hello = BuildTrustedCredentialHelloEnvelope(
        dovahlink::security::EncodeHex(std::vector<std::uint8_t>{9, 9, 9, 9}));
    auto result = HandleHello(hello, tokenStore, throttle, trustStore,
                              credentialThrottle, sessions, 1, timeout, now);

    CHECK(result.closeConnection);
    auto error = dovahlink::protocol::DecodeErrorPayload(result.response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "revoked");
}

TEST_CASE("HandleHello reports revoked for a revoked clientId even when the "
          "credential is malformed",
          "[application][handshake_handler]") {
    //  IsRevoked keys only on clientId, so it must still win over the
    //  "structurally invalid credential" path (a non-hex credential cannot be
    //  decoded to bytes at all).
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    REQUIRE(trustStore
                .Persist("client-1", std::vector<std::uint8_t>{1, 2, 3, 4},
                         std::nullopt)
                .has_value());
    REQUIRE(trustStore.Revoke("client-1"));

    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    FailedTokenThrottle credentialThrottle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    auto hello = BuildTrustedCredentialHelloEnvelope("not-valid-hex");
    auto result = HandleHello(hello, tokenStore, throttle, trustStore,
                              credentialThrottle, sessions, 1, timeout, now);

    CHECK(result.closeConnection);
    auto error = dovahlink::protocol::DecodeErrorPayload(result.response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "revoked");
}

TEST_CASE("HandleHello rejects an unpaired hello from a blocked clientId",
          "[application][handshake_handler]") {
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    REQUIRE(trustStore
                .Persist("client-1", std::vector<std::uint8_t>{1, 2, 3, 4},
                         std::string("My PC"))
                .has_value());
    REQUIRE(trustStore.Block("client-1") == BlockOutcome::kBlocked);

    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    FailedTokenThrottle credentialThrottle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    auto hello = BuildUnpairedHelloEnvelope();
    auto result = HandleHello(hello, tokenStore, throttle, trustStore,
                              credentialThrottle, sessions, 1, timeout, now);

    CHECK(result.closeConnection);
    CHECK_FALSE(result.sessionLease.has_value());
    auto error = dovahlink::protocol::DecodeErrorPayload(result.response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "blocked");
    CHECK_FALSE(error->retryable);
    CHECK_FALSE(error->message.find("My PC") != std::string::npos);
    //  The check runs before any auth-method-specific work, so neither throttle is
    //  touched and the one-time token stays available.
    CHECK_FALSE(throttle.IsBlocked(now));
    CHECK_FALSE(credentialThrottle.IsBlocked(now));
    CHECK(tokenStore.IsAvailable());
}

TEST_CASE("HandleHello rejects a trusted_device_credential hello from a "
          "blocked clientId, not revoked",
          "[application][handshake_handler]") {
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    std::vector<std::uint8_t> credentialBytes{1, 2, 3, 4};
    REQUIRE(trustStore.Persist("client-1", credentialBytes, std::nullopt)
                .has_value());
    REQUIRE(trustStore.Block("client-1") == BlockOutcome::kBlocked);

    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    FailedTokenThrottle credentialThrottle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    //  The device's real credential was destroyed by Block; presenting it back
    //  proves blocked wins even against a credential that used to be valid, not
    //  just an obviously-wrong guess.
    auto hello = BuildTrustedCredentialHelloEnvelope(
        dovahlink::security::EncodeHex(credentialBytes));
    auto result = HandleHello(hello, tokenStore, throttle, trustStore,
                              credentialThrottle, sessions, 1, timeout, now);

    CHECK(result.closeConnection);
    CHECK_FALSE(result.sessionLease.has_value());
    auto error = dovahlink::protocol::DecodeErrorPayload(result.response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "blocked");
    CHECK_FALSE(error->retryable);
    CHECK_FALSE(throttle.IsBlocked(now));
    CHECK_FALSE(credentialThrottle.IsBlocked(now));
    CHECK(tokenStore.IsAvailable());
}

TEST_CASE("HandleHello rejects a blocked clientId's trusted_device_credential "
          "hello even with a "
          "structurally malformed credential",
          "[application][handshake_handler]") {
    //  Proves the blocked gate runs before hex-decoding the presented credential,
    //  not just before a well-formed-but-wrong one -- mirroring the existing
    //  revoked-vs-malformed-credential proof.
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    REQUIRE(trustStore
                .Persist("client-1", std::vector<std::uint8_t>{1, 2, 3, 4},
                         std::nullopt)
                .has_value());
    REQUIRE(trustStore.Block("client-1") == BlockOutcome::kBlocked);

    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    FailedTokenThrottle credentialThrottle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    auto hello = BuildTrustedCredentialHelloEnvelope("not-valid-hex!!");
    auto result = HandleHello(hello, tokenStore, throttle, trustStore,
                              credentialThrottle, sessions, 1, timeout, now);

    CHECK(result.closeConnection);
    auto error = dovahlink::protocol::DecodeErrorPayload(result.response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "blocked");
}

TEST_CASE("HandleHello does not reject a hello from a different, never-blocked "
          "clientId",
          "[application][handshake_handler]") {
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    REQUIRE(trustStore
                .Persist("client-1", std::vector<std::uint8_t>{1, 2, 3, 4},
                         std::nullopt)
                .has_value());
    REQUIRE(trustStore.Block("client-1") == BlockOutcome::kBlocked);

    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    FailedTokenThrottle credentialThrottle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    auto hello = BuildUnpairedHelloEnvelope("message-hello-1", "client-2");
    auto result = HandleHello(hello, tokenStore, throttle, trustStore,
                              credentialThrottle, sessions, 1, timeout, now);

    CHECK_FALSE(result.closeConnection);
    REQUIRE(result.sessionLease.has_value());
}

TEST_CASE("HandleHello's one_time_local_token path is unaffected by a blocked "
          "clientId",
          "[application][handshake_handler]") {
    //  Developer-token authentication stays a separate provider, never redefined
    //  as a paired device by Known Device blocking (security.md's "Developer
    //  authentication").
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    REQUIRE(trustStore
                .Persist("client-1", std::vector<std::uint8_t>{1, 2, 3, 4},
                         std::nullopt)
                .has_value());
    REQUIRE(trustStore.Block("client-1") == BlockOutcome::kBlocked);
    FailedTokenThrottle credentialThrottle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    auto hello = BuildHelloEnvelope(kValidHexToken, "message-hello-1",
                                    std::string("client-1"));
    auto result = HandleHello(hello, tokenStore, throttle, trustStore,
                              credentialThrottle, sessions,
                              /*connection=*/1, timeout, now);

    CHECK_FALSE(result.closeConnection);
    REQUIRE(result.sessionLease.has_value());
    CHECK(sessions.IsFullyTrusted(/*connection=*/1));
}

TEST_CASE("HandleHello rejects a trusted_device_credential hello for an "
          "unknown clientId",
          "[application][handshake_handler]") {
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);

    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    FailedTokenThrottle credentialThrottle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    auto hello = BuildTrustedCredentialHelloEnvelope(
        dovahlink::security::EncodeHex(std::vector<std::uint8_t>{1, 2, 3, 4}),
        "message-hello-1", "never-paired");
    auto result = HandleHello(hello, tokenStore, throttle, trustStore,
                              credentialThrottle, sessions, 1, timeout, now);

    CHECK(result.closeConnection);
    auto error = dovahlink::protocol::DecodeErrorPayload(result.response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "unauthenticated");
}

TEST_CASE("HandleHello reports rate_limited for the credential throttle "
          "independently of the token throttle",
          "[application][handshake_handler]") {
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    REQUIRE(trustStore
                .Persist("client-1", std::vector<std::uint8_t>{1, 2, 3, 4},
                         std::nullopt)
                .has_value());

    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    FailedTokenThrottle credentialThrottle; //  shared across every attempt below,
                                            //  like a real deployment.
    auto now = std::chrono::steady_clock::now();

    for (int attempt = 0; attempt < 5; ++attempt) {
        SessionManager sessions;
        ConnectionTimeoutTracker timeout(now);
        auto hello = BuildTrustedCredentialHelloEnvelope(
            dovahlink::security::EncodeHex(std::vector<std::uint8_t>{9, 9, 9, 9}));
        auto result = HandleHello(hello, tokenStore, throttle, trustStore,
                                  credentialThrottle, sessions, 1, timeout, now);
        CHECK(result.closeConnection);
    }

    SessionManager sessions;
    ConnectionTimeoutTracker timeout(now);
    auto blocked = HandleHello(
        BuildTrustedCredentialHelloEnvelope(dovahlink::security::EncodeHex(
            std::vector<std::uint8_t>{1, 2, 3, 4})),
        tokenStore, throttle, trustStore, credentialThrottle, sessions,
        /*connection=*/1, timeout, now);

    auto blockedError =
        dovahlink::protocol::DecodeErrorPayload(blocked.response.payload);
    REQUIRE(blockedError.has_value());
    CHECK(blockedError->code == "rate_limited");
    CHECK(blockedError->retryable);
    //  The one-time-token throttle never saw a single attempt.
    CHECK_FALSE(throttle.IsBlocked(now));
}

TEST_CASE("credential-throttle rate-limited handshakes do not extend the "
          "failed-attempt window",
          "[application][handshake_handler]") {
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    REQUIRE(trustStore
                .Persist("client-1", std::vector<std::uint8_t>{1, 2, 3, 4},
                         std::nullopt)
                .has_value());

    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    FailedTokenThrottle credentialThrottle;
    auto t0 = std::chrono::steady_clock::now();
    for (int attempt = 0; attempt < 5; ++attempt) {
        credentialThrottle.RecordFailure(t0);
    }

    for (int attempt = 0; attempt < 5; ++attempt) {
        SessionManager blockedSessions;
        ConnectionTimeoutTracker blockedTimeout(t0);
        auto hello = BuildTrustedCredentialHelloEnvelope(
            dovahlink::security::EncodeHex(std::vector<std::uint8_t>{1, 2, 3, 4}),
            "message-blocked-" + std::to_string(attempt));
        auto blocked = HandleHello(hello, tokenStore, throttle, trustStore,
                                   credentialThrottle, blockedSessions,
                                   /*connection=*/1, blockedTimeout,
                                   t0 + std::chrono::seconds(59));
        auto error =
            dovahlink::protocol::DecodeErrorPayload(blocked.response.payload);
        REQUIRE(error.has_value());
        CHECK(error->code == "rate_limited");
    }

    SessionManager sessions;
    ConnectionTimeoutTracker timeout(t0);
    auto retry = HandleHello(
        BuildTrustedCredentialHelloEnvelope(dovahlink::security::EncodeHex(
            std::vector<std::uint8_t>{1, 2, 3, 4})),
        tokenStore, throttle, trustStore, credentialThrottle, sessions,
        /*connection=*/1, timeout, t0 + std::chrono::seconds(60));

    CHECK_FALSE(retry.closeConnection);
    CHECK(retry.sessionLease.has_value());
}

TEST_CASE("HandleHello's one_time_local_token path never enrolls the client "
          "into TrustStore",
          "[application][handshake_handler]") {
    //  ai/context/protocol/security.md's "Developer authentication":
    //  developer-token authentication "must not silently enroll the authenticating
    //  client into the persistent trusted-device store." HandleHello's
    //  one_time_local_token branch never calls TrustStore::Persist (its only
    //  caller in the whole codebase is pairing_handler.cpp's HandlePairingAck,
    //  unreachable for a kFull session per message_dispatcher.cpp's trust-tier
    //  allowlist) -- this proves that structural guarantee directly, for a
    //  clientId with no prior record, rather than relying on that reasoning
    //  holding across files.
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    FailedTokenThrottle credentialThrottle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    auto hello = BuildHelloEnvelope(kValidHexToken, "message-hello-1",
                                    std::string("never-enrolled-client"));
    auto result = HandleHello(hello, tokenStore, throttle, trustStore,
                              credentialThrottle, sessions,
                              /*connection=*/1, timeout, now);

    REQUIRE_FALSE(result.closeConnection);
    REQUIRE(result.sessionLease.has_value());
    CHECK(sessions.IsFullyTrusted(/*connection=*/1));
    CHECK_FALSE(trustStore.Query("never-enrolled-client").has_value());
}

TEST_CASE("HandleHello's one_time_local_token path leaves an already-paired "
          "clientId's TrustStore "
          "record untouched",
          "[application][handshake_handler]") {
    //  Distinct from the no-prior-record test above: proves developer-token auth
    //  for a clientId that happens to already be a genuinely paired device neither
    //  overwrites nor otherwise mutates that existing record, not just that it
    //  doesn't create a new one.
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    std::vector<std::uint8_t> credentialBytes{9, 8, 7, 6};
    auto originalRecord = trustStore.Persist(
        "already-paired-client", credentialBytes, std::string("My PC"));
    REQUIRE(originalRecord.has_value());
    FailedTokenThrottle credentialThrottle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    auto hello = BuildHelloEnvelope(kValidHexToken, "message-hello-1",
                                    std::string("already-paired-client"));
    auto result = HandleHello(hello, tokenStore, throttle, trustStore,
                              credentialThrottle, sessions,
                              /*connection=*/1, timeout, now);

    REQUIRE_FALSE(result.closeConnection);
    CHECK(sessions.IsFullyTrusted(/*connection=*/1));
    auto recordAfter = trustStore.Query("already-paired-client");
    REQUIRE(recordAfter.has_value());
    CHECK(recordAfter->credential == originalRecord->credential);
    CHECK(recordAfter->shortId == originalRecord->shortId);
    CHECK(recordAfter->displayName == originalRecord->displayName);
}

TEST_CASE("the same clientId across unpaired, one_time_local_token, and "
          "trusted_device_credential "
          "hellos leaves no cross-method state leakage",
          "[application][handshake_handler]") {
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    std::vector<std::uint8_t> credentialBytes{1, 2, 3, 4};
    REQUIRE(trustStore.Persist("client-1", credentialBytes, std::nullopt)
                .has_value());

    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    FailedTokenThrottle credentialThrottle;
    auto now = std::chrono::steady_clock::now();

    {
        SessionManager sessions;
        ConnectionTimeoutTracker timeout(now);
        auto result =
            HandleHello(BuildUnpairedHelloEnvelope(), tokenStore, throttle,
                        trustStore, credentialThrottle, sessions, 1, timeout, now);
        CHECK_FALSE(result.closeConnection);
        CHECK_FALSE(sessions.IsFullyTrusted(1));
    }
    {
        SessionManager sessions;
        ConnectionTimeoutTracker timeout(now);
        auto result =
            HandleHello(BuildHelloEnvelope(kValidHexToken), tokenStore, throttle,
                        trustStore, credentialThrottle, sessions, 1, timeout, now);
        CHECK_FALSE(result.closeConnection);
        CHECK(sessions.IsFullyTrusted(1));
        auto ack =
            dovahlink::protocol::DecodeHelloAckPayload(result.response.payload);
        REQUIRE(ack.has_value());
        CHECK(ack->clientIdentityKind == "unpaired");
    }
    {
        SessionManager sessions;
        ConnectionTimeoutTracker timeout(now);
        auto result =
            HandleHello(BuildTrustedCredentialHelloEnvelope(
                            dovahlink::security::EncodeHex(credentialBytes)),
                        tokenStore, throttle, trustStore, credentialThrottle,
                        sessions, 1, timeout, now);
        CHECK_FALSE(result.closeConnection);
        CHECK(sessions.IsFullyTrusted(1));
        auto ack =
            dovahlink::protocol::DecodeHelloAckPayload(result.response.payload);
        REQUIRE(ack.has_value());
        CHECK(ack->clientIdentityKind == "paired");
    }
}

TEST_CASE(
    "HandleHello reports rate_limited once the failed-token throttle trips",
    "[application][handshake_handler]") {
    FailedTokenThrottle
        throttle; //  shared across every attempt below, like a real deployment.
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    FailedTokenThrottle credentialThrottle;
    auto now = std::chrono::steady_clock::now();

    //  security::kMaxFailedTokenAttempts is 5: the first 5 failures are each
    //  reported as unauthenticated. The 6th attempt is rejected before token
    //  validation, even when it presents the correct token.
    dovahlink::protocol::ErrorPayload lastError;
    TokenStore tokenStore(ValidTokenBytes());
    for (int attempt = 0; attempt < 5; ++attempt) {
        SessionManager sessions;
        ConnectionTimeoutTracker timeout(now);
        auto hello = BuildHelloEnvelope(kWrongHexToken);
        auto result = HandleHello(hello, tokenStore, throttle, trustStore,
                                  credentialThrottle, sessions, 1, timeout, now);
        auto error =
            dovahlink::protocol::DecodeErrorPayload(result.response.payload);
        REQUIRE(error.has_value());
        lastError = *error;
    }

    SessionManager sessions;
    ConnectionTimeoutTracker timeout(now);
    auto blocked = HandleHello(BuildHelloEnvelope(kValidHexToken), tokenStore,
                               throttle, trustStore, credentialThrottle, sessions,
                               /*connection=*/1, timeout, now);
    auto blockedError =
        dovahlink::protocol::DecodeErrorPayload(blocked.response.payload);
    REQUIRE(blockedError.has_value());
    lastError = *blockedError;

    CHECK(lastError.code == "rate_limited");
    CHECK(lastError.retryable);
    CHECK(tokenStore.IsAvailable());
}

TEST_CASE("rate-limited handshakes do not extend the failed-attempt window",
          "[application][handshake_handler]") {
    FailedTokenThrottle throttle;
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    FailedTokenThrottle credentialThrottle;
    auto t0 = std::chrono::steady_clock::now();
    for (int attempt = 0; attempt < 5; ++attempt) {
        throttle.RecordFailure(t0);
    }

    TokenStore tokenStore(ValidTokenBytes());
    for (int attempt = 0; attempt < 5; ++attempt) {
        SessionManager blockedSessions;
        ConnectionTimeoutTracker blockedTimeout(t0);
        auto blocked = HandleHello(
            BuildHelloEnvelope(kValidHexToken,
                               "message-blocked-" + std::to_string(attempt)),
            tokenStore, throttle, trustStore, credentialThrottle, blockedSessions,
            /*connection=*/1, blockedTimeout, t0 + std::chrono::seconds(59));
        auto error =
            dovahlink::protocol::DecodeErrorPayload(blocked.response.payload);
        REQUIRE(error.has_value());
        CHECK(error->code == "rate_limited");
    }

    SessionManager sessions;
    ConnectionTimeoutTracker timeout(t0);
    auto retry =
        HandleHello(BuildHelloEnvelope(kValidHexToken), tokenStore, throttle,
                    trustStore, credentialThrottle, sessions, /*connection=*/1,
                    timeout, t0 + std::chrono::seconds(60));

    CHECK_FALSE(retry.closeConnection);
    CHECK(retry.sessionLease.has_value());
}

TEST_CASE(
    "HandleHello rejects a second connection while a session is already active",
    "[application][handshake_handler]") {
    SessionManager sessions;
    auto existingLease = sessions.TryCreateSession(
        /*connection=*/1, "existing-session", "existing-client",
        SessionTrustTier::kFull, SessionAuthMethod::kTrustedDeviceCredential);
    REQUIRE(existingLease.has_value());

    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    FailedTokenThrottle credentialThrottle;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    auto hello = BuildHelloEnvelope(kValidHexToken);
    auto result = HandleHello(hello, tokenStore, throttle, trustStore,
                              credentialThrottle, sessions,
                              /*connection=*/2, timeout, now);

    CHECK(result.closeConnection);
    auto error = dovahlink::protocol::DecodeErrorPayload(result.response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "unauthorized");
    CHECK(error->retryable);
    CHECK(tokenStore.IsAvailable());

    existingLease.reset();
    ConnectionTimeoutTracker retryTimeout(now);
    auto retry = HandleHello(hello, tokenStore, throttle, trustStore,
                             credentialThrottle, sessions,
                             /*connection=*/2, retryTimeout, now);
    CHECK_FALSE(retry.closeConnection);
    CHECK(retry.sessionLease.has_value());
    CHECK_FALSE(tokenStore.IsAvailable());
}

TEST_CASE("HandleHello marks a one_time_local_token session as kDeveloperToken",
          "[application][handshake_handler]") {
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    FailedTokenThrottle credentialThrottle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    auto hello = BuildHelloEnvelope(kValidHexToken);
    auto result = HandleHello(hello, tokenStore, throttle, trustStore,
                              credentialThrottle, sessions,
                              /*connection=*/1, timeout, now);

    REQUIRE(result.sessionLease.has_value());
    auto authMethod = sessions.AuthMethodForConnection(/*connection=*/1);
    REQUIRE(authMethod.has_value());
    CHECK(*authMethod == SessionAuthMethod::kDeveloperToken);
}

TEST_CASE("HandleHello marks an unpaired session as kUnpaired",
          "[application][handshake_handler]") {
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    FailedTokenThrottle credentialThrottle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    auto hello = BuildUnpairedHelloEnvelope();
    auto result = HandleHello(hello, tokenStore, throttle, trustStore,
                              credentialThrottle, sessions, 1, timeout, now);

    REQUIRE(result.sessionLease.has_value());
    auto authMethod = sessions.AuthMethodForConnection(/*connection=*/1);
    REQUIRE(authMethod.has_value());
    CHECK(*authMethod == SessionAuthMethod::kUnpaired);
}

TEST_CASE("HandleHello marks a trusted_device_credential session as "
          "kTrustedDeviceCredential",
          "[application][handshake_handler]") {
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    std::vector<std::uint8_t> credentialBytes{1, 2, 3, 4};
    REQUIRE(trustStore.Persist("client-1", credentialBytes, std::nullopt)
                .has_value());

    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    FailedTokenThrottle credentialThrottle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    auto hello = BuildTrustedCredentialHelloEnvelope(
        dovahlink::security::EncodeHex(credentialBytes));
    auto result = HandleHello(hello, tokenStore, throttle, trustStore,
                              credentialThrottle, sessions, 1, timeout, now);

    REQUIRE(result.sessionLease.has_value());
    auto authMethod = sessions.AuthMethodForConnection(/*connection=*/1);
    REQUIRE(authMethod.has_value());
    CHECK(*authMethod == SessionAuthMethod::kTrustedDeviceCredential);
}

TEST_CASE("HandleHello updates AuthMethodForConnection on reconnect rather "
          "than retaining a stale "
          "value from the previous session on the same connection",
          "[application][handshake_handler]") {
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    FailedTokenThrottle credentialThrottle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();

    ConnectionTimeoutTracker firstTimeout(now);
    auto firstHello = BuildUnpairedHelloEnvelope();
    auto firstResult =
        HandleHello(firstHello, tokenStore, throttle, trustStore,
                    credentialThrottle, sessions, 1, firstTimeout, now);
    REQUIRE(firstResult.sessionLease.has_value());
    auto firstAuthMethod = sessions.AuthMethodForConnection(/*connection=*/1);
    REQUIRE(firstAuthMethod.has_value());
    CHECK(*firstAuthMethod == SessionAuthMethod::kUnpaired);
    firstResult.sessionLease.reset();

    ConnectionTimeoutTracker secondTimeout(now);
    auto secondHello = BuildHelloEnvelope(kValidHexToken, "message-hello-2");
    auto secondResult =
        HandleHello(secondHello, tokenStore, throttle, trustStore,
                    credentialThrottle, sessions, 1, secondTimeout, now);

    REQUIRE(secondResult.sessionLease.has_value());
    auto secondAuthMethod = sessions.AuthMethodForConnection(/*connection=*/1);
    REQUIRE(secondAuthMethod.has_value());
    CHECK(*secondAuthMethod == SessionAuthMethod::kDeveloperToken);
}

TEST_CASE("TrustLossAfterAdmission reports revoked for a revoked clientId with "
          "kTrustedDeviceCredential",
          "[application][handshake_handler]") {
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    REQUIRE(trustStore
                .Persist("client-1", std::vector<std::uint8_t>{1, 2, 3, 4},
                         std::nullopt)
                .has_value());
    REQUIRE(trustStore.Revoke("client-1"));

    auto reason = TrustLossAfterAdmission(
        trustStore, "client-1", SessionAuthMethod::kTrustedDeviceCredential);

    REQUIRE(reason.has_value());
    CHECK(*reason == "revoked");
}

TEST_CASE("TrustLossAfterAdmission reports blocked for a blocked clientId with "
          "kTrustedDeviceCredential",
          "[application][handshake_handler]") {
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    REQUIRE(trustStore
                .Persist("client-1", std::vector<std::uint8_t>{1, 2, 3, 4},
                         std::nullopt)
                .has_value());
    REQUIRE(trustStore.Block("client-1") == BlockOutcome::kBlocked);

    auto reason = TrustLossAfterAdmission(
        trustStore, "client-1", SessionAuthMethod::kTrustedDeviceCredential);

    REQUIRE(reason.has_value());
    CHECK(*reason == "blocked");
}

TEST_CASE("TrustLossAfterAdmission reports blocked for a blocked clientId with "
          "kUnpaired",
          "[application][handshake_handler]") {
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    REQUIRE(trustStore
                .Persist("client-1", std::vector<std::uint8_t>{1, 2, 3, 4},
                         std::nullopt)
                .has_value());
    REQUIRE(trustStore.Block("client-1") == BlockOutcome::kBlocked);

    auto reason = TrustLossAfterAdmission(trustStore, "client-1",
                                          SessionAuthMethod::kUnpaired);

    REQUIRE(reason.has_value());
    CHECK(*reason == "blocked");
}

TEST_CASE("TrustLossAfterAdmission reports no loss for a revoked clientId with "
          "kUnpaired",
          "[application][handshake_handler]") {
    //  Revoke only applies to kUnpaired's own credential-less admission
    //  indirectly, via IsBlocked -- the revoked check itself is gated to
    //  kTrustedDeviceCredential, so a revoked clientId must fall through to no
    //  loss when authMethod is kUnpaired instead.
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    REQUIRE(trustStore
                .Persist("client-1", std::vector<std::uint8_t>{1, 2, 3, 4},
                         std::nullopt)
                .has_value());
    REQUIRE(trustStore.Revoke("client-1"));

    CHECK_FALSE(TrustLossAfterAdmission(trustStore, "client-1",
                                        SessionAuthMethod::kUnpaired)
                    .has_value());
}

TEST_CASE(
    "TrustLossAfterAdmission reports no loss for a still-trusted clientId with "
    "kTrustedDeviceCredential",
    "[application][handshake_handler]") {
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    REQUIRE(trustStore
                .Persist("client-1", std::vector<std::uint8_t>{1, 2, 3, 4},
                         std::nullopt)
                .has_value());

    CHECK_FALSE(
        TrustLossAfterAdmission(trustStore, "client-1",
                                SessionAuthMethod::kTrustedDeviceCredential)
            .has_value());
}

TEST_CASE(
    "TrustLossAfterAdmission reports no loss for a never-known clientId with "
    "kTrustedDeviceCredential",
    "[application][handshake_handler]") {
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);

    CHECK_FALSE(
        TrustLossAfterAdmission(trustStore, "client-1",
                                SessionAuthMethod::kTrustedDeviceCredential)
            .has_value());
}

TEST_CASE("TrustLossAfterAdmission reports no loss for a never-known clientId "
          "with kUnpaired",
          "[application][handshake_handler]") {
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);

    CHECK_FALSE(TrustLossAfterAdmission(trustStore, "client-1",
                                        SessionAuthMethod::kUnpaired)
                    .has_value());
}

TEST_CASE("TrustLossAfterAdmission is exempt for kDeveloperToken even when the "
          "clientId is blocked",
          "[application][handshake_handler]") {
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    REQUIRE(trustStore
                .Persist("client-1", std::vector<std::uint8_t>{1, 2, 3, 4},
                         std::nullopt)
                .has_value());
    REQUIRE(trustStore.Block("client-1") == BlockOutcome::kBlocked);

    CHECK_FALSE(TrustLossAfterAdmission(trustStore, "client-1",
                                        SessionAuthMethod::kDeveloperToken)
                    .has_value());
}

TEST_CASE("TrustLossAfterAdmission is exempt for kDeveloperToken even when the "
          "clientId is revoked",
          "[application][handshake_handler]") {
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    REQUIRE(trustStore
                .Persist("client-1", std::vector<std::uint8_t>{1, 2, 3, 4},
                         std::nullopt)
                .has_value());
    REQUIRE(trustStore.Revoke("client-1"));

    CHECK_FALSE(TrustLossAfterAdmission(trustStore, "client-1",
                                        SessionAuthMethod::kDeveloperToken)
                    .has_value());
}

TEST_CASE("HandleHello's post-admission trust recheck does not reject a "
          "still-trusted "
          "trusted_device_credential hello",
          "[application][handshake_handler]") {
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    std::vector<std::uint8_t> credentialBytes{1, 2, 3, 4};
    REQUIRE(trustStore.Persist("client-1", credentialBytes, std::nullopt)
                .has_value());

    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle throttle;
    FailedTokenThrottle credentialThrottle;
    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);

    auto hello = BuildTrustedCredentialHelloEnvelope(
        dovahlink::security::EncodeHex(credentialBytes));
    auto result = HandleHello(hello, tokenStore, throttle, trustStore,
                              credentialThrottle, sessions, 1, timeout, now);

    REQUIRE(result.sessionLease.has_value());
    REQUIRE(result.response.sessionId.has_value());
    CHECK(sessions.IsValidForConnection(*result.response.sessionId,
                                        /*connection=*/1));
}

TEST_CASE("a rate-limited trusted credential hello does not validate the "
          "credential",
          "[application][handshake_handler]") {
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle tokenThrottle;
    FailedTokenThrottle credentialThrottle;
    auto now = std::chrono::steady_clock::now();
    for (int attempt = 0; attempt < 5; ++attempt) {
        auto reservation = credentialThrottle.TryReserve(now);
        REQUIRE(reservation.has_value());
        reservation->Commit();
    }

    StrictMock<MockTrustStore> trustStore;
    EXPECT_CALL(trustStore, IsBlocked("client-1"))
        .WillOnce(testing::Return(false));
    EXPECT_CALL(trustStore, Authenticate(testing::_, testing::_)).Times(0);
    EXPECT_CALL(trustStore, IsRevoked(testing::_)).Times(0);

    SessionManager sessions;
    ConnectionTimeoutTracker timeout(now);
    auto result = HandleHello(
        BuildTrustedCredentialHelloEnvelope(kWrongHexToken), tokenStore,
        tokenThrottle, trustStore, credentialThrottle, sessions, 1, timeout,
        now);

    CHECK(result.closeConnection);
    auto error = dovahlink::protocol::DecodeErrorPayload(result.response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "rate_limited");
}

TEST_CASE("an invalid trusted-device credential commits a failed attempt",
          "[application][handshake_handler]") {
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    const std::vector<std::uint8_t> credentialBytes{1, 2, 3, 4};
    REQUIRE(trustStore.Persist("client-1", credentialBytes, std::nullopt)
                .has_value());

    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle tokenThrottle;
    FailedTokenThrottle credentialThrottle;
    auto now = std::chrono::steady_clock::now();
    const std::string wrongCredential =
        dovahlink::security::EncodeHex(std::vector<std::uint8_t>{9, 9, 9, 9});

    for (int attempt = 0; attempt < 5; ++attempt) {
        SessionManager sessions;
        ConnectionTimeoutTracker timeout(now);
        auto result = HandleHello(
            BuildTrustedCredentialHelloEnvelope(wrongCredential,
                                                "message-wrong-" +
                                                    std::to_string(attempt)),
            tokenStore, tokenThrottle, trustStore, credentialThrottle, sessions,
            1, timeout, now);
        auto error =
            dovahlink::protocol::DecodeErrorPayload(result.response.payload);
        REQUIRE(error.has_value());
        CHECK(error->code == "unauthenticated");
    }

    SessionManager sessions;
    ConnectionTimeoutTracker timeout(now);
    auto result = HandleHello(
        BuildTrustedCredentialHelloEnvelope(
            dovahlink::security::EncodeHex(credentialBytes), "message-valid"),
        tokenStore, tokenThrottle, trustStore, credentialThrottle, sessions, 1,
        timeout, now);
    auto error =
        dovahlink::protocol::DecodeErrorPayload(result.response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "rate_limited");
}

TEST_CASE("valid developer authentication releases its failed-attempt slot",
          "[application][handshake_handler]") {
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle tokenThrottle;
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    FailedTokenThrottle credentialThrottle;
    auto now = std::chrono::steady_clock::now();

    for (int attempt = 0; attempt < 4; ++attempt) {
        SessionManager sessions;
        ConnectionTimeoutTracker timeout(now);
        auto result = HandleHello(
            BuildHelloEnvelope(kWrongHexToken,
                               "message-wrong-" + std::to_string(attempt)),
            tokenStore, tokenThrottle, trustStore, credentialThrottle, sessions,
            1, timeout, now);
        CHECK(result.response.messageType == "error");
    }

    SessionManager sessions;
    ConnectionTimeoutTracker timeout(now);
    auto success = HandleHello(BuildHelloEnvelope(kValidHexToken, "message-valid"),
                               tokenStore, tokenThrottle, trustStore,
                               credentialThrottle, sessions, 1, timeout, now);
    REQUIRE(success.sessionLease.has_value());
    success.sessionLease.reset();

    ConnectionTimeoutTracker retryTimeout(now);
    auto retry = HandleHello(
        BuildHelloEnvelope(kWrongHexToken, "message-after-success"), tokenStore,
        tokenThrottle, trustStore, credentialThrottle, sessions, 1, retryTimeout,
        now);
    auto error = dovahlink::protocol::DecodeErrorPayload(retry.response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "unauthenticated");
}

TEST_CASE("valid trusted-device authentication releases its slot when session "
          "admission fails",
          "[application][handshake_handler]") {
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    const std::vector<std::uint8_t> credentialBytes{1, 2, 3, 4};
    REQUIRE(trustStore.Persist("client-1", credentialBytes, std::nullopt)
                .has_value());

    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle tokenThrottle;
    FailedTokenThrottle credentialThrottle;
    auto now = std::chrono::steady_clock::now();
    for (int attempt = 0; attempt < 4; ++attempt) {
        auto reservation = credentialThrottle.TryReserve(now);
        REQUIRE(reservation.has_value());
        reservation->Commit();
    }

    SessionManager sessions;
    auto existingLease = sessions.TryCreateSession(
        2, "existing-session", "existing-client", SessionTrustTier::kFull,
        SessionAuthMethod::kTrustedDeviceCredential);
    REQUIRE(existingLease.has_value());

    ConnectionTimeoutTracker timeout(now);
    auto rejected = HandleHello(
        BuildTrustedCredentialHelloEnvelope(
            dovahlink::security::EncodeHex(credentialBytes), "message-valid"),
        tokenStore, tokenThrottle, trustStore, credentialThrottle, sessions, 1,
        timeout, now);
    auto rejectedError =
        dovahlink::protocol::DecodeErrorPayload(rejected.response.payload);
    REQUIRE(rejectedError.has_value());
    CHECK(rejectedError->code == "unauthorized");
    existingLease.reset();

    ConnectionTimeoutTracker retryTimeout(now);
    auto retry = HandleHello(
        BuildTrustedCredentialHelloEnvelope(kWrongHexToken,
                                            "message-after-rejection"),
        tokenStore, tokenThrottle, trustStore, credentialThrottle, sessions, 1,
        retryTimeout, now);
    auto retryError =
        dovahlink::protocol::DecodeErrorPayload(retry.response.payload);
    REQUIRE(retryError.has_value());
    CHECK(retryError->code == "unauthenticated");
}

TEST_CASE("valid trusted-device authentication releases its failed-attempt "
          "slot",
          "[application][handshake_handler]") {
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    const std::vector<std::uint8_t> credentialBytes{1, 2, 3, 4};
    REQUIRE(trustStore.Persist("client-1", credentialBytes, std::nullopt)
                .has_value());

    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle tokenThrottle;
    FailedTokenThrottle credentialThrottle;
    auto now = std::chrono::steady_clock::now();
    const std::string wrongCredential =
        dovahlink::security::EncodeHex(std::vector<std::uint8_t>{9, 9, 9, 9});
    for (int attempt = 0; attempt < 4; ++attempt) {
        auto reservation = credentialThrottle.TryReserve(now);
        REQUIRE(reservation.has_value());
        reservation->Commit();
    }

    SessionManager sessions;
    ConnectionTimeoutTracker timeout(now);
    auto success = HandleHello(
        BuildTrustedCredentialHelloEnvelope(
            dovahlink::security::EncodeHex(credentialBytes), "message-valid"),
        tokenStore, tokenThrottle, trustStore, credentialThrottle, sessions, 1,
        timeout, now);
    REQUIRE(success.sessionLease.has_value());
    success.sessionLease.reset();

    ConnectionTimeoutTracker retryTimeout(now);
    auto retry = HandleHello(
        BuildTrustedCredentialHelloEnvelope(wrongCredential,
                                            "message-after-success"),
        tokenStore, tokenThrottle, trustStore, credentialThrottle, sessions, 1,
        retryTimeout, now);
    auto error = dovahlink::protocol::DecodeErrorPayload(retry.response.payload);
    REQUIRE(error.has_value());
    CHECK(error->code == "unauthenticated");
}

TEST_CASE("an authentication exception releases its failed-attempt reservation",
          "[application][handshake_handler]") {
    TokenStore tokenStore(ValidTokenBytes());
    FailedTokenThrottle tokenThrottle;
    FailedTokenThrottle credentialThrottle;
    StrictMock<MockTrustStore> trustStore;
    EXPECT_CALL(trustStore, IsBlocked("client-1"))
        .WillOnce(testing::Return(false));
    EXPECT_CALL(trustStore, Authenticate(testing::_, testing::_))
        .WillOnce(testing::Throw(std::runtime_error("validation failed")));

    SessionManager sessions;
    auto now = std::chrono::steady_clock::now();
    ConnectionTimeoutTracker timeout(now);
    CHECK_THROWS_AS(
        HandleHello(BuildTrustedCredentialHelloEnvelope(kWrongHexToken),
                    tokenStore, tokenThrottle, trustStore, credentialThrottle,
                    sessions, 1, timeout, now),
        std::runtime_error);

    auto retryReservation = credentialThrottle.TryReserve(now);
    CHECK(retryReservation.has_value());
}
