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

using dovahlink::application::CharacterSnapshot;
using dovahlink::application::CharacterStateProvider;
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

/// Supplies a deterministic character snapshot to the real connection session.
class FakeCharacterStateProvider : public CharacterStateProvider {
public:
    /// @copydoc CharacterStateProvider::CurrentCharacterSnapshot
    [[nodiscard]] CharacterSnapshot CurrentCharacterSnapshot() const override {
        return CharacterSnapshot{.level = 30};
    }
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

/// Builds a hello message using the supplied authentication token.
std::string HelloMessage(const std::string& token) {
    return R"({"protocolVersion": 0, "messageType": "hello", "messageId": "message-hello-1", )"
           R"("sessionId": null, "correlationId": null, "payload": {"endpoint": "client", )"
           R"("supportedProtocolVersions": [1], "auth": {"method": "one_time_local_token", "token": ")" +
           token + R"("}}})";
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
    FakeCharacterStateProvider stateProvider;

    boost::system::error_code serverAcceptEc;
    std::thread serverThread([&] {
        boost::asio::ip::tcp::socket serverSocket = listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        RunConnectionSession(session, tokenStore, tokenThrottle, sessionManager, /*connection=*/1, stateProvider);
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

    ClientWriteText(clientWs, R"({"protocolVersion": 1, "messageType": "ping", "messageId": "message-ping-1", )"
                                  R"("sessionId": ")" +
                                  sessionId + R"(", "correlationId": null, "payload": {}})");
    auto pong = ClientReadEnvelope(clientWs);
    CHECK(pong.messageType == "pong");

    ClientWriteText(clientWs,
                     R"({"protocolVersion": 1, "messageType": "subscribe", "messageId": "message-sub-1", )"
                     R"("sessionId": ")" +
                         sessionId + R"(", "correlationId": null, "payload": {"stateAreas": ["character"]}})");
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
    FakeCharacterStateProvider stateProvider;

    boost::system::error_code serverAcceptEc;
    std::thread serverThread([&] {
        boost::asio::ip::tcp::socket serverSocket = listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        RunConnectionSession(session, tokenStore, tokenThrottle, sessionManager, /*connection=*/1, stateProvider);
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
    FakeCharacterStateProvider stateProvider;

    boost::system::error_code serverAcceptEc;
    std::thread serverThread([&] {
        boost::asio::ip::tcp::socket serverSocket = listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        RunConnectionSession(session, tokenStore, tokenThrottle, sessionManager, /*connection=*/1, stateProvider);
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
