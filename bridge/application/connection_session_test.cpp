#include "application/connection_session.hpp"

#include "protocol/bounded_json.hpp"
#include "protocol/envelope.hpp"
#include "protocol/messages.hpp"
#include "security/hex.hpp"
#include "transport/listener.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/asio/buffer.hpp>
#include <boost/asio/io_context.hpp>
#include <boost/asio/ip/tcp.hpp>
#include <boost/beast/core/buffers_to_string.hpp>
#include <boost/beast/core/flat_buffer.hpp>
#include <boost/beast/websocket/error.hpp>
#include <boost/beast/websocket/stream.hpp>
#include <boost/system/error_code.hpp>

#include <chrono>
#include <string>
#include <string_view>
#include <thread>
#include <utility>
#include <vector>

using dovahlink::application::ActivePlayContext;
using dovahlink::application::PairingNotificationSink;
using dovahlink::application::RunConnectionSession;
using dovahlink::application::SessionManager;
using dovahlink::protocol::Envelope;
using dovahlink::security::DecodeHex;
using dovahlink::security::FailedTokenThrottle;
using dovahlink::security::ITrustStorePersistence;
using dovahlink::security::PairingSession;
using dovahlink::security::StartChallengeOutcome;
using dovahlink::security::TokenStore;
using dovahlink::security::TrustStore;
using dovahlink::security::TrustStoreSnapshot;
using dovahlink::transport::LoopbackListener;
using dovahlink::transport::WebSocketSession;

// These tests use two real loopback sockets (client and server) with a real
// Boost.Beast WebSocket handshake and application-level protocol exchange,
// matching this project's established convention that transport-adjacent
// code is proven against real sockets rather than fakes (ai/context/
// integration/testing.md). Catch2 assertions are never called from the
// background server thread; results are captured into variables and
// checked only after the thread is joined onto the main test thread,
// matching transport/websocket_session_test.cpp's own precedent.

namespace {

constexpr const char* kValidHexToken = "0123456789abcdefABCDEF00112233445566778899aabbccddeeff0011223344";
constexpr const char* kBridgeVersion = "0.2.0";

/// `ITrustStorePersistence` double that always loads an empty snapshot -- these tests only
/// exercise the one_time_local_token auth path and never touch the trust store.
class EmptyPersistence : public ITrustStorePersistence {
public:
    /// Always reports a valid, empty snapshot.
    std::optional<TrustStoreSnapshot> Load() override { return TrustStoreSnapshot{}; }

    /// Always succeeds without recording anything.
    bool Save(const TrustStoreSnapshot&) override { return true; }
};

/// `PairingNotificationSink` double that records every code it is given. These tests only need a
/// working sink to satisfy `RunConnectionSession`'s signature -- pairing behavior itself is
/// exercised in message_dispatcher_test.cpp and pairing_handler_test.cpp.
class RecordingPairingNotificationSink : public PairingNotificationSink {
public:
    /// Appends `sixDigitCode` to `codes`.
    void NotifyPairingCodeAvailable(std::string_view sixDigitCode) override {
        codes.emplace_back(sixDigitCode);
    }

    /// No-op: satisfies the interface only, matching this fake's existing minimal-footprint intent.
    void NotifyPairingCodeIncorrect(std::string_view) override {}

    /// No-op: satisfies the interface only, matching this fake's existing minimal-footprint intent.
    void NotifyPairingAttemptsExhausted() override {}

    /// Every code this sink has been given, in order.
    std::vector<std::string> codes;
};

/// Reads and decodes one protocol envelope from the test WebSocket.
Envelope ClientReadEnvelope(boost::beast::websocket::stream<boost::asio::ip::tcp::socket>& clientWs) {
    boost::beast::flat_buffer buffer;
    boost::system::error_code ec;
    clientWs.read(buffer, ec);
    REQUIRE_FALSE(ec);
    auto parsed = dovahlink::protocol::ParseBoundedJson(boost::beast::buffers_to_string(buffer.data()));
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE(envelope.has_value());
    return std::move(*envelope);
}

/// Writes one text protocol message to the test WebSocket.
void ClientWriteText(boost::beast::websocket::stream<boost::asio::ip::tcp::socket>& clientWs,
                      const std::string& text) {
    clientWs.text(true);
    boost::system::error_code ec;
    clientWs.write(boost::asio::buffer(text), ec);
    REQUIRE_FALSE(ec);
}

/// Builds a hello message using the supplied authentication token and clientId.
std::string HelloMessage(const std::string& token, std::string clientId = "client-1") {
    return R"({"messageType": "hello", "messageId": "message-hello-1", )"
           R"("sessionId": null, "correlationId": null, "payload": {"endpoint": "client", )"
           R"("clientId": ")" +
           clientId + R"(", "auth": {"method": "one_time_local_token", "token": ")" + token +
           R"("}}, "bridgeInstanceId": null, "playContextId": null, "clientId": null})";
}

/// Builds an unpaired-tier hello message (no credential) for the given clientId -- the actual
/// trust tier the pairing flow runs under, per `ai/context/protocol/security.md`'s "Hello
/// authentication and session trust tiers".
std::string UnpairedHelloMessage(std::string clientId) {
    return R"({"messageType": "hello", "messageId": "message-hello-1", )"
           R"("sessionId": null, "correlationId": null, "payload": {"endpoint": "client", )"
           R"("clientId": ")" +
           clientId + R"(", "auth": {"method": "unpaired"}}, )"
           R"("bridgeInstanceId": null, "playContextId": null, "clientId": null})";
}

/// Builds a ping message for an established session.
std::string PingMessage(const std::string& sessionId) {
    return R"({"messageType": "ping", "messageId": "message-ping-1", "sessionId": ")" + sessionId +
           R"(", "correlationId": null, "payload": {}, )"
           R"("bridgeInstanceId": null, "playContextId": null, "clientId": null})";
}

/// Builds a subscribe message for the character state area on an established session.
std::string SubscribeMessage(const std::string& sessionId, std::string messageId = "message-sub-1") {
    return R"({"messageType": "subscribe", "messageId": ")" + messageId + R"(", "sessionId": ")" + sessionId +
           R"(", "correlationId": null, "payload": {"stateAreas": ["character"]}, )"
           R"("bridgeInstanceId": null, "playContextId": null, "clientId": null})";
}

/// Builds a snapshot_request message for the character state area on an established session.
std::string SnapshotRequestMessage(const std::string& sessionId, std::string messageId = "message-snap-1") {
    return R"({"messageType": "snapshot_request", "messageId": ")" + messageId + R"(", "sessionId": ")" + sessionId +
           R"(", "correlationId": null, "payload": {"stateArea": "character"}, )"
           R"("bridgeInstanceId": null, "playContextId": null, "clientId": null})";
}

}  // namespace

TEST_CASE("RunConnectionSession completes hello, capabilities, ping, and subscribe over a real socket",
          "[application][connection_session]") {
    boost::asio::io_context ioc;
    auto listener = LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
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
    ActivePlayContext activePlayContext;
    auto context = activePlayContext.Begin("context-1");
    context->characterState.OnLevelCaptured(30);
    auto start = std::chrono::steady_clock::now();
    int clockCalls = 0;
    // Simulate a hello completing at +4s, then authenticated traffic at
    // +63s. The ping is 59s after authentication and must remain inside
    // the 60s idle window; using the pre-read time would close it at +60s.
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
        boost::asio::ip::tcp::socket serverSocket = listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        RunConnectionSession(session, tokenStore, tokenThrottle, trustStore, credentialThrottle, sessionManager, /*connection=*/1, activePlayContext,
                             pairingSession, pairingNotificationSink, /*bridgeInstanceId=*/std::nullopt, kBridgeVersion, steadyNow);
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
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
    auto snapshot = ClientReadEnvelope(clientWs);
    CHECK(snapshot.messageType == "state_snapshot");
    auto decodedSnapshot = dovahlink::protocol::DecodeStateSnapshotPayload(snapshot.payload);
    REQUIRE(decodedSnapshot.has_value());
    auto characterState = dovahlink::protocol::DecodeCharacterState(decodedSnapshot->data);
    REQUIRE(characterState.has_value());
    REQUIRE(characterState->level.has_value());
    CHECK(*characterState->level == 30);

    boost::system::error_code closeEc;
    clientWs.close(boost::beast::websocket::close_code::normal, closeEc);

    serverThread.join();

    REQUIRE_FALSE(serverAcceptEc);
    // Disconnect must invalidate the session (ai/context/protocol/security.md).
    CHECK_FALSE(sessionManager.IsValidForConnection(sessionId, /*connection=*/1));
    CHECK(clockCalls >= 4);
}

TEST_CASE("RunConnectionSession stamps bridgeInstanceId on every response, not just hello_ack",
          "[application][connection_session]") {
    boost::asio::io_context ioc;
    auto listener = LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
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
    ActivePlayContext activePlayContext;
    std::optional<std::string> bridgeInstanceId = "bridge-1";

    boost::system::error_code serverAcceptEc;
    std::thread serverThread([&] {
        boost::asio::ip::tcp::socket serverSocket = listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        RunConnectionSession(session, tokenStore, tokenThrottle, trustStore, credentialThrottle, sessionManager, /*connection=*/1, activePlayContext,
                             pairingSession, pairingNotificationSink, bridgeInstanceId, kBridgeVersion);
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
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
    // The end-to-end point of this test: bridgeInstanceId reaches the wire,
    // and playContextId is null (no play context is active in this test).
    REQUIRE(helloAck.bridgeInstanceId.has_value());
    CHECK(*helloAck.bridgeInstanceId == "bridge-1");
    CHECK_FALSE(helloAck.playContextId.has_value());
    // hello_ack is the one exception: it echoes the client-provided identity
    // once, confirming what was accepted at authentication.
    REQUIRE(helloAck.clientId.has_value());
    CHECK(*helloAck.clientId == "client-1");
    // The real end-to-end point: the session manager RunConnectionSession
    // authenticated against, not a synthetic one, now owns the client
    // identity as session state. TryCreateSession (and thus this) commits
    // before HandleHello returns and its response is written, which
    // happens-before the client finishing this read.
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
    // Every post-hello_ack response derives the client from session state
    // rather than repeating it on the wire.
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
    auto snapshot = ClientReadEnvelope(clientWs);
    CHECK(snapshot.messageType == "state_snapshot");
    REQUIRE(snapshot.sessionId.has_value());
    CHECK(*snapshot.sessionId == sessionId);
    REQUIRE(snapshot.bridgeInstanceId.has_value());
    CHECK(*snapshot.bridgeInstanceId == "bridge-1");
    CHECK_FALSE(snapshot.clientId.has_value());
    CHECK_FALSE(snapshot.playContextId.has_value());

    boost::system::error_code closeEc;
    clientWs.close(boost::beast::websocket::close_code::normal, closeEc);

    serverThread.join();

    REQUIRE_FALSE(serverAcceptEc);
}

TEST_CASE("RunConnectionSession's unsolicited capabilities envelope carries a real playContextId "
          "when a context is already active at connect time",
          "[application][connection_session]") {
    // The capabilities envelope is built outside ProcessInboundMessage's own
    // stamping loop (BuildBridgeCapabilities, sent once right after
    // hello_ack); this is its own, separately wired stamping site.
    boost::asio::io_context ioc;
    auto listener = LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
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
    ActivePlayContext activePlayContext;
    activePlayContext.Begin("context-1");
    std::optional<std::string> bridgeInstanceId = "bridge-1";

    boost::system::error_code serverAcceptEc;
    std::thread serverThread([&] {
        boost::asio::ip::tcp::socket serverSocket = listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        RunConnectionSession(session, tokenStore, tokenThrottle, trustStore, credentialThrottle, sessionManager, /*connection=*/1, activePlayContext,
                             pairingSession, pairingNotificationSink, bridgeInstanceId, kBridgeVersion);
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
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

TEST_CASE("RunConnectionSession's revision survives a reconnect that reuses the same play context",
          "[application][connection_session]") {
    // For every connection, ActivePlayContext (and the RevisionTracker it
    // owns) is pool-lifetime, not per-connection, so a reconnect to the same
    // still-active play context must NOT reset the revision sequence.
    boost::asio::io_context ioc;
    auto listener = LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());
    boost::asio::ip::tcp::endpoint endpoint = listener->LocalEndpoint();

    // Each connection gets its own TokenStore/throttle: the one-time-token
    // model has no mechanism yet to reissue a token for a same-instance
    // reconnect (that is Phase 3's "Local Device Pairing and Reconnection"
    // scope, per ROADMAP.md's Phase 1 acceptance note on this exact
    // limitation). This test isolates the concern actually in scope here --
    // does the revision survive a reconnect -- from how a reconnect
    // authenticates, which is unbuilt and irrelevant to that question.
    TokenStore firstTokenStore(*DecodeHex(kValidHexToken));
    FailedTokenThrottle firstTokenThrottle;
    EmptyPersistence firstPersistence;
    auto firstTrustStore = TrustStore::Load(firstPersistence);
    FailedTokenThrottle firstCredentialThrottle;
    // SessionManager and ActivePlayContext are shared across both
    // connections below, matching how BridgeWorkerPool actually owns them at
    // pool lifetime rather than per-connection.
    SessionManager sessionManager;
    PairingSession pairingSession;
    RecordingPairingNotificationSink pairingNotificationSink;
    ActivePlayContext activePlayContext;
    auto context = activePlayContext.Begin("context-1");
    context->characterState.OnLevelCaptured(5);
    std::optional<std::string> bridgeInstanceId = "bridge-1";

    // --- First connection: establish a non-trivial revision via a real change. ---
    boost::system::error_code firstAcceptEc;
    std::thread firstServerThread([&] {
        boost::asio::ip::tcp::socket serverSocket = listener->Acceptor().accept(firstAcceptEc);
        if (firstAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        RunConnectionSession(session, firstTokenStore, firstTokenThrottle, firstTrustStore, firstCredentialThrottle, sessionManager, /*connection=*/1,
                             activePlayContext, pairingSession, pairingNotificationSink, bridgeInstanceId, kBridgeVersion);
    });

    boost::asio::ip::tcp::socket firstClientSocket(ioc);
    boost::system::error_code firstConnectEc;
    firstClientSocket.connect(endpoint, firstConnectEc);
    REQUIRE_FALSE(firstConnectEc);
    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> firstClientWs(std::move(firstClientSocket));
    boost::system::error_code firstHandshakeEc;
    firstClientWs.handshake("127.0.0.1", "/", firstHandshakeEc);
    REQUIRE_FALSE(firstHandshakeEc);

    ClientWriteText(firstClientWs, HelloMessage(kValidHexToken));
    auto firstHelloAck = ClientReadEnvelope(firstClientWs);
    REQUIRE(firstHelloAck.messageType == "hello_ack");
    REQUIRE(firstHelloAck.sessionId.has_value());
    REQUIRE(firstHelloAck.playContextId.has_value());
    CHECK(*firstHelloAck.playContextId == "context-1");
    REQUIRE(firstHelloAck.bridgeInstanceId.has_value());
    CHECK(*firstHelloAck.bridgeInstanceId == "bridge-1");
    std::string firstSessionId = *firstHelloAck.sessionId;
    auto firstCapabilities = ClientReadEnvelope(firstClientWs);
    CHECK(firstCapabilities.messageType == "capabilities");

    ClientWriteText(firstClientWs, SubscribeMessage(firstSessionId));
    auto firstAck = ClientReadEnvelope(firstClientWs);
    CHECK(firstAck.messageType == "subscription_ack");
    auto firstSnapshot = ClientReadEnvelope(firstClientWs);
    auto firstDecoded = dovahlink::protocol::DecodeStateSnapshotPayload(firstSnapshot.payload);
    REQUIRE(firstDecoded.has_value());
    CHECK(firstDecoded->revision == 1);

    // A real change before the next pull, still on this same connection.
    context->characterState.OnLevelCaptured(6);
    ClientWriteText(firstClientWs, SnapshotRequestMessage(firstSessionId));
    auto secondSnapshot = ClientReadEnvelope(firstClientWs);
    auto secondDecoded = dovahlink::protocol::DecodeStateSnapshotPayload(secondSnapshot.payload);
    REQUIRE(secondDecoded.has_value());
    CHECK(secondDecoded->revision == 2);

    boost::system::error_code firstCloseEc;
    firstClientWs.close(boost::beast::websocket::close_code::normal, firstCloseEc);
    firstServerThread.join();
    REQUIRE_FALSE(firstAcceptEc);

    // --- Second connection ("reconnect"): same ActivePlayContext, no further change. ---
    // A fresh TokenStore/throttle stands in for however the reconnect
    // re-authenticated (see the comment above); sessionManager and
    // activePlayContext stay the same shared instances.
    TokenStore secondTokenStore(*DecodeHex(kValidHexToken));
    FailedTokenThrottle secondTokenThrottle;
    EmptyPersistence secondPersistence;
    auto secondTrustStore = TrustStore::Load(secondPersistence);
    FailedTokenThrottle secondCredentialThrottle;
    boost::system::error_code secondAcceptEc;
    std::thread secondServerThread([&] {
        boost::asio::ip::tcp::socket serverSocket = listener->Acceptor().accept(secondAcceptEc);
        if (secondAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        RunConnectionSession(session, secondTokenStore, secondTokenThrottle, secondTrustStore, secondCredentialThrottle, sessionManager, /*connection=*/2,
                             activePlayContext, pairingSession, pairingNotificationSink, bridgeInstanceId, kBridgeVersion);
    });

    boost::asio::ip::tcp::socket secondClientSocket(ioc);
    boost::system::error_code secondConnectEc;
    secondClientSocket.connect(endpoint, secondConnectEc);
    REQUIRE_FALSE(secondConnectEc);
    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> secondClientWs(std::move(secondClientSocket));
    boost::system::error_code secondHandshakeEc;
    secondClientWs.handshake("127.0.0.1", "/", secondHandshakeEc);
    REQUIRE_FALSE(secondHandshakeEc);

    ClientWriteText(secondClientWs, HelloMessage(kValidHexToken));
    auto secondHelloAck = ClientReadEnvelope(secondClientWs);
    REQUIRE(secondHelloAck.messageType == "hello_ack");
    REQUIRE(secondHelloAck.sessionId.has_value());
    // A genuinely new session, not a resumption of the first connection's.
    CHECK(*secondHelloAck.sessionId != firstSessionId);
    REQUIRE(secondHelloAck.playContextId.has_value());
    CHECK(*secondHelloAck.playContextId == "context-1");
    // Still the same running bridge instance -- this is a reconnect, not a
    // restart -- so bridgeInstanceId matches the first connection's.
    REQUIRE(secondHelloAck.bridgeInstanceId.has_value());
    CHECK(*secondHelloAck.bridgeInstanceId == *firstHelloAck.bridgeInstanceId);
    std::string secondSessionId = *secondHelloAck.sessionId;
    auto secondCapabilities = ClientReadEnvelope(secondClientWs);
    CHECK(secondCapabilities.messageType == "capabilities");

    // No change since the first connection's last pull (level is still 6):
    // the reconnect's own subscribe snapshot must reuse revision 2, neither
    // resetting to 1 (proving the revision survives the reconnect) nor
    // advancing past 2 (proving the reused RevisionTracker still correctly
    // sees this as unchanged).
    ClientWriteText(secondClientWs, SubscribeMessage(secondSessionId, "message-sub-2"));
    auto reconnectAck = ClientReadEnvelope(secondClientWs);
    CHECK(reconnectAck.messageType == "subscription_ack");
    auto reconnectSnapshot = ClientReadEnvelope(secondClientWs);
    auto reconnectDecoded = dovahlink::protocol::DecodeStateSnapshotPayload(reconnectSnapshot.payload);
    REQUIRE(reconnectDecoded.has_value());
    CHECK(reconnectDecoded->revision == 2);
    auto reconnectState = dovahlink::protocol::DecodeCharacterState(reconnectDecoded->data);
    REQUIRE(reconnectState.has_value());
    REQUIRE(reconnectState->level.has_value());
    CHECK(*reconnectState->level == 6);

    boost::system::error_code secondCloseEc;
    secondClientWs.close(boost::beast::websocket::close_code::normal, secondCloseEc);
    secondServerThread.join();
    REQUIRE_FALSE(secondAcceptEc);
}

TEST_CASE("RunConnectionSession closes without creating a session when the token is invalid",
          "[application][connection_session]") {
    boost::asio::io_context ioc;
    auto listener = LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
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
    ActivePlayContext activePlayContext;
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
        boost::asio::ip::tcp::socket serverSocket = listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        RunConnectionSession(session, tokenStore, tokenThrottle, trustStore, credentialThrottle, sessionManager, /*connection=*/1, activePlayContext,
                             pairingSession, pairingNotificationSink, /*bridgeInstanceId=*/std::nullopt, kBridgeVersion, steadyNow);
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
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

    // The server closes the connection; the client's next read observes it.
    boost::beast::flat_buffer buffer;
    boost::system::error_code readEc;
    clientWs.read(buffer, readEc);

    serverThread.join();

    REQUIRE_FALSE(serverAcceptEc);
    CHECK(readEc);
    // The real token was never consumed by a rejected attempt.
    CHECK(tokenStore.IsAvailable());
    // The session's fifth failure occurred at simulated +4s, so all five
    // remain active at +63s. Recording it at the pre-read start would let
    // that one expire and incorrectly unblock authentication.
    CHECK(tokenThrottle.IsBlocked(start + std::chrono::seconds(63)));
}

TEST_CASE("RunConnectionSession's idle-loop read closes the connection once "
          "ConnectionTimeoutTracker's deadline has already elapsed, without waiting out any "
          "WebSocket-level timeout",
          "[application][connection_session]") {
    // Proves the wiring added in this step: timeout.Deadline() actually reaches
    // WebSocketSession::ReadMessage's own idleDeadline watchdog inside the message loop, not just
    // the initial hello read. A client that kept answering WebSocket-level pings (now enabled via
    // keep_alive_pings) would otherwise let Beast's own per-operation timeout run indefinitely;
    // that mechanism itself is proven directly at transport/websocket_session_test.cpp's level.
    // Here, an artificially already-elapsed idle deadline (manufactured via steadyNow, not a real
    // 60-second wait) proves the connection still closes through this real session loop.
    boost::asio::io_context ioc;
    auto listener = LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
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
    ActivePlayContext activePlayContext;
    auto start = std::chrono::steady_clock::now();
    int clockCalls = 0;
    // The hello read itself must not be rejected as late (postReadNow must stay before the
    // handshake deadline, start+5s), but authenticating at start-100s leaves the resulting idle
    // deadline (postReadNow+60s = start-40s) already 40s in the past relative to real time by the
    // time the loop's read arms its watchdog moments later.
    auto steadyNow = [&] {
        ++clockCalls;
        return clockCalls == 1 ? start : start - std::chrono::seconds(100);
    };

    boost::system::error_code serverAcceptEc;
    std::thread serverThread([&] {
        boost::asio::ip::tcp::socket serverSocket = listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        RunConnectionSession(session, tokenStore, tokenThrottle, trustStore, credentialThrottle, sessionManager, /*connection=*/1, activePlayContext,
                             pairingSession, pairingNotificationSink, /*bridgeInstanceId=*/std::nullopt, kBridgeVersion, steadyNow);
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
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

    // The client deliberately sends nothing else and does not close -- only the watchdog, racing
    // the already-elapsed deadline, should end this connection.
    auto readStart = std::chrono::steady_clock::now();
    boost::beast::flat_buffer buffer;
    boost::system::error_code readEc;
    clientWs.read(buffer, readEc);
    auto readDuration = std::chrono::steady_clock::now() - readStart;

    serverThread.join();

    REQUIRE_FALSE(serverAcceptEc);
    CHECK(readEc);
    // Well under the real 60s idle timeout the watchdog exists to bound, proving the already-past
    // deadline -- not eventually waiting out Beast's own longer timer -- is what closed this.
    CHECK(readDuration < std::chrono::seconds(10));
    CHECK_FALSE(sessionManager.IsValidForConnection(sessionId, /*connection=*/1));
}

TEST_CASE("RunConnectionSession's initial hello read closes the connection once "
          "ConnectionTimeoutTracker's handshake deadline has already elapsed, without waiting out "
          "any WebSocket-level timeout",
          "[application][connection_session]") {
    // Symmetric with the idle-loop watchdog test above, but for the *other* ws.ReadMessage() call
    // site this step changed: the very first hello read, before any session exists.
    boost::asio::io_context ioc;
    auto listener = LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
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
    ActivePlayContext activePlayContext;
    // The handshake deadline (constructor time + 5s) is already 5s in the past the moment the
    // tracker is built, so the very first ReadMessage's watchdog has nothing to wait out.
    auto steadyNow = [] { return std::chrono::steady_clock::now() - std::chrono::seconds(10); };

    boost::system::error_code serverAcceptEc;
    std::thread serverThread([&] {
        boost::asio::ip::tcp::socket serverSocket = listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        RunConnectionSession(session, tokenStore, tokenThrottle, trustStore, credentialThrottle, sessionManager, /*connection=*/1, activePlayContext,
                             pairingSession, pairingNotificationSink, /*bridgeInstanceId=*/std::nullopt, kBridgeVersion, steadyNow);
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);
    // The client deliberately never sends hello -- only the watchdog, racing the already-elapsed
    // handshake deadline, should end this connection.

    auto readStart = std::chrono::steady_clock::now();
    boost::beast::flat_buffer buffer;
    boost::system::error_code readEc;
    clientWs.read(buffer, readEc);
    auto readDuration = std::chrono::steady_clock::now() - readStart;

    serverThread.join();

    REQUIRE_FALSE(serverAcceptEc);
    CHECK(readEc);
    // Well under the real 5s handshake timeout the watchdog exists to bound.
    CHECK(readDuration < std::chrono::seconds(3));
}

TEST_CASE("RunConnectionSession closes with no hello_ack when hello arrives after the handshake "
          "deadline, even though no single socket read ever timed out",
          "[application][connection_session]") {
    boost::asio::io_context ioc;
    auto listener = LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
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
    ActivePlayContext activePlayContext;

    boost::system::error_code serverAcceptEc;
    std::thread serverThread([&] {
        boost::asio::ip::tcp::socket serverSocket = listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        RunConnectionSession(session, tokenStore, tokenThrottle, trustStore, credentialThrottle, sessionManager, /*connection=*/1, activePlayContext,
                             pairingSession, pairingNotificationSink, /*bridgeInstanceId=*/std::nullopt, kBridgeVersion);
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);

    // Each gap below is well under the 5-second handshake timeout, so no
    // individual OS-level socket read (WebSocketSession::SetReadTimeout)
    // ever fires. Sending pings in between (which the server's Beast read
    // absorbs transparently without returning to RunConnectionSession)
    // pushes the cumulative time before hello arrives past the 5-second
    // window anyway, proving the application-level ConnectionTimeoutTracker
    // check catches what SO_RCVTIMEO alone cannot.
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
    // The server closed without ever answering hello: no hello_ack, no
    // error -- the same "close with no response" behavior as an oversized
    // frame or the session message cap.
    CHECK(readEc);
    CHECK(tokenStore.IsAvailable());
}

TEST_CASE("RunConnectionSession's disconnect notifies PairingSession, keeping an owned challenge "
          "reserved within the grace period",
          "[application][connection_session]") {
    boost::asio::io_context ioc;
    auto listener = LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
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
    ActivePlayContext activePlayContext;

    auto start = std::chrono::steady_clock::now();
    // Established directly through PairingSession's own API -- this test proves
    // RunConnectionSession's disconnect wiring, not the pairing_request wire flow, which is
    // covered elsewhere (pairing_handler_test.cpp, message_dispatcher_test.cpp).
    REQUIRE(pairingSession.TryStartChallenge("client-1", start).outcome == StartChallengeOutcome::kStarted);

    // Exactly three steadyNow() calls occur for a connection that completes hello and then closes
    // without exchanging any further messages: the ConnectionTimeoutTracker constructor, postReadNow
    // (also used for the reconnect notification), and the final teardown call that notifies
    // PairingSession of the disconnect.
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
        boost::asio::ip::tcp::socket serverSocket = listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        RunConnectionSession(session, tokenStore, tokenThrottle, trustStore, credentialThrottle, sessionManager, /*connection=*/1, activePlayContext,
                             pairingSession, pairingNotificationSink, /*bridgeInstanceId=*/std::nullopt, kBridgeVersion, steadyNow);
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
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

    // Disconnect landed at start+2s; well inside the 10-second grace period, a different device is
    // still shut out, and the original owner's challenge is untouched.
    CHECK(pairingSession.TryStartChallenge("client-2", start + std::chrono::seconds(2 + 5)).outcome ==
          StartChallengeOutcome::kOtherDeviceActive);
}

TEST_CASE("RunConnectionSession's disconnect notification lets an owned challenge's grace period "
          "elapse, freeing it for another device",
          "[application][connection_session]") {
    boost::asio::io_context ioc;
    auto listener = LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
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
    ActivePlayContext activePlayContext;

    auto start = std::chrono::steady_clock::now();
    REQUIRE(pairingSession.TryStartChallenge("client-1", start).outcome == StartChallengeOutcome::kStarted);

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
        boost::asio::ip::tcp::socket serverSocket = listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        RunConnectionSession(session, tokenStore, tokenThrottle, trustStore, credentialThrottle, sessionManager, /*connection=*/1, activePlayContext,
                             pairingSession, pairingNotificationSink, /*bridgeInstanceId=*/std::nullopt, kBridgeVersion, steadyNow);
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
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

    // Disconnect landed at start+2s; past the 10-second grace period (start+2s+11s), the slot is
    // genuinely free for a different device.
    auto started = pairingSession.TryStartChallenge("client-2", start + std::chrono::seconds(2 + 11));
    CHECK(started.outcome == StartChallengeOutcome::kStarted);
    REQUIRE(started.code.has_value());
}

TEST_CASE("RunConnectionSession's successful hello notifies PairingSession of a reconnect, clearing "
          "a pending grace countdown from a prior disconnect",
          "[application][connection_session]") {
    boost::asio::io_context ioc;
    auto listener = LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
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
    ActivePlayContext activePlayContext;

    auto start = std::chrono::steady_clock::now();
    REQUIRE(pairingSession.TryStartChallenge("client-1", start).outcome == StartChallengeOutcome::kStarted);
    // Simulates a prior dropped connection that already started the grace countdown, before this
    // test's own connection (the "reconnect") is even accepted.
    pairingSession.NotifyDisconnected("client-1", start);

    boost::system::error_code serverAcceptEc;
    std::thread serverThread([&] {
        boost::asio::ip::tcp::socket serverSocket = listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        RunConnectionSession(session, tokenStore, tokenThrottle, trustStore, credentialThrottle, sessionManager, /*connection=*/1, activePlayContext,
                             pairingSession, pairingNotificationSink, /*bridgeInstanceId=*/std::nullopt, kBridgeVersion);
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);

    ClientWriteText(clientWs, HelloMessage(kValidHexToken, "client-1"));
    auto helloAck = ClientReadEnvelope(clientWs);
    REQUIRE(helloAck.messageType == "hello_ack");
    // capabilities is sent strictly after this session's own NotifyReconnected call in program
    // order on the server thread, so reading it here happens-after that call actually ran.
    auto capabilities = ClientReadEnvelope(clientWs);
    CHECK(capabilities.messageType == "capabilities");

    // Even past what would have been the original disconnect's grace deadline, the challenge is
    // still client-1's -- the reconnect cleared the countdown rather than leaving it running.
    auto pastOriginalGrace = start + std::chrono::seconds(11);
    CHECK(pairingSession.TryStartChallenge("client-2", pastOriginalGrace).outcome ==
          StartChallengeOutcome::kOtherDeviceActive);

    boost::system::error_code closeEc;
    clientWs.close(boost::beast::websocket::close_code::normal, closeEc);
    serverThread.join();
    REQUIRE_FALSE(serverAcceptEc);
}

TEST_CASE("RunConnectionSession's disconnect/reconnect wiring works on an unpaired session, the "
          "trust tier pairing actually runs under",
          "[application][connection_session]") {
    boost::asio::io_context ioc;
    auto listener = LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
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
    ActivePlayContext activePlayContext;

    auto start = std::chrono::steady_clock::now();
    REQUIRE(pairingSession.TryStartChallenge("client-1", start).outcome == StartChallengeOutcome::kStarted);

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
        boost::asio::ip::tcp::socket serverSocket = listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        RunConnectionSession(session, tokenStore, tokenThrottle, trustStore, credentialThrottle, sessionManager, /*connection=*/1, activePlayContext,
                             pairingSession, pairingNotificationSink, /*bridgeInstanceId=*/std::nullopt, kBridgeVersion, steadyNow);
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
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

    // Disconnect landed at start+2s; well inside the 10-second grace period, a different device is
    // still shut out. Proves the wiring fires the same way on the unpaired tier the pairing flow
    // actually authenticates on, not just the developer-token tier the other tests above use.
    CHECK(pairingSession.TryStartChallenge("client-2", start + std::chrono::seconds(2 + 5)).outcome ==
          StartChallengeOutcome::kOtherDeviceActive);
}

TEST_CASE("RunConnectionSession's reconnect notification for a client owning nothing is a harmless "
          "no-op that leaves an unrelated client's owned challenge untouched",
          "[application][connection_session]") {
    boost::asio::io_context ioc;
    auto listener = LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
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
    ActivePlayContext activePlayContext;

    auto start = std::chrono::steady_clock::now();
    // "client-1" owns an active challenge; the connection under test authenticates as the
    // unrelated "client-3", which owns nothing in PairingSession.
    REQUIRE(pairingSession.TryStartChallenge("client-1", start).outcome == StartChallengeOutcome::kStarted);

    boost::system::error_code serverAcceptEc;
    std::thread serverThread([&] {
        boost::asio::ip::tcp::socket serverSocket = listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        RunConnectionSession(session, tokenStore, tokenThrottle, trustStore, credentialThrottle, sessionManager, /*connection=*/1, activePlayContext,
                             pairingSession, pairingNotificationSink, /*bridgeInstanceId=*/std::nullopt, kBridgeVersion);
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);

    ClientWriteText(clientWs, UnpairedHelloMessage("client-3"));
    auto helloAck = ClientReadEnvelope(clientWs);
    REQUIRE(helloAck.messageType == "hello_ack");
    // capabilities is sent strictly after this session's own NotifyReconnected("client-3", ...)
    // call in program order on the server thread, so reading it here happens-after that call ran.
    auto capabilities = ClientReadEnvelope(clientWs);
    CHECK(capabilities.messageType == "capabilities");

    // client-1's challenge is exactly as it was: "client-3" reconnecting (owning nothing) never
    // touched it.
    CHECK(pairingSession.TryStartChallenge("client-1", start).outcome == StartChallengeOutcome::kResumed);
    CHECK(pairingSession.TryStartChallenge("client-2", start).outcome == StartChallengeOutcome::kOtherDeviceActive);

    boost::system::error_code closeEc;
    clientWs.close(boost::beast::websocket::close_code::normal, closeEc);
    serverThread.join();
    REQUIRE_FALSE(serverAcceptEc);
}
