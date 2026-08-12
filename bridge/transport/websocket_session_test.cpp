#include "transport/websocket_session.hpp"

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

#include <expected>
#include <string>
#include <thread>
#include <utility>

using dovahlink::transport::LoopbackListener;
using dovahlink::transport::SessionError;
using dovahlink::transport::WebSocketSession;

// These tests use two real loopback sockets (client and server) with an
// actual Boost.Beast WebSocket handshake on each side, on a background
// thread for the server, matching this project's testing convention that
// transport code is proven against real sockets. Catch2 assertions are
// deliberately never called from the background thread (not documented as
// thread-safe); results are captured into variables and checked only after
// the thread is joined back onto the main test thread.

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
    // The actual timeout durations (5s handshake vs. 60s idle) are not
    // exercised here -- a unit test cannot wait real seconds to prove a
    // timeout fires (ai/context/skse/testing.md: do not rely on timing
    // sleeps) -- and are verified by code inspection instead, matching this
    // file's existing precedent for Beast options that have no simple
    // runtime-introspectable accessor (see the compression-disabled note in
    // websocket_session.hpp). This proves only that re-applying the option
    // mid-session is a safe, message-preserving operation, exercising the
    // one thing that IS observable: does a message sent after the switch
    // still arrive correctly.
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
