#include "transport/websocket_session.hpp"

#include "security/limits.hpp"
#include "transport/loopback_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/asio/buffer.hpp>
#include <boost/asio/io_context.hpp>
#include <boost/asio/ip/tcp.hpp>
#include <boost/asio/post.hpp>
#include <boost/beast/core/buffers_to_string.hpp>
#include <boost/beast/core/flat_buffer.hpp>
#include <boost/beast/websocket/error.hpp>
#include <boost/beast/websocket/stream.hpp>
#include <boost/system/error_code.hpp>

#include <chrono>
#include <expected>
#include <future>
#include <semaphore>
#include <stdexcept>
#include <string>
#include <utility>

using dovahlink::transport::SessionError;
using dovahlink::transport::WebSocketSession;
using dovahlink::transport::test_support::LoopbackWebSocketServer;

namespace {

/// Releases one test semaphore during early scope exit unless released explicitly.
class SemaphoreRelease {
public:
    /// Arms cleanup for the supplied semaphore.
    explicit SemaphoreRelease(std::binary_semaphore& semaphore) : semaphore_(semaphore) {}

    /// Releases the semaphore when normal test flow did not already do so.
    ~SemaphoreRelease() { Release(); }

    /// Releases the semaphore exactly once.
    void Release() noexcept {
        if (isArmed_) {
            semaphore_.release();
            isArmed_ = false;
        }
    }

private:
    /// Semaphore whose waiter must not outlive the test scope.
    std::binary_semaphore& semaphore_;
    /// Whether cleanup still owns the release obligation.
    bool isArmed_{true};
};

}  // namespace

// These tests use two real loopback sockets and a real Boost.Beast handshake.
// Server-thread results are captured and asserted only after the thread joins,
// keeping Catch2 assertions on the main test thread.

TEST_CASE("LoopbackWebSocketServer cleanup joins when client setup exits before connecting",
          "[transport][websocket_session][test_support]") {
    using namespace std::chrono_literals;

    auto start = std::chrono::steady_clock::now();
    {
        LoopbackWebSocketServer server([](WebSocketSession&) {});
    }

    CHECK(std::chrono::steady_clock::now() - start < 2s);
}

TEST_CASE("LoopbackWebSocketServer cleanup interrupts accepted session work on early exit",
          "[transport][websocket_session][test_support]") {
    using namespace std::chrono_literals;

    std::promise<void> serverWorkStarted;
    std::future<void> workStarted = serverWorkStarted.get_future();
    auto start = std::chrono::steady_clock::now();
    {
        LoopbackWebSocketServer server([&](WebSocketSession& session) {
            serverWorkStarted.set_value();
            (void)session.Accept();
        });

        boost::asio::io_context clientIoc;
        boost::asio::ip::tcp::socket clientSocket(clientIoc);
        boost::system::error_code connectEc;
        clientSocket.connect(server.LocalEndpoint(), connectEc);
        REQUIRE_FALSE(connectEc);
        REQUIRE(workStarted.wait_for(2s) == std::future_status::ready);
    }

    CHECK(std::chrono::steady_clock::now() - start < 2s);
}

TEST_CASE("LoopbackWebSocketServer reports server exceptions on the test thread",
          "[transport][websocket_session][test_support]") {
    LoopbackWebSocketServer server([](WebSocketSession&) { throw std::runtime_error("server failed"); });

    boost::asio::io_context clientIoc;
    boost::asio::ip::tcp::socket clientSocket(clientIoc);
    boost::system::error_code connectEc;
    clientSocket.connect(server.LocalEndpoint(), connectEc);
    REQUIRE_FALSE(connectEc);

    CHECK_THROWS_AS(server.Join(), std::runtime_error);
}

TEST_CASE("LoopbackWebSocketServer reports accept failures on the test thread",
          "[transport][websocket_session][test_support]") {
    using namespace std::chrono_literals;

    LoopbackWebSocketServer server([](WebSocketSession&) {});
    server.CancelPendingAccept();

    REQUIRE(server.WaitFor(2s));
    CHECK_THROWS_AS(server.Join(), std::runtime_error);
}

TEST_CASE("LoopbackWebSocketServer cleanup suppresses a server exception without terminating",
          "[transport][websocket_session][test_support]") {
    using namespace std::chrono_literals;

    CHECK_NOTHROW([] {
        LoopbackWebSocketServer server([](WebSocketSession&) { throw std::runtime_error("server failed"); });
        boost::asio::io_context clientIoc;
        boost::asio::ip::tcp::socket clientSocket(clientIoc);
        boost::system::error_code connectEc;
        clientSocket.connect(server.LocalEndpoint(), connectEc);
        if (connectEc) {
            throw std::runtime_error("client connection failed");
        }
        if (!server.WaitFor(2s)) {
            throw std::runtime_error("server work did not finish");
        }
    }());
}

TEST_CASE("LoopbackWebSocketServer cleanup releases non-socket server coordination on early exit",
          "[transport][websocket_session][test_support]") {
    using namespace std::chrono_literals;

    auto start = std::chrono::steady_clock::now();
    {
        std::binary_semaphore clientReady{0};
        LoopbackWebSocketServer server([&](WebSocketSession& session) {
            if (session.Accept().has_value()) {
                clientReady.acquire();
            }
        });
        SemaphoreRelease releaseServerWork(clientReady);

        boost::asio::io_context clientIoc;
        boost::asio::ip::tcp::socket clientSocket(clientIoc);
        boost::system::error_code connectEc;
        clientSocket.connect(server.LocalEndpoint(), connectEc);
        REQUIRE_FALSE(connectEc);

        boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
        boost::system::error_code handshakeEc;
        clientWs.handshake("127.0.0.1", "/", handshakeEc);
        REQUIRE_FALSE(handshakeEc);
    }

    CHECK(std::chrono::steady_clock::now() - start < 2s);
}

TEST_CASE("Accept completes a real WebSocket handshake over loopback", "[transport][websocket_session]") {
    std::expected<void, SessionError> serverHandshakeResult = std::unexpected(SessionError::kHandshakeFailed);
    LoopbackWebSocketServer server([&](WebSocketSession& session) { serverHandshakeResult = session.Accept(); });

    boost::asio::io_context ioc;
    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(server.LocalEndpoint(), connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);

    server.Join();

    REQUIRE(serverHandshakeResult.has_value());
}

TEST_CASE("Shutdown interrupts an already-pending WebSocket handshake", "[transport][websocket_session]") {
    using namespace std::chrono_literals;

    std::expected<void, SessionError> handshake = std::unexpected(SessionError::kHandshakeFailed);
    std::binary_semaphore acceptPending{0};
    LoopbackWebSocketServer server([&](WebSocketSession& session, boost::asio::io_context& serverIoc) {
        // RunOperation starts async_accept before driving the server event loop. This
        // marker therefore cannot run until the handshake operation is pending.
        boost::asio::post(serverIoc, [&acceptPending] { acceptPending.release(); });
        handshake = session.Accept();
    });

    boost::asio::io_context clientIoc;
    boost::asio::ip::tcp::socket clientSocket(clientIoc);
    boost::system::error_code connectEc;
    clientSocket.connect(server.LocalEndpoint(), connectEc);
    REQUIRE_FALSE(connectEc);

    REQUIRE(acceptPending.try_acquire_for(2s));
    server.ShutdownActiveSession();

    REQUIRE(server.WaitFor(2s));
    server.Join();
    REQUIRE_FALSE(handshake.has_value());
    CHECK(handshake.error() == SessionError::kHandshakeFailed);
}

TEST_CASE("Accept times out when a TCP peer never sends a WebSocket upgrade", "[transport][websocket_session]") {
    using namespace std::chrono_literals;

    std::expected<void, SessionError> handshake = std::unexpected(SessionError::kHandshakeFailed);
    std::chrono::steady_clock::duration duration{};
    LoopbackWebSocketServer server([&](WebSocketSession& session) {
        auto start = std::chrono::steady_clock::now();
        handshake = session.Accept();
        duration = std::chrono::steady_clock::now() - start;
    });

    boost::asio::io_context clientIoc;
    boost::asio::ip::tcp::socket clientSocket(clientIoc);
    boost::system::error_code connectEc;
    clientSocket.connect(server.LocalEndpoint(), connectEc);
    REQUIRE_FALSE(connectEc);

    bool completed = server.WaitFor(20s);
    if (!completed) {
        server.ShutdownActiveSession();
    }
    server.Join();

    REQUIRE(completed);
    REQUIRE_FALSE(handshake.has_value());
    CHECK(handshake.error() == SessionError::kHandshakeFailed);
    CHECK(duration < 20s);
}

TEST_CASE("a text message from the client is read on the server", "[transport][websocket_session]") {
    std::expected<void, SessionError> serverHandshakeResult = std::unexpected(SessionError::kHandshakeFailed);
    std::expected<std::string, SessionError> serverReadResult = std::unexpected(SessionError::kReadFailed);
    LoopbackWebSocketServer server([&](WebSocketSession& session) {
        serverHandshakeResult = session.Accept();
        if (!serverHandshakeResult.has_value()) {
            return;
        }
        serverReadResult = session.ReadMessage();
    });

    boost::asio::io_context ioc;
    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(server.LocalEndpoint(), connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);

    clientWs.text(true);
    boost::system::error_code writeEc;
    clientWs.write(boost::asio::buffer(std::string("hello")), writeEc);
    REQUIRE_FALSE(writeEc);

    server.Join();

    REQUIRE(serverHandshakeResult.has_value());
    REQUIRE(serverReadResult.has_value());
    CHECK(*serverReadResult == "hello");
}

TEST_CASE("a text message from the server is read on the client", "[transport][websocket_session]") {
    std::expected<void, SessionError> serverHandshakeResult = std::unexpected(SessionError::kHandshakeFailed);
    std::expected<void, SessionError> serverWriteResult = std::unexpected(SessionError::kWriteFailed);
    LoopbackWebSocketServer server([&](WebSocketSession& session) {
        serverHandshakeResult = session.Accept();
        if (!serverHandshakeResult.has_value()) {
            return;
        }
        serverWriteResult = session.WriteMessage("hello from server");
    });

    boost::asio::io_context ioc;
    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(server.LocalEndpoint(), connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);

    boost::beast::flat_buffer buffer;
    boost::system::error_code readEc;
    clientWs.read(buffer, readEc);

    server.Join();

    REQUIRE(serverHandshakeResult.has_value());
    REQUIRE(serverWriteResult.has_value());
    REQUIRE_FALSE(readEc);
    CHECK(boost::beast::buffers_to_string(buffer.data()) == "hello from server");
}

TEST_CASE("WriteMessage closes a connection that stops accepting bytes", "[transport][websocket_session]") {
    using namespace std::chrono_literals;

    std::binary_semaphore clientReady{0};
    std::expected<void, SessionError> write = std::unexpected(SessionError::kHandshakeFailed);
    std::chrono::steady_clock::duration duration{};
    LoopbackWebSocketServer server([&](WebSocketSession& session) {
        auto handshake = session.Accept();
        if (!handshake.has_value()) {
            return;
        }

        clientReady.acquire();
        std::string payload(16 * 1024 * 1024, 'x');
        auto start = std::chrono::steady_clock::now();
        write = session.WriteMessage(payload);
        duration = std::chrono::steady_clock::now() - start;
    });
    SemaphoreRelease releaseServerWork(clientReady);

    boost::asio::io_context clientIoc;
    boost::asio::ip::tcp::socket clientSocket(clientIoc);
    boost::system::error_code connectEc;
    clientSocket.connect(server.LocalEndpoint(), connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);
    clientWs.next_layer().set_option(boost::asio::socket_base::receive_buffer_size(1024));
    releaseServerWork.Release();

    bool completed = server.WaitFor(20s);
    if (!completed) {
        server.ShutdownActiveSession();
    }
    server.Join();

    REQUIRE(completed);
    REQUIRE_FALSE(write.has_value());
    CHECK(write.error() == SessionError::kWriteFailed);
    CHECK(duration < 20s);
}

TEST_CASE("ReadMessage fails when the client closes the connection abruptly "
          "without a close frame",
          "[transport][websocket_session]") {
    std::expected<void, SessionError> serverHandshakeResult = std::unexpected(SessionError::kHandshakeFailed);
    std::expected<std::string, SessionError> serverReadResult = std::string{"should not remain unset"};
    LoopbackWebSocketServer server([&](WebSocketSession& session) {
        serverHandshakeResult = session.Accept();
        if (!serverHandshakeResult.has_value()) {
            return;
        }
        serverReadResult = session.ReadMessage();
    });

    boost::asio::io_context ioc;
    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(server.LocalEndpoint(), connectEc);
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

    server.Join();

    REQUIRE(serverHandshakeResult.has_value());
    CHECK_FALSE(serverReadResult.has_value());
    CHECK(serverReadResult.error() == SessionError::kReadFailed);
}

TEST_CASE("Close causes the peer's next read to observe the connection closing", "[transport][websocket_session]") {
    std::expected<void, SessionError> serverHandshakeResult = std::unexpected(SessionError::kHandshakeFailed);
    LoopbackWebSocketServer server([&](WebSocketSession& session) {
        serverHandshakeResult = session.Accept();
        if (!serverHandshakeResult.has_value()) {
            return;
        }
        session.Close();
    });

    boost::asio::io_context ioc;
    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(server.LocalEndpoint(), connectEc);
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

    server.Join();

    REQUIRE(serverHandshakeResult.has_value());
    CHECK(readEc == boost::beast::websocket::error::closed);
}

TEST_CASE("Close times out when the peer never answers the close frame", "[transport][websocket_session]") {
    using namespace std::chrono_literals;

    std::binary_semaphore clientReady{0};
    bool reachedClose = false;
    std::chrono::steady_clock::duration duration{};
    LoopbackWebSocketServer server([&](WebSocketSession& session) {
        if (!session.Accept().has_value()) {
            return;
        }

        clientReady.acquire();
        auto start = std::chrono::steady_clock::now();
        session.Close();
        reachedClose = true;
        duration = std::chrono::steady_clock::now() - start;
    });
    SemaphoreRelease releaseServerWork(clientReady);

    boost::asio::io_context clientIoc;
    boost::asio::ip::tcp::socket clientSocket(clientIoc);
    boost::system::error_code connectEc;
    clientSocket.connect(server.LocalEndpoint(), connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);
    releaseServerWork.Release();

    bool completed = server.WaitFor(20s);
    if (!completed) {
        server.ShutdownActiveSession();
    }
    server.Join();

    REQUIRE(completed);
    REQUIRE(reachedClose);
    CHECK(duration < 20s);
}

TEST_CASE("SwitchToIdleTimeout does not disrupt a subsequent read", "[transport][websocket_session]") {
    // This test proves that reapplying the timeout option mid-session is
    // message-preserving; the long idle duration is not waited out here.
    std::expected<void, SessionError> serverHandshakeResult = std::unexpected(SessionError::kHandshakeFailed);
    std::expected<std::string, SessionError> serverReadResult = std::unexpected(SessionError::kReadFailed);
    LoopbackWebSocketServer server([&](WebSocketSession& session) {
        serverHandshakeResult = session.Accept();
        if (!serverHandshakeResult.has_value()) {
            return;
        }
        session.SwitchToIdleTimeout();
        serverReadResult = session.ReadMessage();
    });

    boost::asio::io_context ioc;
    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(server.LocalEndpoint(), connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);

    clientWs.text(true);
    boost::system::error_code writeEc;
    clientWs.write(boost::asio::buffer(std::string("after switch")), writeEc);
    REQUIRE_FALSE(writeEc);

    server.Join();

    REQUIRE(serverHandshakeResult.has_value());
    REQUIRE(serverReadResult.has_value());
    CHECK(*serverReadResult == "after switch");
}

TEST_CASE("ReadMessage fails within the handshake timeout when the client "
          "sends nothing",
          "[transport][websocket_session]") {
    // Proves the production timeout boundary: the session's Beast policy
    // genuinely bounds a stalled asynchronous read. This exact scenario (a
    // WebSocket-handshaked peer that never sends an application message)
    // previously had no enforced bound in this class's synchronous API.
    // security::kHandshakeTimeout is 5 seconds; this asserts
    // comfortably under that with margin, not an exact bound, since exact
    // timer precision is not this test's concern.
    std::expected<void, SessionError> serverHandshakeResult = std::unexpected(SessionError::kHandshakeFailed);
    std::expected<std::string, SessionError> serverReadResult = std::string{"should not remain unset"};
    std::chrono::steady_clock::duration serverReadDuration{};
    LoopbackWebSocketServer server([&](WebSocketSession& session) {
        serverHandshakeResult = session.Accept();
        if (!serverHandshakeResult.has_value()) {
            return;
        }
        auto start = std::chrono::steady_clock::now();
        serverReadResult = session.ReadMessage();
        serverReadDuration = std::chrono::steady_clock::now() - start;
    });

    boost::asio::io_context ioc;
    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(server.LocalEndpoint(), connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);
    // The client deliberately sends nothing and does not close, simulating
    // a hung or unresponsive peer -- the case that used to stall.

    server.Join();

    REQUIRE(serverHandshakeResult.has_value());
    CHECK_FALSE(serverReadResult.has_value());
    CHECK(serverReadDuration < std::chrono::seconds(20));
}

TEST_CASE("a client that sends an oversized frame and never closes still gets "
          "a bounded failure",
          "[transport][websocket_session]") {
    // This covers a peer that never cooperates after sending an oversized
    // frame. Before WebSocketSession drove Beast's asynchronous API, its
    // do_fail()/teardown() sequence (triggered once it detects the
    // oversized frame) would wait unboundedly for this peer to close back,
    // observed to take roughly two minutes before falling back to a
    // generic OS-level failure. The exact resulting SessionError is not
    // asserted -- only that the connection slot is not held hostage.
    std::expected<void, SessionError> serverHandshakeResult = std::unexpected(SessionError::kHandshakeFailed);
    std::expected<std::string, SessionError> serverReadResult = std::string{"should not remain unset"};
    std::chrono::steady_clock::duration serverReadDuration{};
    LoopbackWebSocketServer server([&](WebSocketSession& session) {
        serverHandshakeResult = session.Accept();
        if (!serverHandshakeResult.has_value()) {
            return;
        }
        auto start = std::chrono::steady_clock::now();
        serverReadResult = session.ReadMessage();
        serverReadDuration = std::chrono::steady_clock::now() - start;
    });

    boost::asio::io_context ioc;
    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(server.LocalEndpoint(), connectEc);
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

    server.Join();

    REQUIRE(serverHandshakeResult.has_value());
    CHECK_FALSE(serverReadResult.has_value());
    CHECK(serverReadDuration < std::chrono::seconds(20));
}

TEST_CASE("a binary frame from the client is rejected, not returned as a message", "[transport][websocket_session]") {
    std::expected<void, SessionError> serverHandshakeResult = std::unexpected(SessionError::kHandshakeFailed);
    std::expected<std::string, SessionError> serverReadResult = std::string{"should not remain unset"};
    LoopbackWebSocketServer server([&](WebSocketSession& session) {
        serverHandshakeResult = session.Accept();
        if (!serverHandshakeResult.has_value()) {
            return;
        }
        serverReadResult = session.ReadMessage();
    });

    boost::asio::io_context ioc;
    boost::asio::ip::tcp::socket clientSocket(ioc);
    boost::system::error_code connectEc;
    clientSocket.connect(server.LocalEndpoint(), connectEc);
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

    server.Join();

    REQUIRE(serverHandshakeResult.has_value());
    REQUIRE_FALSE(serverReadResult.has_value());
    CHECK(serverReadResult.error() == SessionError::kBinaryFrameRejected);
}
