#include "application/active_play_context_reader.hpp"
#include "application/connection_session.hpp"

#include "application/application_test_support.hpp"
#include "application/bridge_config.hpp"
#include "protocol/bounded_json.hpp"
#include "protocol/envelope.hpp"
#include "protocol/messages.hpp"
#include "security/hex.hpp"
#include "security/test_token.hpp"
#include "transport/loopback_listener.hpp"

#include <catch2/catch_test_macros.hpp>
#include <gmock/gmock.h>

#include <boost/asio/buffer.hpp>
#include <boost/asio/io_context.hpp>
#include <boost/asio/ip/tcp.hpp>
#include <boost/beast/core/buffers_to_string.hpp>
#include <boost/beast/core/flat_buffer.hpp>
#include <boost/beast/websocket/error.hpp>
#include <boost/beast/websocket/stream.hpp>
#include <boost/json/parse.hpp>
#include <boost/system/error_code.hpp>

#include <chrono>
#include <expected>
#include <memory>
#include <string>
#include <string_view>
#include <thread>
#include <utility>
#include <vector>

using dovahlink::application::IActivePlayContextReader;
using dovahlink::application::IConnectionSession;
using dovahlink::application::IPairingNotificationSink;
using dovahlink::application::kBridgeVersion;
using dovahlink::application::SessionManager;
using dovahlink::application::test_support::BuildEnvelope;
using dovahlink::application::test_support::BuildPairingAckEnvelope;
using dovahlink::application::test_support::BuildPairingConfirmEnvelope;
using dovahlink::application::test_support::BuildPairingRequestEnvelope;
using dovahlink::application::test_support::EmptyActivePlayContext;
using dovahlink::application::test_support::MockActivePlayContext;
using dovahlink::protocol::Envelope;
using dovahlink::security::DecodeHex;
using dovahlink::security::FailedTokenThrottle;
using dovahlink::security::ITrustStorePersistence;
using dovahlink::security::kValidHexToken;
using dovahlink::security::PairingSession;
using dovahlink::security::StartChallengeOutcome;
using dovahlink::security::TokenStore;
using dovahlink::security::TrustStore;
using dovahlink::security::TrustStoreSnapshot;
using dovahlink::transport::IWebSocketSession;
using dovahlink::transport::LoopbackListener;
using dovahlink::transport::SessionError;
using dovahlink::transport::WebSocketSession;
using testing::Return;
using testing::StrictMock;

//  These tests use two real loopback sockets (client and server) with a real
//  Boost.Beast WebSocket handshake and application-level protocol exchange,
//  matching this project's established convention that transport-adjacent
//  code is proven against real sockets rather than fakes (ai/context/
//  integration/testing.md). Catch2 assertions are never called from the
//  background server thread; results are captured into variables and
//  checked only after the thread is joined onto the main test thread,
//  matching transport/websocket_session_test.cpp's own precedent.

namespace {

///  `ITrustStorePersistence` double that always loads an empty snapshot -- these
///  tests only exercise the one_time_local_token auth path and never touch the
///  trust store.
class EmptyPersistence : public ITrustStorePersistence {
  public:
    ///  Always reports a valid, empty snapshot.
    std::optional<TrustStoreSnapshot> Load() override {
        return TrustStoreSnapshot{};
    }

    ///  Always succeeds without recording anything.
    bool Save(const TrustStoreSnapshot&) override { return true; }
};

///  `IPairingNotificationSink` double that records every code it is given. These
///  tests only need a working sink to satisfy `RunConnectionSession`'s signature
///  -- pairing behavior itself is exercised in message_dispatcher_test.cpp and
///  pairing_handler_test.cpp.
class RecordingPairingNotificationSink : public IPairingNotificationSink {
  public:
    ///  Appends `sixDigitCode` to `codes`.
    void NotifyPairingCodeAvailable(std::string_view sixDigitCode) override {
        codes.emplace_back(sixDigitCode);
    }

    ///  No-op: satisfies the interface only, matching this fake's existing
    ///  minimal-footprint intent.
    void NotifyPairingCodeIncorrect(std::string_view) override {}

    ///  No-op: satisfies the interface only, matching this fake's existing
    ///  minimal-footprint intent.
    void NotifyPairingAttemptsExhausted() override {}

    ///  Every code this sink has been given, in order.
    std::vector<std::string> codes;
};

///  `IWebSocketSession` mock used to prove `RunConnectionSession`'s own
///  early-return orchestration branches without a real socket.
class MockWebSocketSession : public IWebSocketSession {
  public:
    MOCK_METHOD((std::expected<void, SessionError>), Accept, (), (override));
    MOCK_METHOD(void, SwitchToIdleTimeout, (), (override));
    MOCK_METHOD((std::expected<std::string, SessionError>), ReadMessage,
                (std::optional<std::chrono::steady_clock::time_point>),
                (override));
    MOCK_METHOD((std::expected<void, SessionError>), WriteMessage,
                (const std::string&), (override));
    MOCK_METHOD(void, Close, (), (override));
};

///  `IHandshakeHandler` mock used to prove `ConnectionSession`'s own
///  early-return orchestration branches never reach handshake validation.
class MockHandshakeHandler : public dovahlink::application::IHandshakeHandler {
  public:
    MOCK_METHOD(dovahlink::application::HandshakeResult, Handle,
                (const Envelope&, dovahlink::application::ConnectionId,
                 dovahlink::application::IConnectionTimeoutTracker&,
                 std::chrono::steady_clock::time_point),
                (override));
};

///  `IMessageDispatcher` mock used to prove `ConnectionSession`'s message loop
///  delegates to it, isolated from `MessageDispatcher`'s own dispatch logic
///  (covered by message_dispatcher_test.cpp).
class MockMessageDispatcher
    : public dovahlink::application::IMessageDispatcher {
  public:
    MOCK_METHOD(dovahlink::application::DispatchResult, Process,
                (const std::string&, std::size_t&, const std::string&,
                 dovahlink::application::ConnectionId,
                 dovahlink::application::IReplayGuard&,
                 dovahlink::security::IViolationTracker&,
                 dovahlink::security::IInboundMessageRateLimiter&,
                 dovahlink::application::IConnectionTimeoutTracker&,
                 std::chrono::steady_clock::time_point),
                (override));
};

///  Reads and decodes one protocol envelope from the test WebSocket.
Envelope ClientReadEnvelope(
    boost::beast::websocket::stream<boost::asio::ip::tcp::socket>& clientWs) {
    boost::beast::flat_buffer buffer;
    boost::system::error_code ec;
    clientWs.read(buffer, ec);
    REQUIRE_FALSE(ec);
    auto parsed = dovahlink::protocol::ParseBoundedJson(
        boost::beast::buffers_to_string(buffer.data()));
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE(envelope.has_value());
    return std::move(*envelope);
}

///  Writes one text protocol message to the test WebSocket.
void ClientWriteText(
    boost::beast::websocket::stream<boost::asio::ip::tcp::socket>& clientWs,
    const std::string& text) {
    clientWs.text(true);
    boost::system::error_code ec;
    clientWs.write(boost::asio::buffer(text), ec);
    REQUIRE_FALSE(ec);
}

///  Builds a hello message using the supplied authentication token and clientId.
std::string HelloMessage(const std::string& token,
                         std::string clientId = "client-1") {
    return dovahlink::protocol::EncodeEnvelope(
        dovahlink::application::test_support::BuildHelloEnvelope(
            std::string(token), "message-hello-1", std::move(clientId)));
}

///  Builds an unpaired-tier hello message (no credential) for the given clientId
///  -- the actual trust tier the pairing flow runs under, per
///  `ai/context/protocol/security.md`'s "Hello authentication and session trust
///  tiers".
std::string UnpairedHelloMessage(std::string clientId) {
    return dovahlink::protocol::EncodeEnvelope(
        dovahlink::application::test_support::BuildHelloEnvelope(
            std::nullopt, "message-hello-1", std::move(clientId), "unpaired"));
}

///  Builds a ping message for an established session.
std::string PingMessage(const std::string& sessionId) {
    return dovahlink::protocol::EncodeEnvelope(
        BuildEnvelope("ping", "message-ping-1", std::string(sessionId)));
}

///  Builds a subscribe message requesting an example state area on an
///  established session.
std::string SubscribeMessage(const std::string& sessionId,
                             std::string messageId = "message-sub-1") {
    auto payload = boost::json::parse(
                       R"({"stateAreas": ["character"]})")
                       .get_object();
    return dovahlink::protocol::EncodeEnvelope(BuildEnvelope(
        "subscribe", std::move(messageId), std::string(sessionId),
        std::nullopt, std::move(payload)));
}

///  Keeps transport-session tests focused on connection behavior while wiring
///  the real trust-mutation coordinator required by production sessions.
void RunConnectionSession(
    WebSocketSession& ws, TokenStore& tokenStore,
    FailedTokenThrottle& tokenThrottle, TrustStore& trustStore,
    FailedTokenThrottle& credentialThrottle, SessionManager& sessionManager,
    dovahlink::application::ConnectionId connection,
    const IActivePlayContextReader& activePlayContext,
    PairingSession& pairingSession,
    IPairingNotificationSink& pairingNotificationSink,
    const std::optional<std::string>& bridgeInstanceId,
    const std::string& bridgeVersion,
    dovahlink::application::SteadyNowProvider steadyNow = [] {
        return std::chrono::steady_clock::now();
    }) {
    dovahlink::application::TrustMutationCoordinator coordinator(trustStore,
                                                                 pairingSession,
                                                                 sessionManager);
    dovahlink::application::HandshakeHandler handshakeHandler(
        tokenStore, tokenThrottle, trustStore, credentialThrottle,
        sessionManager, activePlayContext, bridgeInstanceId, bridgeVersion);
    dovahlink::application::PairingHandler pairingHandler(
        pairingSession, coordinator, pairingNotificationSink);
    dovahlink::application::MessageDispatcher messageDispatcher(
        sessionManager, trustStore, coordinator, pairingHandler,
        activePlayContext, bridgeInstanceId);
    dovahlink::application::ConnectionSession connectionSession(
        handshakeHandler, messageDispatcher, activePlayContext, pairingSession,
        bridgeInstanceId);
    connectionSession.Run(ws, connection, std::move(steadyNow));
}

} //  namespace

TEST_CASE("RunConnectionSession completes hello, capabilities, ping, and "
          "subscribe over a real socket",
          "[application][connection_session]") {
    boost::asio::io_context ioc;
    auto listener =
        LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());
    boost::asio::ip::tcp::endpoint endpoint = listener->LocalEndpoint();

    TokenStore tokenStore(*DecodeHex(kValidHexToken));
    FailedTokenThrottle tokenThrottle;
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    FailedTokenThrottle credentialThrottle;
    PairingSession pairingSession;
    RecordingPairingNotificationSink pairingNotificationSink;
    SessionManager sessionManager;
    EmptyActivePlayContext activePlayContext;
    auto start = std::chrono::steady_clock::now();
    int clockCalls = 0;
    //  Simulate a hello completing at +4s, then authenticated traffic at
    //  +63s. The ping is 59s after authentication and must remain inside
    //  the 60s idle window; using the pre-read time would close it at +60s.
    auto steadyNow = [&] {
        ++clockCalls;
        if (clockCalls == 1) {
            return start;
        }
        if (clockCalls == 2) {
            return start + std::chrono::seconds(4);
        }
        return start + std::chrono::seconds(63);
    };

    boost::system::error_code serverAcceptEc;
    std::thread serverThread([&] {
        boost::asio::ip::tcp::socket serverSocket =
            listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        RunConnectionSession(
            session, tokenStore, tokenThrottle, trustStore, credentialThrottle,
            sessionManager, /*connection=*/1, activePlayContext, pairingSession,
            pairingNotificationSink, /*bridgeInstanceId=*/std::nullopt,
            kBridgeVersion, steadyNow);
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(
        std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);

    ClientWriteText(clientWs, HelloMessage(kValidHexToken));
    auto helloAck = ClientReadEnvelope(clientWs);
    REQUIRE(helloAck.messageType == "hello_ack");
    REQUIRE(helloAck.sessionId.has_value());
    std::string sessionId = *helloAck.sessionId;

    auto capabilities = ClientReadEnvelope(clientWs);
    CHECK(capabilities.messageType == "capabilities");

    ClientWriteText(clientWs, PingMessage(sessionId));
    auto pong = ClientReadEnvelope(clientWs);
    CHECK(pong.messageType == "pong");

    ClientWriteText(clientWs, SubscribeMessage(sessionId));
    auto subscriptionAck = ClientReadEnvelope(clientWs);
    CHECK(subscriptionAck.messageType == "subscription_ack");
    //  No state area is currently registered (protocol/schema/README.md's
    //  "Registered state areas"), so subscribe rejects every requested area and no
    //  snapshot follows.
    auto ack = dovahlink::protocol::DecodeSubscriptionAckPayload(
        subscriptionAck.payload);
    REQUIRE(ack.has_value());
    CHECK(ack->acceptedStateAreas.empty());
    CHECK(ack->rejectedStateAreas == std::vector<std::string>{"character"});

    boost::system::error_code closeEc;
    clientWs.close(boost::beast::websocket::close_code::normal, closeEc);

    serverThread.join();

    REQUIRE_FALSE(serverAcceptEc);
    //  Disconnect must invalidate the session (ai/context/protocol/security.md).
    CHECK_FALSE(sessionManager.IsValidForConnection(sessionId, /*connection=*/1));
    CHECK(clockCalls >= 4);
}

TEST_CASE("RunConnectionSession completes pairing through the mutation coordinator",
          "[application][connection_session]") {
    boost::asio::io_context ioc;
    auto listener =
        LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());
    boost::asio::ip::tcp::endpoint endpoint = listener->LocalEndpoint();

    TokenStore tokenStore(*DecodeHex(kValidHexToken));
    FailedTokenThrottle tokenThrottle;
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    FailedTokenThrottle credentialThrottle;
    PairingSession pairingSession(
        []() -> std::optional<std::string> { return std::string("123456"); });
    RecordingPairingNotificationSink pairingNotificationSink;
    SessionManager sessionManager;
    EmptyActivePlayContext activePlayContext;

    boost::system::error_code serverAcceptEc;
    std::thread serverThread([&] {
        boost::asio::ip::tcp::socket serverSocket =
            listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        RunConnectionSession(session, tokenStore, tokenThrottle, trustStore,
                             credentialThrottle, sessionManager, /*connection=*/1,
                             activePlayContext, pairingSession,
                             pairingNotificationSink, /*bridgeInstanceId=*/
                             std::nullopt,
                             kBridgeVersion);
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);
    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(
        std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);

    ClientWriteText(clientWs, UnpairedHelloMessage("client-1"));
    auto helloAck = ClientReadEnvelope(clientWs);
    REQUIRE(helloAck.sessionId.has_value());
    const std::string sessionId = *helloAck.sessionId;
    static_cast<void>(ClientReadEnvelope(clientWs));

    ClientWriteText(
        clientWs,
        dovahlink::protocol::EncodeEnvelope(BuildPairingRequestEnvelope(
            "pairing-request-1", sessionId)));
    auto pairingStatus = ClientReadEnvelope(clientWs);
    CHECK(pairingStatus.messageType == "pairing_status");

    ClientWriteText(
        clientWs,
        dovahlink::protocol::EncodeEnvelope(BuildPairingConfirmEnvelope(
            "123456", std::nullopt, "pairing-confirm-1", sessionId)));
    auto credentialIssued = ClientReadEnvelope(clientWs);
    auto issued = dovahlink::protocol::DecodePairingOutcomePayload(
        credentialIssued.payload);
    REQUIRE(issued.has_value());
    REQUIRE(issued->credential.has_value());
    CHECK(issued->outcome == "credential_issued");

    ClientWriteText(
        clientWs,
        dovahlink::protocol::EncodeEnvelope(BuildPairingAckEnvelope(
            *issued->credential, "pairing-ack-1", sessionId)));
    auto trusted = ClientReadEnvelope(clientWs);
    auto trustedOutcome =
        dovahlink::protocol::DecodePairingOutcomePayload(trusted.payload);
    REQUIRE(trustedOutcome.has_value());
    CHECK(trustedOutcome->outcome == "trusted");

    boost::system::error_code closeEc;
    clientWs.close(boost::beast::websocket::close_code::normal, closeEc);
    serverThread.join();
    CHECK_FALSE(serverAcceptEc);
    CHECK(trustStore.Query("client-1").has_value());
}

TEST_CASE("RunConnectionSession stamps bridgeInstanceId on every response, not "
          "just hello_ack",
          "[application][connection_session]") {
    boost::asio::io_context ioc;
    auto listener =
        LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());
    boost::asio::ip::tcp::endpoint endpoint = listener->LocalEndpoint();

    TokenStore tokenStore(*DecodeHex(kValidHexToken));
    FailedTokenThrottle tokenThrottle;
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    FailedTokenThrottle credentialThrottle;
    PairingSession pairingSession;
    RecordingPairingNotificationSink pairingNotificationSink;
    SessionManager sessionManager;
    EmptyActivePlayContext activePlayContext;
    std::optional<std::string> bridgeInstanceId = "bridge-1";

    boost::system::error_code serverAcceptEc;
    std::thread serverThread([&] {
        boost::asio::ip::tcp::socket serverSocket =
            listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        RunConnectionSession(
            session, tokenStore, tokenThrottle, trustStore, credentialThrottle,
            sessionManager, /*connection=*/1, activePlayContext, pairingSession,
            pairingNotificationSink, bridgeInstanceId, kBridgeVersion);
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(
        std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);

    ClientWriteText(clientWs, HelloMessage(kValidHexToken));
    auto helloAck = ClientReadEnvelope(clientWs);
    REQUIRE(helloAck.messageType == "hello_ack");
    REQUIRE(helloAck.sessionId.has_value());
    auto ack = dovahlink::protocol::DecodeHelloAckPayload(helloAck.payload);
    REQUIRE(ack.has_value());
    CHECK(ack->bridgeVersion == kBridgeVersion);
    //  The end-to-end point of this test: bridgeInstanceId reaches the wire,
    //  and playContextId is null (no play context is active in this test).
    REQUIRE(helloAck.bridgeInstanceId.has_value());
    CHECK(*helloAck.bridgeInstanceId == "bridge-1");
    CHECK_FALSE(helloAck.playContextId.has_value());
    //  hello_ack is the one exception: it echoes the client-provided identity
    //  once, confirming what was accepted at authentication.
    REQUIRE(helloAck.clientId.has_value());
    CHECK(*helloAck.clientId == "client-1");
    //  The real end-to-end point: the session manager RunConnectionSession
    //  authenticated against, not a synthetic one, now owns the client
    //  identity as session state. TryCreateSession (and thus this) commits
    //  before HandleHello returns and its response is written, which
    //  happens-before the client finishing this read.
    auto sessionClientId = sessionManager.ClientIdForConnection(/*connection=*/1);
    REQUIRE(sessionClientId.has_value());
    CHECK(*sessionClientId == "client-1");
    std::string sessionId = *helloAck.sessionId;

    auto capabilities = ClientReadEnvelope(clientWs);
    CHECK(capabilities.messageType == "capabilities");
    REQUIRE(capabilities.sessionId.has_value());
    CHECK(*capabilities.sessionId == sessionId);
    REQUIRE(capabilities.bridgeInstanceId.has_value());
    CHECK(*capabilities.bridgeInstanceId == "bridge-1");
    //  Every post-hello_ack response derives the client from session state
    //  rather than repeating it on the wire.
    CHECK_FALSE(capabilities.clientId.has_value());

    ClientWriteText(clientWs, PingMessage(sessionId));
    auto pong = ClientReadEnvelope(clientWs);
    CHECK(pong.messageType == "pong");
    REQUIRE(pong.sessionId.has_value());
    CHECK(*pong.sessionId == sessionId);
    REQUIRE(pong.bridgeInstanceId.has_value());
    CHECK(*pong.bridgeInstanceId == "bridge-1");
    CHECK_FALSE(pong.clientId.has_value());

    ClientWriteText(clientWs, SubscribeMessage(sessionId));
    auto subscriptionAck = ClientReadEnvelope(clientWs);
    CHECK(subscriptionAck.messageType == "subscription_ack");
    REQUIRE(subscriptionAck.sessionId.has_value());
    CHECK(*subscriptionAck.sessionId == sessionId);
    REQUIRE(subscriptionAck.bridgeInstanceId.has_value());
    CHECK(*subscriptionAck.bridgeInstanceId == "bridge-1");
    CHECK_FALSE(subscriptionAck.clientId.has_value());
    CHECK_FALSE(subscriptionAck.playContextId.has_value());

    boost::system::error_code closeEc;
    clientWs.close(boost::beast::websocket::close_code::normal, closeEc);

    serverThread.join();

    REQUIRE_FALSE(serverAcceptEc);
}

TEST_CASE("RunConnectionSession's unsolicited capabilities envelope carries a "
          "real playContextId "
          "when a context is already active at connect time",
          "[application][connection_session]") {
    //  The capabilities envelope is built outside MessageDispatcher::Process's
    //  own stamping loop (BuildBridgeCapabilities, sent once right after
    //  hello_ack); this is its own, separately wired stamping site.
    boost::asio::io_context ioc;
    auto listener =
        LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());
    boost::asio::ip::tcp::endpoint endpoint = listener->LocalEndpoint();

    TokenStore tokenStore(*DecodeHex(kValidHexToken));
    FailedTokenThrottle tokenThrottle;
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    FailedTokenThrottle credentialThrottle;
    PairingSession pairingSession;
    RecordingPairingNotificationSink pairingNotificationSink;
    SessionManager sessionManager;
    testing::StrictMock<MockActivePlayContext> activePlayContext;
    EXPECT_CALL(activePlayContext, CurrentPlayContextId())
        .Times(testing::AnyNumber())
        .WillRepeatedly(Return(std::optional<std::string>{"context-1"}));
    std::optional<std::string> bridgeInstanceId = "bridge-1";

    boost::system::error_code serverAcceptEc;
    std::thread serverThread([&] {
        boost::asio::ip::tcp::socket serverSocket =
            listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        RunConnectionSession(
            session, tokenStore, tokenThrottle, trustStore, credentialThrottle,
            sessionManager, /*connection=*/1, activePlayContext, pairingSession,
            pairingNotificationSink, bridgeInstanceId, kBridgeVersion);
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(
        std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);

    ClientWriteText(clientWs, HelloMessage(kValidHexToken));
    auto helloAck = ClientReadEnvelope(clientWs);
    REQUIRE(helloAck.messageType == "hello_ack");
    REQUIRE(helloAck.playContextId.has_value());
    CHECK(*helloAck.playContextId == "context-1");

    auto capabilities = ClientReadEnvelope(clientWs);
    CHECK(capabilities.messageType == "capabilities");
    REQUIRE(capabilities.playContextId.has_value());
    CHECK(*capabilities.playContextId == "context-1");

    boost::system::error_code closeEc;
    clientWs.close(boost::beast::websocket::close_code::normal, closeEc);

    serverThread.join();

    REQUIRE_FALSE(serverAcceptEc);
}

TEST_CASE("RunConnectionSession closes without creating a session when the "
          "token is invalid",
          "[application][connection_session]") {
    boost::asio::io_context ioc;
    auto listener =
        LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());
    boost::asio::ip::tcp::endpoint endpoint = listener->LocalEndpoint();

    TokenStore tokenStore(*DecodeHex(kValidHexToken));
    FailedTokenThrottle tokenThrottle;
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    FailedTokenThrottle credentialThrottle;
    PairingSession pairingSession;
    RecordingPairingNotificationSink pairingNotificationSink;
    SessionManager sessionManager;
    EmptyActivePlayContext activePlayContext;
    auto start = std::chrono::steady_clock::now();
    for (int failure = 0; failure < 4; ++failure) {
        tokenThrottle.RecordFailure(start + std::chrono::seconds(4));
    }
    int clockCalls = 0;
    auto steadyNow = [&] {
        ++clockCalls;
        return clockCalls == 1 ? start : start + std::chrono::seconds(4);
    };

    boost::system::error_code serverAcceptEc;
    std::thread serverThread([&] {
        boost::asio::ip::tcp::socket serverSocket =
            listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        RunConnectionSession(
            session, tokenStore, tokenThrottle, trustStore, credentialThrottle,
            sessionManager, /*connection=*/1, activePlayContext, pairingSession,
            pairingNotificationSink, /*bridgeInstanceId=*/std::nullopt,
            kBridgeVersion, steadyNow);
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(
        std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);

    std::string wrongToken(64, 'f');
    ClientWriteText(clientWs, HelloMessage(wrongToken));

    auto error = ClientReadEnvelope(clientWs);
    CHECK(error.messageType == "error");
    auto errorPayload = dovahlink::protocol::DecodeErrorPayload(error.payload);
    REQUIRE(errorPayload.has_value());
    CHECK(errorPayload->code == "unauthenticated");

    //  The server closes the connection; the client's next read observes it.
    boost::beast::flat_buffer buffer;
    boost::system::error_code readEc;
    clientWs.read(buffer, readEc);

    serverThread.join();

    REQUIRE_FALSE(serverAcceptEc);
    CHECK(readEc);
    //  The real token was never consumed by a rejected attempt.
    CHECK(tokenStore.IsAvailable());
    //  The session's fifth failure occurred at simulated +4s, so all five
    //  remain active at +63s. Recording it at the pre-read start would let
    //  that one expire and incorrectly unblock authentication.
    CHECK(tokenThrottle.IsBlocked(start + std::chrono::seconds(63)));
}

TEST_CASE("RunConnectionSession's idle-loop read closes the connection once "
          "ConnectionTimeoutTracker's deadline has already elapsed, without "
          "waiting out any "
          "WebSocket-level timeout",
          "[application][connection_session]") {
    //  Proves the wiring added in this step: timeout.Deadline() actually reaches
    //  WebSocketSession::ReadMessage's own idleDeadline watchdog inside the
    //  message loop, not just the initial hello read. A client that kept answering
    //  WebSocket-level pings (now enabled via keep_alive_pings) would otherwise
    //  let Beast's own per-operation timeout run indefinitely; that mechanism
    //  itself is proven directly at transport/websocket_session_test.cpp's level.
    //  Here, an artificially already-elapsed idle deadline (manufactured via
    //  steadyNow, not a real 60-second wait) proves the connection still closes
    //  through this real session loop.
    boost::asio::io_context ioc;
    auto listener =
        LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());
    boost::asio::ip::tcp::endpoint endpoint = listener->LocalEndpoint();

    TokenStore tokenStore(*DecodeHex(kValidHexToken));
    FailedTokenThrottle tokenThrottle;
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    FailedTokenThrottle credentialThrottle;
    PairingSession pairingSession;
    RecordingPairingNotificationSink pairingNotificationSink;
    SessionManager sessionManager;
    EmptyActivePlayContext activePlayContext;
    auto start = std::chrono::steady_clock::now();
    int clockCalls = 0;
    //  The hello read itself must not be rejected as late (postReadNow must stay
    //  before the handshake deadline, start+5s), but authenticating at start-100s
    //  leaves the resulting idle deadline (postReadNow+60s = start-40s) already
    //  40s in the past relative to real time by the time the loop's read arms its
    //  watchdog moments later.
    auto steadyNow = [&] {
        ++clockCalls;
        return clockCalls == 1 ? start : start - std::chrono::seconds(100);
    };

    boost::system::error_code serverAcceptEc;
    std::thread serverThread([&] {
        boost::asio::ip::tcp::socket serverSocket =
            listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        RunConnectionSession(
            session, tokenStore, tokenThrottle, trustStore, credentialThrottle,
            sessionManager, /*connection=*/1, activePlayContext, pairingSession,
            pairingNotificationSink, /*bridgeInstanceId=*/std::nullopt,
            kBridgeVersion, steadyNow);
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(
        std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);

    ClientWriteText(clientWs, HelloMessage(kValidHexToken));
    auto helloAck = ClientReadEnvelope(clientWs);
    REQUIRE(helloAck.messageType == "hello_ack");
    REQUIRE(helloAck.sessionId.has_value());
    std::string sessionId = *helloAck.sessionId;

    auto capabilities = ClientReadEnvelope(clientWs);
    CHECK(capabilities.messageType == "capabilities");

    //  The client deliberately sends nothing else and does not close -- only the
    //  watchdog, racing the already-elapsed deadline, should end this connection.
    auto readStart = std::chrono::steady_clock::now();
    boost::beast::flat_buffer buffer;
    boost::system::error_code readEc;
    clientWs.read(buffer, readEc);
    auto readDuration = std::chrono::steady_clock::now() - readStart;

    serverThread.join();

    REQUIRE_FALSE(serverAcceptEc);
    CHECK(readEc);
    //  Well under the real 60s idle timeout the watchdog exists to bound, proving
    //  the already-past deadline -- not eventually waiting out Beast's own longer
    //  timer -- is what closed this.
    CHECK(readDuration < std::chrono::seconds(10));
    CHECK_FALSE(sessionManager.IsValidForConnection(sessionId, /*connection=*/1));
}

TEST_CASE(
    "RunConnectionSession's initial hello read closes the connection once "
    "ConnectionTimeoutTracker's handshake deadline has already elapsed, "
    "without waiting out "
    "any WebSocket-level timeout",
    "[application][connection_session]") {
    //  Symmetric with the idle-loop watchdog test above, but for the *other*
    //  ws.ReadMessage() call site this step changed: the very first hello read,
    //  before any session exists.
    boost::asio::io_context ioc;
    auto listener =
        LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());
    boost::asio::ip::tcp::endpoint endpoint = listener->LocalEndpoint();

    TokenStore tokenStore(*DecodeHex(kValidHexToken));
    FailedTokenThrottle tokenThrottle;
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    FailedTokenThrottle credentialThrottle;
    PairingSession pairingSession;
    RecordingPairingNotificationSink pairingNotificationSink;
    SessionManager sessionManager;
    EmptyActivePlayContext activePlayContext;
    //  The handshake deadline (constructor time + 5s) is already 5s in the past
    //  the moment the tracker is built, so the very first ReadMessage's watchdog
    //  has nothing to wait out.
    auto steadyNow = [] {
        return std::chrono::steady_clock::now() - std::chrono::seconds(10);
    };

    boost::system::error_code serverAcceptEc;
    std::thread serverThread([&] {
        boost::asio::ip::tcp::socket serverSocket =
            listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        RunConnectionSession(
            session, tokenStore, tokenThrottle, trustStore, credentialThrottle,
            sessionManager, /*connection=*/1, activePlayContext, pairingSession,
            pairingNotificationSink, /*bridgeInstanceId=*/std::nullopt,
            kBridgeVersion, steadyNow);
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(
        std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);
    //  The client deliberately never sends hello -- only the watchdog, racing the
    //  already-elapsed handshake deadline, should end this connection.

    auto readStart = std::chrono::steady_clock::now();
    boost::beast::flat_buffer buffer;
    boost::system::error_code readEc;
    clientWs.read(buffer, readEc);
    auto readDuration = std::chrono::steady_clock::now() - readStart;

    serverThread.join();

    REQUIRE_FALSE(serverAcceptEc);
    CHECK(readEc);
    //  Well under the real 5s handshake timeout the watchdog exists to bound.
    CHECK(readDuration < std::chrono::seconds(3));
}

TEST_CASE("RunConnectionSession closes with no hello_ack when hello arrives "
          "after the handshake "
          "deadline, even though no single socket read ever timed out",
          "[application][connection_session]") {
    boost::asio::io_context ioc;
    auto listener =
        LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());
    boost::asio::ip::tcp::endpoint endpoint = listener->LocalEndpoint();

    TokenStore tokenStore(*DecodeHex(kValidHexToken));
    FailedTokenThrottle tokenThrottle;
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    FailedTokenThrottle credentialThrottle;
    PairingSession pairingSession;
    RecordingPairingNotificationSink pairingNotificationSink;
    SessionManager sessionManager;
    EmptyActivePlayContext activePlayContext;

    boost::system::error_code serverAcceptEc;
    std::thread serverThread([&] {
        boost::asio::ip::tcp::socket serverSocket =
            listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        RunConnectionSession(session, tokenStore, tokenThrottle, trustStore,
                             credentialThrottle, sessionManager, /*connection=*/1,
                             activePlayContext, pairingSession,
                             pairingNotificationSink,
                             /*bridgeInstanceId=*/std::nullopt, kBridgeVersion);
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(
        std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);

    //  Each gap below is well under the 5-second handshake timeout, so no
    //  individual OS-level socket read (WebSocketSession::SetReadTimeout)
    //  ever fires. Sending pings in between (which the server's Beast read
    //  absorbs transparently without returning to RunConnectionSession)
    //  pushes the cumulative time before hello arrives past the 5-second
    //  window anyway, proving the application-level ConnectionTimeoutTracker
    //  check catches what SO_RCVTIMEO alone cannot.
    boost::system::error_code pingEc;
    std::this_thread::sleep_for(std::chrono::milliseconds(2200));
    clientWs.ping({}, pingEc);
    REQUIRE_FALSE(pingEc);
    std::this_thread::sleep_for(std::chrono::milliseconds(2200));
    clientWs.ping({}, pingEc);
    REQUIRE_FALSE(pingEc);
    std::this_thread::sleep_for(std::chrono::milliseconds(2200));

    ClientWriteText(clientWs, HelloMessage(kValidHexToken));

    boost::beast::flat_buffer buffer;
    boost::system::error_code readEc;
    clientWs.read(buffer, readEc);

    serverThread.join();

    REQUIRE_FALSE(serverAcceptEc);
    //  The server closed without ever answering hello: no hello_ack, no
    //  error -- the same "close with no response" behavior as an oversized
    //  frame or the session message cap.
    CHECK(readEc);
    CHECK(tokenStore.IsAvailable());
}

TEST_CASE("RunConnectionSession's disconnect notifies PairingSession, keeping "
          "an owned challenge "
          "reserved within the grace period",
          "[application][connection_session]") {
    boost::asio::io_context ioc;
    auto listener =
        LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());
    boost::asio::ip::tcp::endpoint endpoint = listener->LocalEndpoint();

    TokenStore tokenStore(*DecodeHex(kValidHexToken));
    FailedTokenThrottle tokenThrottle;
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    FailedTokenThrottle credentialThrottle;
    PairingSession pairingSession;
    RecordingPairingNotificationSink pairingNotificationSink;
    SessionManager sessionManager;
    EmptyActivePlayContext activePlayContext;

    auto start = std::chrono::steady_clock::now();
    //  Established directly through PairingSession's own API -- this test proves
    //  RunConnectionSession's disconnect wiring, not the pairing_request wire
    //  flow, which is covered elsewhere (pairing_handler_test.cpp,
    //  message_dispatcher_test.cpp).
    REQUIRE(pairingSession.TryStartChallenge("client-1", start).outcome ==
            StartChallengeOutcome::kStarted);

    //  Exactly three steadyNow() calls occur for a connection that completes hello
    //  and then closes without exchanging any further messages: the
    //  ConnectionTimeoutTracker constructor, postReadNow (also used for the
    //  reconnect notification), and the final teardown call that notifies
    //  PairingSession of the disconnect.
    int clockCalls = 0;
    auto steadyNow = [&] {
        ++clockCalls;
        if (clockCalls <= 2) {
            return start;
        }
        return start + std::chrono::seconds(2);
    };

    boost::system::error_code serverAcceptEc;
    std::thread serverThread([&] {
        boost::asio::ip::tcp::socket serverSocket =
            listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        RunConnectionSession(
            session, tokenStore, tokenThrottle, trustStore, credentialThrottle,
            sessionManager, /*connection=*/1, activePlayContext, pairingSession,
            pairingNotificationSink, /*bridgeInstanceId=*/std::nullopt,
            kBridgeVersion, steadyNow);
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(
        std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);

    ClientWriteText(clientWs, HelloMessage(kValidHexToken, "client-1"));
    auto helloAck = ClientReadEnvelope(clientWs);
    REQUIRE(helloAck.messageType == "hello_ack");
    auto capabilities = ClientReadEnvelope(clientWs);
    CHECK(capabilities.messageType == "capabilities");

    boost::system::error_code closeEc;
    clientWs.close(boost::beast::websocket::close_code::normal, closeEc);
    serverThread.join();
    REQUIRE_FALSE(serverAcceptEc);
    CHECK(clockCalls == 3);

    //  Disconnect landed at start+2s; well inside the 10-second grace period, a
    //  different device is still shut out, and the original owner's challenge is
    //  untouched.
    CHECK(pairingSession
              .TryStartChallenge("client-2", start + std::chrono::seconds(2 + 5))
              .outcome == StartChallengeOutcome::kOtherDeviceActive);
}

TEST_CASE("RunConnectionSession's disconnect notification lets an owned "
          "challenge's grace period "
          "elapse, freeing it for another device",
          "[application][connection_session]") {
    boost::asio::io_context ioc;
    auto listener =
        LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());
    boost::asio::ip::tcp::endpoint endpoint = listener->LocalEndpoint();

    TokenStore tokenStore(*DecodeHex(kValidHexToken));
    FailedTokenThrottle tokenThrottle;
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    FailedTokenThrottle credentialThrottle;
    PairingSession pairingSession;
    RecordingPairingNotificationSink pairingNotificationSink;
    SessionManager sessionManager;
    EmptyActivePlayContext activePlayContext;

    auto start = std::chrono::steady_clock::now();
    REQUIRE(pairingSession.TryStartChallenge("client-1", start).outcome ==
            StartChallengeOutcome::kStarted);

    int clockCalls = 0;
    auto steadyNow = [&] {
        ++clockCalls;
        if (clockCalls <= 2) {
            return start;
        }
        return start + std::chrono::seconds(2);
    };

    boost::system::error_code serverAcceptEc;
    std::thread serverThread([&] {
        boost::asio::ip::tcp::socket serverSocket =
            listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        RunConnectionSession(
            session, tokenStore, tokenThrottle, trustStore, credentialThrottle,
            sessionManager, /*connection=*/1, activePlayContext, pairingSession,
            pairingNotificationSink, /*bridgeInstanceId=*/std::nullopt,
            kBridgeVersion, steadyNow);
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(
        std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);

    ClientWriteText(clientWs, HelloMessage(kValidHexToken, "client-1"));
    auto helloAck = ClientReadEnvelope(clientWs);
    REQUIRE(helloAck.messageType == "hello_ack");
    auto capabilities = ClientReadEnvelope(clientWs);
    CHECK(capabilities.messageType == "capabilities");

    boost::system::error_code closeEc;
    clientWs.close(boost::beast::websocket::close_code::normal, closeEc);
    serverThread.join();
    REQUIRE_FALSE(serverAcceptEc);
    CHECK(clockCalls == 3);

    //  Disconnect landed at start+2s; past the 10-second grace period
    //  (start+2s+11s), the slot is genuinely free for a different device.
    auto started = pairingSession.TryStartChallenge(
        "client-2", start + std::chrono::seconds(2 + 11));
    CHECK(started.outcome == StartChallengeOutcome::kStarted);
    REQUIRE(started.code.has_value());
}

TEST_CASE("RunConnectionSession's successful hello notifies PairingSession of "
          "a reconnect, clearing "
          "a pending grace countdown from a prior disconnect",
          "[application][connection_session]") {
    boost::asio::io_context ioc;
    auto listener =
        LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());
    boost::asio::ip::tcp::endpoint endpoint = listener->LocalEndpoint();

    TokenStore tokenStore(*DecodeHex(kValidHexToken));
    FailedTokenThrottle tokenThrottle;
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    FailedTokenThrottle credentialThrottle;
    PairingSession pairingSession;
    RecordingPairingNotificationSink pairingNotificationSink;
    SessionManager sessionManager;
    EmptyActivePlayContext activePlayContext;

    auto start = std::chrono::steady_clock::now();
    REQUIRE(pairingSession.TryStartChallenge("client-1", start).outcome ==
            StartChallengeOutcome::kStarted);
    //  Simulates a prior dropped connection that already started the grace
    //  countdown, before this test's own connection (the "reconnect") is even
    //  accepted.
    pairingSession.NotifyDisconnected("client-1", start);

    boost::system::error_code serverAcceptEc;
    std::thread serverThread([&] {
        boost::asio::ip::tcp::socket serverSocket =
            listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        RunConnectionSession(session, tokenStore, tokenThrottle, trustStore,
                             credentialThrottle, sessionManager, /*connection=*/1,
                             activePlayContext, pairingSession,
                             pairingNotificationSink,
                             /*bridgeInstanceId=*/std::nullopt, kBridgeVersion);
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(
        std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);

    ClientWriteText(clientWs, HelloMessage(kValidHexToken, "client-1"));
    auto helloAck = ClientReadEnvelope(clientWs);
    REQUIRE(helloAck.messageType == "hello_ack");
    //  capabilities is sent strictly after this session's own NotifyReconnected
    //  call in program order on the server thread, so reading it here
    //  happens-after that call actually ran.
    auto capabilities = ClientReadEnvelope(clientWs);
    CHECK(capabilities.messageType == "capabilities");

    //  Even past what would have been the original disconnect's grace deadline,
    //  the challenge is still client-1's -- the reconnect cleared the countdown
    //  rather than leaving it running.
    auto pastOriginalGrace = start + std::chrono::seconds(11);
    CHECK(
        pairingSession.TryStartChallenge("client-2", pastOriginalGrace).outcome ==
        StartChallengeOutcome::kOtherDeviceActive);

    boost::system::error_code closeEc;
    clientWs.close(boost::beast::websocket::close_code::normal, closeEc);
    serverThread.join();
    REQUIRE_FALSE(serverAcceptEc);
}

TEST_CASE("RunConnectionSession's disconnect/reconnect wiring works on an "
          "unpaired session, the "
          "trust tier pairing actually runs under",
          "[application][connection_session]") {
    boost::asio::io_context ioc;
    auto listener =
        LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());
    boost::asio::ip::tcp::endpoint endpoint = listener->LocalEndpoint();

    TokenStore tokenStore(*DecodeHex(kValidHexToken));
    FailedTokenThrottle tokenThrottle;
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    FailedTokenThrottle credentialThrottle;
    PairingSession pairingSession;
    RecordingPairingNotificationSink pairingNotificationSink;
    SessionManager sessionManager;
    EmptyActivePlayContext activePlayContext;

    auto start = std::chrono::steady_clock::now();
    REQUIRE(pairingSession.TryStartChallenge("client-1", start).outcome ==
            StartChallengeOutcome::kStarted);

    int clockCalls = 0;
    auto steadyNow = [&] {
        ++clockCalls;
        if (clockCalls <= 2) {
            return start;
        }
        return start + std::chrono::seconds(2);
    };

    boost::system::error_code serverAcceptEc;
    std::thread serverThread([&] {
        boost::asio::ip::tcp::socket serverSocket =
            listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        RunConnectionSession(
            session, tokenStore, tokenThrottle, trustStore, credentialThrottle,
            sessionManager, /*connection=*/1, activePlayContext, pairingSession,
            pairingNotificationSink, /*bridgeInstanceId=*/std::nullopt,
            kBridgeVersion, steadyNow);
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(
        std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);

    ClientWriteText(clientWs, UnpairedHelloMessage("client-1"));
    auto helloAck = ClientReadEnvelope(clientWs);
    REQUIRE(helloAck.messageType == "hello_ack");
    auto capabilities = ClientReadEnvelope(clientWs);
    CHECK(capabilities.messageType == "capabilities");

    boost::system::error_code closeEc;
    clientWs.close(boost::beast::websocket::close_code::normal, closeEc);
    serverThread.join();
    REQUIRE_FALSE(serverAcceptEc);
    CHECK(clockCalls == 3);

    //  Disconnect landed at start+2s; well inside the 10-second grace period, a
    //  different device is still shut out. Proves the wiring fires the same way on
    //  the unpaired tier the pairing flow actually authenticates on, not just the
    //  developer-token tier the other tests above use.
    CHECK(pairingSession
              .TryStartChallenge("client-2", start + std::chrono::seconds(2 + 5))
              .outcome == StartChallengeOutcome::kOtherDeviceActive);
}

TEST_CASE("RunConnectionSession's reconnect notification for a client owning "
          "nothing is a harmless "
          "no-op that leaves an unrelated client's owned challenge untouched",
          "[application][connection_session]") {
    boost::asio::io_context ioc;
    auto listener =
        LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());
    boost::asio::ip::tcp::endpoint endpoint = listener->LocalEndpoint();

    TokenStore tokenStore(*DecodeHex(kValidHexToken));
    FailedTokenThrottle tokenThrottle;
    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    FailedTokenThrottle credentialThrottle;
    PairingSession pairingSession;
    RecordingPairingNotificationSink pairingNotificationSink;
    SessionManager sessionManager;
    EmptyActivePlayContext activePlayContext;

    auto start = std::chrono::steady_clock::now();
    //  "client-1" owns an active challenge; the connection under test
    //  authenticates as the unrelated "client-3", which owns nothing in
    //  PairingSession.
    REQUIRE(pairingSession.TryStartChallenge("client-1", start).outcome ==
            StartChallengeOutcome::kStarted);

    boost::system::error_code serverAcceptEc;
    std::thread serverThread([&] {
        boost::asio::ip::tcp::socket serverSocket =
            listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        RunConnectionSession(session, tokenStore, tokenThrottle, trustStore,
                             credentialThrottle, sessionManager, /*connection=*/1,
                             activePlayContext, pairingSession,
                             pairingNotificationSink,
                             /*bridgeInstanceId=*/std::nullopt, kBridgeVersion);
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(
        std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);

    ClientWriteText(clientWs, UnpairedHelloMessage("client-3"));
    auto helloAck = ClientReadEnvelope(clientWs);
    REQUIRE(helloAck.messageType == "hello_ack");
    //  capabilities is sent strictly after this session's own
    //  NotifyReconnected("client-3", ...) call in program order on the server
    //  thread, so reading it here happens-after that call ran.
    auto capabilities = ClientReadEnvelope(clientWs);
    CHECK(capabilities.messageType == "capabilities");

    //  client-1's challenge is exactly as it was: "client-3" reconnecting (owning
    //  nothing) never touched it.
    CHECK(pairingSession.TryStartChallenge("client-1", start).outcome ==
          StartChallengeOutcome::kResumed);
    CHECK(pairingSession.TryStartChallenge("client-2", start).outcome ==
          StartChallengeOutcome::kOtherDeviceActive);

    boost::system::error_code closeEc;
    clientWs.close(boost::beast::websocket::close_code::normal, closeEc);
    serverThread.join();
    REQUIRE_FALSE(serverAcceptEc);
}

TEST_CASE("ConnectionSession's real behavior is reachable through "
          "IConnectionSession",
          "[application][connection_session][i_connection_session]") {
    StrictMock<MockWebSocketSession> mockWs;
    EXPECT_CALL(mockWs, Accept())
        .WillOnce(Return(std::unexpected(SessionError::kHandshakeFailed)));
    StrictMock<MockHandshakeHandler> mockHandshakeHandler;

    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    PairingSession pairingSession;
    RecordingPairingNotificationSink pairingNotificationSink;
    SessionManager sessionManager;
    EmptyActivePlayContext activePlayContext;
    dovahlink::application::TrustMutationCoordinator mutationCoordinator(
        trustStore, pairingSession, sessionManager);
    dovahlink::application::PairingHandler pairingHandler(
        pairingSession, mutationCoordinator, pairingNotificationSink);
    dovahlink::application::MessageDispatcher messageDispatcher(
        sessionManager, trustStore, mutationCoordinator, pairingHandler,
        activePlayContext, /*bridgeInstanceId=*/std::nullopt);
    dovahlink::application::ConnectionSession connectionSession(
        mockHandshakeHandler, messageDispatcher, activePlayContext,
        pairingSession, /*bridgeInstanceId=*/std::nullopt);
    IConnectionSession& contract = connectionSession;

    //  Only proves the interface dispatches to the real implementation; the
    //  StrictMock below already proves this exact "Accept fails" branch calls
    //  nothing else, so that assertion is not repeated here.
    contract.Run(mockWs, /*connection=*/1);
}

TEST_CASE("RunConnectionSession makes no other session calls when Accept fails",
          "[application][connection_session][i_web_socket_session]") {
    StrictMock<MockWebSocketSession> mockWs;
    EXPECT_CALL(mockWs, Accept())
        .WillOnce(Return(std::unexpected(SessionError::kHandshakeFailed)));
    StrictMock<MockHandshakeHandler> mockHandshakeHandler;

    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    PairingSession pairingSession;
    RecordingPairingNotificationSink pairingNotificationSink;
    SessionManager sessionManager;
    EmptyActivePlayContext activePlayContext;
    dovahlink::application::TrustMutationCoordinator mutationCoordinator(
        trustStore, pairingSession, sessionManager);
    dovahlink::application::PairingHandler pairingHandler(
        pairingSession, mutationCoordinator, pairingNotificationSink);
    dovahlink::application::MessageDispatcher messageDispatcher(
        sessionManager, trustStore, mutationCoordinator, pairingHandler,
        activePlayContext, /*bridgeInstanceId=*/std::nullopt);
    dovahlink::application::ConnectionSession connectionSession(
        mockHandshakeHandler, messageDispatcher, activePlayContext,
        pairingSession, /*bridgeInstanceId=*/std::nullopt);

    connectionSession.Run(mockWs, /*connection=*/1);
}

TEST_CASE("RunConnectionSession closes without writing when the hello read "
          "fails",
          "[application][connection_session][i_web_socket_session]") {
    StrictMock<MockWebSocketSession> mockWs;
    {
        testing::InSequence sequence;
        EXPECT_CALL(mockWs, Accept())
            .WillOnce(Return(std::expected<void, SessionError>{}));
        EXPECT_CALL(mockWs, ReadMessage(testing::_))
            .WillOnce(Return(std::unexpected(SessionError::kReadFailed)));
        EXPECT_CALL(mockWs, Close());
    }
    StrictMock<MockHandshakeHandler> mockHandshakeHandler;

    EmptyPersistence persistence;
    auto trustStore = TrustStore::Load(persistence);
    PairingSession pairingSession;
    RecordingPairingNotificationSink pairingNotificationSink;
    SessionManager sessionManager;
    EmptyActivePlayContext activePlayContext;
    dovahlink::application::TrustMutationCoordinator mutationCoordinator(
        trustStore, pairingSession, sessionManager);
    dovahlink::application::PairingHandler pairingHandler(
        pairingSession, mutationCoordinator, pairingNotificationSink);
    dovahlink::application::MessageDispatcher messageDispatcher(
        sessionManager, trustStore, mutationCoordinator, pairingHandler,
        activePlayContext, /*bridgeInstanceId=*/std::nullopt);
    dovahlink::application::ConnectionSession connectionSession(
        mockHandshakeHandler, messageDispatcher, activePlayContext,
        pairingSession, /*bridgeInstanceId=*/std::nullopt);

    connectionSession.Run(mockWs, /*connection=*/1);
}

TEST_CASE("RunConnectionSession dispatches an inbound message through "
          "IMessageDispatcher and sends its responses",
          "[application][connection_session][i_message_dispatcher]") {
    std::string helloRaw = HelloMessage(kValidHexToken);
    std::string pingRaw = PingMessage("session-1");

    StrictMock<MockWebSocketSession> mockWs;
    {
        testing::InSequence sequence;
        EXPECT_CALL(mockWs, Accept())
            .WillOnce(Return(std::expected<void, SessionError>{}));
        EXPECT_CALL(mockWs, ReadMessage(testing::_))
            .WillOnce(Return(helloRaw));
        EXPECT_CALL(mockWs, WriteMessage(testing::_)) //  hello_ack
            .WillOnce(Return(std::expected<void, SessionError>{}));
        EXPECT_CALL(mockWs, SwitchToIdleTimeout());
        EXPECT_CALL(mockWs, WriteMessage(testing::_)) //  capabilities
            .WillOnce(Return(std::expected<void, SessionError>{}));
        EXPECT_CALL(mockWs, ReadMessage(testing::_))
            .WillOnce(Return(pingRaw));
        EXPECT_CALL(mockWs, WriteMessage(testing::_)) //  dispatched response
            .WillOnce(Return(std::expected<void, SessionError>{}));
        EXPECT_CALL(mockWs, ReadMessage(testing::_))
            .WillOnce(Return(std::unexpected(SessionError::kReadFailed)));
        EXPECT_CALL(mockWs, Close());
    }

    StrictMock<MockHandshakeHandler> mockHandshakeHandler;
    EXPECT_CALL(mockHandshakeHandler,
                Handle(testing::_, testing::_, testing::_, testing::_))
        .WillOnce(testing::Invoke(
            [](const Envelope&, dovahlink::application::ConnectionId,
               dovahlink::application::IConnectionTimeoutTracker&,
               std::chrono::steady_clock::time_point) {
                return dovahlink::application::HandshakeResult{
                    .response =
                        dovahlink::protocol::Envelope{
                            .messageType = "hello_ack",
                            .messageId = "message-hello-ack-1",
                            .sessionId = std::string("session-1"),
                            .correlationId = std::nullopt,
                            .payload = boost::json::object{},
                            .clientId = std::string("client-1"),
                        },
                    .sessionLease = dovahlink::shared::ScopedRelease([] {}),
                    .closeConnection = false,
                };
            }));

    StrictMock<MockMessageDispatcher> mockMessageDispatcher;
    EXPECT_CALL(
        mockMessageDispatcher,
        Process(pingRaw, testing::_, testing::_, testing::_, testing::_,
                testing::_, testing::_, testing::_, testing::_))
        .WillOnce(testing::Return(dovahlink::application::DispatchResult{
            .responses = {dovahlink::protocol::Envelope{
                .messageType = "pong",
                .messageId = "message-pong-1",
                .sessionId = std::string("session-1"),
                .correlationId = std::nullopt,
                .payload = boost::json::object{},
            }},
        }));

    PairingSession pairingSession;
    EmptyActivePlayContext activePlayContext;
    dovahlink::application::ConnectionSession connectionSession(
        mockHandshakeHandler, mockMessageDispatcher, activePlayContext,
        pairingSession, /*bridgeInstanceId=*/std::nullopt);

    connectionSession.Run(mockWs, /*connection=*/1);
}

TEST_CASE("RunConnectionSession closes the connection when IMessageDispatcher "
          "requests it, without reading another message",
          "[application][connection_session][i_message_dispatcher]") {
    std::string helloRaw = HelloMessage(kValidHexToken);
    std::string pingRaw = PingMessage("session-1");

    StrictMock<MockWebSocketSession> mockWs;
    {
        testing::InSequence sequence;
        EXPECT_CALL(mockWs, Accept())
            .WillOnce(Return(std::expected<void, SessionError>{}));
        EXPECT_CALL(mockWs, ReadMessage(testing::_))
            .WillOnce(Return(helloRaw));
        EXPECT_CALL(mockWs, WriteMessage(testing::_)) //  hello_ack
            .WillOnce(Return(std::expected<void, SessionError>{}));
        EXPECT_CALL(mockWs, SwitchToIdleTimeout());
        EXPECT_CALL(mockWs, WriteMessage(testing::_)) //  capabilities
            .WillOnce(Return(std::expected<void, SessionError>{}));
        EXPECT_CALL(mockWs, ReadMessage(testing::_))
            .WillOnce(Return(pingRaw));
        EXPECT_CALL(mockWs, WriteMessage(testing::_)) //  dispatched response
            .WillOnce(Return(std::expected<void, SessionError>{}));
        EXPECT_CALL(mockWs, Close());
        //  No further ReadMessage: closeConnection ends the loop immediately.
    }

    StrictMock<MockHandshakeHandler> mockHandshakeHandler;
    EXPECT_CALL(mockHandshakeHandler,
                Handle(testing::_, testing::_, testing::_, testing::_))
        .WillOnce(testing::Invoke(
            [](const Envelope&, dovahlink::application::ConnectionId,
               dovahlink::application::IConnectionTimeoutTracker&,
               std::chrono::steady_clock::time_point) {
                return dovahlink::application::HandshakeResult{
                    .response =
                        dovahlink::protocol::Envelope{
                            .messageType = "hello_ack",
                            .messageId = "message-hello-ack-1",
                            .sessionId = std::string("session-1"),
                            .correlationId = std::nullopt,
                            .payload = boost::json::object{},
                            .clientId = std::string("client-1"),
                        },
                    .sessionLease = dovahlink::shared::ScopedRelease([] {}),
                    .closeConnection = false,
                };
            }));

    StrictMock<MockMessageDispatcher> mockMessageDispatcher;
    EXPECT_CALL(
        mockMessageDispatcher,
        Process(pingRaw, testing::_, testing::_, testing::_, testing::_,
                testing::_, testing::_, testing::_, testing::_))
        .WillOnce(testing::Return(dovahlink::application::DispatchResult{
            .responses = {dovahlink::protocol::Envelope{
                .messageType = "error",
                .messageId = "message-error-1",
                .sessionId = std::string("session-1"),
                .correlationId = std::nullopt,
                .payload = boost::json::object{},
            }},
            .closeConnection = true,
        }));

    PairingSession pairingSession;
    EmptyActivePlayContext activePlayContext;
    dovahlink::application::ConnectionSession connectionSession(
        mockHandshakeHandler, mockMessageDispatcher, activePlayContext,
        pairingSession, /*bridgeInstanceId=*/std::nullopt);

    connectionSession.Run(mockWs, /*connection=*/1);
}
