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
#include <thread>
#include <utility>

using dovahlink::application::ActivePlayContext;
using dovahlink::application::RunConnectionSession;
using dovahlink::application::SessionManager;
using dovahlink::protocol::Envelope;
using dovahlink::security::DecodeHex;
using dovahlink::security::FailedTokenThrottle;
using dovahlink::security::TokenStore;
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
constexpr const char* kBridgeVersion = "0.1.0";

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
        RunConnectionSession(session, tokenStore, tokenThrottle, sessionManager, /*connection=*/1, activePlayContext,
                             /*bridgeInstanceId=*/std::nullopt, kBridgeVersion, steadyNow);
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
        RunConnectionSession(session, tokenStore, tokenThrottle, sessionManager, /*connection=*/1, activePlayContext,
                             bridgeInstanceId, kBridgeVersion);
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
    std::string sessionId = *helloAck.sessionId;

    auto capabilities = ClientReadEnvelope(clientWs);
    CHECK(capabilities.messageType == "capabilities");
    REQUIRE(capabilities.bridgeInstanceId.has_value());
    CHECK(*capabilities.bridgeInstanceId == "bridge-1");
    REQUIRE(capabilities.clientId.has_value());
    CHECK(*capabilities.clientId == "client-1");

    ClientWriteText(clientWs, PingMessage(sessionId));
    auto pong = ClientReadEnvelope(clientWs);
    CHECK(pong.messageType == "pong");
    REQUIRE(pong.bridgeInstanceId.has_value());
    CHECK(*pong.bridgeInstanceId == "bridge-1");
    REQUIRE(pong.clientId.has_value());
    CHECK(*pong.clientId == "client-1");

    ClientWriteText(clientWs, SubscribeMessage(sessionId));
    auto subscriptionAck = ClientReadEnvelope(clientWs);
    CHECK(subscriptionAck.messageType == "subscription_ack");
    auto snapshot = ClientReadEnvelope(clientWs);
    CHECK(snapshot.messageType == "state_snapshot");
    REQUIRE(snapshot.bridgeInstanceId.has_value());
    CHECK(*snapshot.bridgeInstanceId == "bridge-1");
    REQUIRE(snapshot.clientId.has_value());
    CHECK(*snapshot.clientId == "client-1");
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
        RunConnectionSession(session, tokenStore, tokenThrottle, sessionManager, /*connection=*/1, activePlayContext,
                             bridgeInstanceId, kBridgeVersion);
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
    // SessionManager and ActivePlayContext are shared across both
    // connections below, matching how BridgeWorkerPool actually owns them at
    // pool lifetime rather than per-connection.
    SessionManager sessionManager;
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
        RunConnectionSession(session, firstTokenStore, firstTokenThrottle, sessionManager, /*connection=*/1,
                             activePlayContext, bridgeInstanceId, kBridgeVersion);
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
    boost::system::error_code secondAcceptEc;
    std::thread secondServerThread([&] {
        boost::asio::ip::tcp::socket serverSocket = listener->Acceptor().accept(secondAcceptEc);
        if (secondAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        RunConnectionSession(session, secondTokenStore, secondTokenThrottle, sessionManager, /*connection=*/2,
                             activePlayContext, bridgeInstanceId, kBridgeVersion);
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
        RunConnectionSession(session, tokenStore, tokenThrottle, sessionManager, /*connection=*/1, activePlayContext,
                             /*bridgeInstanceId=*/std::nullopt, kBridgeVersion, steadyNow);
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

TEST_CASE("RunConnectionSession closes with no hello_ack when hello arrives after the handshake "
          "deadline, even though no single socket read ever timed out",
          "[application][connection_session]") {
    boost::asio::io_context ioc;
    auto listener = LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());
    boost::asio::ip::tcp::endpoint endpoint = listener->LocalEndpoint();

    TokenStore tokenStore(*DecodeHex(kValidHexToken));
    FailedTokenThrottle tokenThrottle;
    SessionManager sessionManager;
    ActivePlayContext activePlayContext;

    boost::system::error_code serverAcceptEc;
    std::thread serverThread([&] {
        boost::asio::ip::tcp::socket serverSocket = listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        RunConnectionSession(session, tokenStore, tokenThrottle, sessionManager, /*connection=*/1, activePlayContext,
                             /*bridgeInstanceId=*/std::nullopt, kBridgeVersion);
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
