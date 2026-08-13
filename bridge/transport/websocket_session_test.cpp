#include "transport/websocket_session.hpp"

#include "security/limits.hpp"
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
#include <expected>
#include <string>
#include <thread>
#include <utility>

using dovahlink::transport::LoopbackListener;
using dovahlink::transport::SessionError;
using dovahlink::transport::WebSocketSession;

// These tests use two real loopback sockets and a real Boost.Beast handshake.
// Server-thread results are captured and asserted only after the thread joins,
// keeping Catch2 assertions on the main test thread.

TEST_CASE("Accept completes a real WebSocket handshake over loopback",
          "[transport][websocket_session]") {
    boost::asio::io_context ioc;
    auto listener = LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());
    boost::asio::ip::tcp::endpoint endpoint = listener->LocalEndpoint();

    boost::system::error_code serverAcceptEc;
    std::expected<void, SessionError> serverHandshakeResult = std::unexpected(SessionError::kHandshakeFailed);

    std::thread serverThread([&] {
        boost::asio::ip::tcp::socket serverSocket = listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        serverHandshakeResult = session.Accept();
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);

    serverThread.join();

    REQUIRE_FALSE(connectEc);
    REQUIRE_FALSE(serverAcceptEc);
    REQUIRE(serverHandshakeResult.has_value());
    CHECK_FALSE(handshakeEc);
}

TEST_CASE("a text message from the client is read on the server", "[transport][websocket_session]") {
    boost::asio::io_context ioc;
    auto listener = LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());
    boost::asio::ip::tcp::endpoint endpoint = listener->LocalEndpoint();

    boost::system::error_code serverAcceptEc;
    std::expected<void, SessionError> serverHandshakeResult = std::unexpected(SessionError::kHandshakeFailed);
    std::expected<std::string, SessionError> serverReadResult = std::unexpected(SessionError::kReadFailed);

    std::thread serverThread([&] {
        boost::asio::ip::tcp::socket serverSocket = listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        serverHandshakeResult = session.Accept();
        if (!serverHandshakeResult.has_value()) {
            return;
        }
        serverReadResult = session.ReadMessage();
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);

    clientWs.text(true);
    boost::system::error_code writeEc;
    clientWs.write(boost::asio::buffer(std::string("hello")), writeEc);
    REQUIRE_FALSE(writeEc);

    serverThread.join();

    REQUIRE_FALSE(serverAcceptEc);
    REQUIRE(serverHandshakeResult.has_value());
    REQUIRE(serverReadResult.has_value());
    CHECK(*serverReadResult == "hello");
}

TEST_CASE("a text message from the server is read on the client", "[transport][websocket_session]") {
    boost::asio::io_context ioc;
    auto listener = LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());
    boost::asio::ip::tcp::endpoint endpoint = listener->LocalEndpoint();

    boost::system::error_code serverAcceptEc;
    std::expected<void, SessionError> serverHandshakeResult = std::unexpected(SessionError::kHandshakeFailed);
    std::expected<void, SessionError> serverWriteResult = std::unexpected(SessionError::kWriteFailed);

    std::thread serverThread([&] {
        boost::asio::ip::tcp::socket serverSocket = listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        serverHandshakeResult = session.Accept();
        if (!serverHandshakeResult.has_value()) {
            return;
        }
        serverWriteResult = session.WriteMessage("hello from server");
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);

    boost::beast::flat_buffer buffer;
    boost::system::error_code readEc;
    clientWs.read(buffer, readEc);

    serverThread.join();

    REQUIRE_FALSE(serverAcceptEc);
    REQUIRE(serverHandshakeResult.has_value());
    REQUIRE(serverWriteResult.has_value());
    REQUIRE_FALSE(readEc);
    CHECK(boost::beast::buffers_to_string(buffer.data()) == "hello from server");
}

TEST_CASE("ReadMessage fails when the client closes the connection abruptly without a close frame",
          "[transport][websocket_session]") {
    boost::asio::io_context ioc;
    auto listener = LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());
    boost::asio::ip::tcp::endpoint endpoint = listener->LocalEndpoint();

    boost::system::error_code serverAcceptEc;
    std::expected<void, SessionError> serverHandshakeResult = std::unexpected(SessionError::kHandshakeFailed);
    std::expected<std::string, SessionError> serverReadResult = std::string{"should not remain unset"};

    std::thread serverThread([&] {
        boost::asio::ip::tcp::socket serverSocket = listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        serverHandshakeResult = session.Accept();
        if (!serverHandshakeResult.has_value()) {
            return;
        }
        serverReadResult = session.ReadMessage();
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);

    // Tear down the underlying TCP connection directly, bypassing the
    // WebSocket close handshake, so the server's blocked read fails instead
    // of completing normally.
    boost::system::error_code closeEc;
    clientWs.next_layer().close(closeEc);

    serverThread.join();

    REQUIRE_FALSE(serverAcceptEc);
    REQUIRE(serverHandshakeResult.has_value());
    CHECK_FALSE(serverReadResult.has_value());
    CHECK(serverReadResult.error() == SessionError::kReadFailed);
}

TEST_CASE("Close causes the peer's next read to observe the connection closing",
          "[transport][websocket_session]") {
    boost::asio::io_context ioc;
    auto listener = LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());
    boost::asio::ip::tcp::endpoint endpoint = listener->LocalEndpoint();

    boost::system::error_code serverAcceptEc;
    std::expected<void, SessionError> serverHandshakeResult = std::unexpected(SessionError::kHandshakeFailed);

    std::thread serverThread([&] {
        boost::asio::ip::tcp::socket serverSocket = listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        serverHandshakeResult = session.Accept();
        if (!serverHandshakeResult.has_value()) {
            return;
        }
        session.Close();
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);

    // The server's Close() sends a close frame; Beast's client-side read
    // completes the close handshake and reports it via the error_code
    // rather than delivering an application message.
    boost::beast::flat_buffer buffer;
    boost::system::error_code readEc;
    clientWs.read(buffer, readEc);

    serverThread.join();

    REQUIRE_FALSE(serverAcceptEc);
    REQUIRE(serverHandshakeResult.has_value());
    CHECK(readEc == boost::beast::websocket::error::closed);
}

TEST_CASE("SwitchToIdleTimeout does not disrupt a subsequent read",
          "[transport][websocket_session]") {
    // This test proves that reapplying the timeout option mid-session is
    // message-preserving; the long idle duration is not waited out here.
    boost::asio::io_context ioc;
    auto listener = LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());
    boost::asio::ip::tcp::endpoint endpoint = listener->LocalEndpoint();

    boost::system::error_code serverAcceptEc;
    std::expected<void, SessionError> serverHandshakeResult = std::unexpected(SessionError::kHandshakeFailed);
    std::expected<std::string, SessionError> serverReadResult = std::unexpected(SessionError::kReadFailed);

    std::thread serverThread([&] {
        boost::asio::ip::tcp::socket serverSocket = listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        serverHandshakeResult = session.Accept();
        if (!serverHandshakeResult.has_value()) {
            return;
        }
        session.SwitchToIdleTimeout();
        serverReadResult = session.ReadMessage();
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);

    clientWs.text(true);
    boost::system::error_code writeEc;
    clientWs.write(boost::asio::buffer(std::string("after switch")), writeEc);
    REQUIRE_FALSE(writeEc);

    serverThread.join();

    REQUIRE_FALSE(serverAcceptEc);
    REQUIRE(serverHandshakeResult.has_value());
    REQUIRE(serverReadResult.has_value());
    CHECK(*serverReadResult == "after switch");
}

TEST_CASE("ReadMessage fails within the handshake timeout when the client sends nothing",
          "[transport][websocket_session]") {
    // Proves the production timeout boundary: SetReadTimeout's OS-level
    // SO_RCVTIMEO genuinely bounds a stalled read. Before that fix, this
    // exact scenario (a WebSocket-handshaked peer that never sends an
    // application message) had no enforced bound at all in this class's
    // synchronous API -- Boost.Beast's own stream_base::timeout option is
    // only armed by its asynchronous operations, confirmed against the
    // vendored source and by directly reproducing an unbounded stall
    // security::kHandshakeTimeout is 5 seconds; this asserts
    // comfortably under that with margin, not an exact bound, since exact
    // OS-level timer precision is not this test's concern.
    boost::asio::io_context ioc;
    auto listener = LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());
    boost::asio::ip::tcp::endpoint endpoint = listener->LocalEndpoint();

    boost::system::error_code serverAcceptEc;
    std::expected<void, SessionError> serverHandshakeResult = std::unexpected(SessionError::kHandshakeFailed);
    std::expected<std::string, SessionError> serverReadResult = std::string{"should not remain unset"};
    std::chrono::steady_clock::duration serverReadDuration{};

    std::thread serverThread([&] {
        boost::asio::ip::tcp::socket serverSocket = listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        serverHandshakeResult = session.Accept();
        if (!serverHandshakeResult.has_value()) {
            return;
        }
        auto start = std::chrono::steady_clock::now();
        serverReadResult = session.ReadMessage();
        serverReadDuration = std::chrono::steady_clock::now() - start;
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);
    // The client deliberately sends nothing and does not close, simulating
    // a hung or unresponsive peer -- the case that used to stall.

    serverThread.join();

    REQUIRE_FALSE(serverAcceptEc);
    REQUIRE(serverHandshakeResult.has_value());
    CHECK_FALSE(serverReadResult.has_value());
    CHECK(serverReadDuration < std::chrono::seconds(20));
}

TEST_CASE("a client that sends an oversized frame and never closes still gets a bounded failure",
          "[transport][websocket_session]") {
    // This covers a peer that never cooperates after sending an oversized
    // frame. Before the SO_RCVTIMEO fix, Beast's internal
    // do_fail()/teardown() sequence (triggered once it detects the
    // oversized frame) would wait unboundedly for this peer to close back,
    // observed to take roughly two minutes before falling back to a
    // generic OS-level failure. The exact resulting SessionError is not
    // asserted -- only that the connection slot is not held hostage.
    boost::asio::io_context ioc;
    auto listener = LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());
    boost::asio::ip::tcp::endpoint endpoint = listener->LocalEndpoint();

    boost::system::error_code serverAcceptEc;
    std::expected<void, SessionError> serverHandshakeResult = std::unexpected(SessionError::kHandshakeFailed);
    std::expected<std::string, SessionError> serverReadResult = std::string{"should not remain unset"};
    std::chrono::steady_clock::duration serverReadDuration{};

    std::thread serverThread([&] {
        boost::asio::ip::tcp::socket serverSocket = listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        serverHandshakeResult = session.Accept();
        if (!serverHandshakeResult.has_value()) {
            return;
        }
        auto start = std::chrono::steady_clock::now();
        serverReadResult = session.ReadMessage();
        serverReadDuration = std::chrono::steady_clock::now() - start;
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);

    clientWs.text(true);
    std::string oversized(dovahlink::security::kMaxInboundFrameBytes + 1, 'x');
    boost::system::error_code writeEc;
    clientWs.write(boost::asio::buffer(oversized), writeEc);
    // Deliberately no close/shutdown of any kind after this.

    serverThread.join();

    REQUIRE_FALSE(serverAcceptEc);
    REQUIRE(serverHandshakeResult.has_value());
    CHECK_FALSE(serverReadResult.has_value());
    CHECK(serverReadDuration < std::chrono::seconds(20));
}

TEST_CASE("a binary frame from the client is rejected, not returned as a message",
          "[transport][websocket_session]") {
    boost::asio::io_context ioc;
    auto listener = LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());
    boost::asio::ip::tcp::endpoint endpoint = listener->LocalEndpoint();

    boost::system::error_code serverAcceptEc;
    std::expected<void, SessionError> serverHandshakeResult = std::unexpected(SessionError::kHandshakeFailed);
    std::expected<std::string, SessionError> serverReadResult = std::string{"should not remain unset"};

    std::thread serverThread([&] {
        boost::asio::ip::tcp::socket serverSocket = listener->Acceptor().accept(serverAcceptEc);
        if (serverAcceptEc) {
            return;
        }
        WebSocketSession session(std::move(serverSocket));
        serverHandshakeResult = session.Accept();
        if (!serverHandshakeResult.has_value()) {
            return;
        }
        serverReadResult = session.ReadMessage();
    });

    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);

    clientWs.binary(true);
    boost::system::error_code writeEc;
    const unsigned char binaryPayload[] = {0x00, 0x01, 0x02};
    clientWs.write(boost::asio::buffer(binaryPayload, sizeof(binaryPayload)), writeEc);
    REQUIRE_FALSE(writeEc);

    serverThread.join();

    REQUIRE_FALSE(serverAcceptEc);
    REQUIRE(serverHandshakeResult.has_value());
    REQUIRE_FALSE(serverReadResult.has_value());
    CHECK(serverReadResult.error() == SessionError::kBinaryFrameRejected);
}
