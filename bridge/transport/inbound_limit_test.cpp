// Proves the Phase 1 input-limit and violation-closure rules from
// ai/context/protocol/security.md work together as intended:
//   - a frame within bridge/security/limits.hpp's kMaxInboundFrameBytes is
//     read normally (transport::WebSocketSession, Step 27/28)
//   - a frame exceeding it is rejected as SessionError::kFrameTooLarge
//     before a message could be decoded
//   - kFrameTooLarge is the caller's signal to close immediately without
//     routing it through the 3-violations/30s throttle
//     (security::ViolationTracker, Step 14), which is reserved for
//     violations discovered after a safe message was already decoded
//
// WebSocketSession has no dependency on ViolationTracker (transport does not
// depend on this particular application-layer policy type); the last test
// below demonstrates the intended calling convention a future coordinator
// step will implement, using both real components together rather than
// asserting the convention only in documentation.

#include "transport/listener.hpp"
#include "transport/websocket_session.hpp"

#include "security/limits.hpp"
#include "security/throttle.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/asio/buffer.hpp>
#include <boost/asio/io_context.hpp>
#include <boost/asio/ip/tcp.hpp>
#include <boost/beast/websocket/stream.hpp>
#include <boost/system/error_code.hpp>

#include <chrono>
#include <cstddef>
#include <expected>
#include <string>
#include <thread>
#include <utility>

using dovahlink::security::ViolationTracker;
using dovahlink::transport::LoopbackListener;
using dovahlink::transport::SessionError;
using dovahlink::transport::WebSocketSession;

TEST_CASE("a message within the frame size limit is read normally", "[transport][inbound_limit]") {
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
    std::string withinLimit(1024, 'x');
    boost::system::error_code writeEc;
    clientWs.write(boost::asio::buffer(withinLimit), writeEc);
    REQUIRE_FALSE(writeEc);

    serverThread.join();

    REQUIRE_FALSE(serverAcceptEc);
    REQUIRE(serverHandshakeResult.has_value());
    REQUIRE(serverReadResult.has_value());
    CHECK(serverReadResult->size() == withinLimit.size());
}

TEST_CASE("a message exactly at the frame size limit is accepted, not rejected",
          "[transport][inbound_limit]") {
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
    std::string atLimit(dovahlink::security::kMaxInboundFrameBytes, 'x');
    boost::system::error_code writeEc;
    clientWs.write(boost::asio::buffer(atLimit), writeEc);
    REQUIRE_FALSE(writeEc);

    serverThread.join();

    REQUIRE_FALSE(serverAcceptEc);
    REQUIRE(serverHandshakeResult.has_value());
    REQUIRE(serverReadResult.has_value());
    CHECK(serverReadResult->size() == atLimit.size());
}

TEST_CASE("a message exceeding the frame size limit is rejected as kFrameTooLarge",
          "[transport][inbound_limit]") {
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

    clientWs.text(true);
    std::string oversized(dovahlink::security::kMaxInboundFrameBytes + 1, 'x');
    boost::system::error_code writeEc;
    clientWs.write(boost::asio::buffer(oversized), writeEc);
    // The write itself may succeed or fail depending on how much the server
    // read before rejecting; either is consistent with rejection happening
    // on the server side, so it is not asserted here.

    serverThread.join();

    REQUIRE_FALSE(serverAcceptEc);
    REQUIRE(serverHandshakeResult.has_value());
    REQUIRE_FALSE(serverReadResult.has_value());
    CHECK(serverReadResult.error() == SessionError::kFrameTooLarge);
}

TEST_CASE("kFrameTooLarge closes immediately without consuming a violation strike, matching the "
          "documented calling convention a coordinator will implement",
          "[transport][inbound_limit]") {
    // Demonstrates the intended policy: kFrameTooLarge -> close now, do not
    // touch the throttle; every other SessionError -> record a violation and
    // close only once the throttle says so. Simulated here as a small,
    // explicit dispatcher rather than something WebSocketSession itself
    // implements, since transport code must not depend on this specific
    // application-layer throttle type.
    ViolationTracker violations;
    std::chrono::steady_clock::time_point now = std::chrono::steady_clock::now();

    auto shouldCloseImmediately = [&](SessionError error) -> bool {
        if (error == SessionError::kFrameTooLarge) {
            return true;  // immediate close, no violation recorded.
        }
        return violations.RecordViolationAndCheckLimit(now);
    };

    // A single kFrameTooLarge closes immediately and leaves the throttle
    // untouched: two more non-framing violations afterward still only close
    // on the third of *those*, proving the frame-too-large case truly did
    // not consume one of the three strikes.
    CHECK(shouldCloseImmediately(SessionError::kFrameTooLarge));

    CHECK_FALSE(shouldCloseImmediately(SessionError::kReadFailed));
    CHECK_FALSE(shouldCloseImmediately(SessionError::kBinaryFrameRejected));
    CHECK(shouldCloseImmediately(SessionError::kHandshakeFailed));
}
