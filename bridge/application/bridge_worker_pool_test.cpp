#include "application/bridge_worker_pool.hpp"

#include "protocol/bounded_json.hpp"
#include "protocol/envelope.hpp"
#include "security/hex.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/asio/buffer.hpp>
#include <boost/asio/io_context.hpp>
#include <boost/asio/ip/tcp.hpp>
#include <boost/beast/core/buffers_to_string.hpp>
#include <boost/beast/core/flat_buffer.hpp>
#include <boost/beast/websocket/stream.hpp>
#include <boost/system/error_code.hpp>

#include <atomic>
#include <chrono>
#include <string>

using dovahlink::application::BridgeWorkerPool;
using dovahlink::application::CharacterSnapshot;
using dovahlink::application::CharacterStateProvider;
using dovahlink::application::ContainedWork;
using dovahlink::application::ContainedWorkRunner;
using dovahlink::application::SessionManager;
using dovahlink::security::DecodeHex;
using dovahlink::security::FailedTokenThrottle;
using dovahlink::security::TokenStore;
using dovahlink::transport::ConnectionSlot;
using dovahlink::transport::LoopbackListener;

namespace {

constexpr const char* kValidHexToken = "0123456789abcdefABCDEF00112233445566778899aabbccddeeff0011223344";

/// Provides the same catch-all semantics as the coordinator for isolated pool tests.
ContainedWorkRunner MakeContainedWorkRunner() {
    return [](ContainedWork work) noexcept {
        try {
            work();
            return true;
        } catch (...) {
            return false;
        }
    };
}

/// Supplies a deterministic character snapshot to the real worker-pool session.
class FakeCharacterStateProvider : public CharacterStateProvider {
public:
    /// @copydoc CharacterStateProvider::CurrentCharacterSnapshot
    [[nodiscard]] CharacterSnapshot CurrentCharacterSnapshot() const override { return CharacterSnapshot{.level = 1}; }
};

/// Owns the real transport and application dependencies shared by worker-pool tests.
struct Fixture {
    /// I/O context supplied to both loopback listeners.
    boost::asio::io_context ioc;
    /// Accepts test connections over IPv4.
    LoopbackListener listenerV4 = *LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    /// Accepts test connections over IPv6.
    LoopbackListener listenerV6 = *LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV6, 0);
    /// Enforces the one-active-connection limit.
    ConnectionSlot slot;
    /// Holds the one-time token accepted by the test session.
    TokenStore tokenStore{*DecodeHex(kValidHexToken)};
    /// Tracks failed token attempts for the test session.
    FailedTokenThrottle tokenThrottle;
    /// Tracks the authenticated test session.
    SessionManager sessionManager;
    /// Provides the deterministic character snapshot.
    FakeCharacterStateProvider stateProvider;
    /// Runs the production worker-pool/session path under test.
    BridgeWorkerPool pool{listenerV4, listenerV6, slot, tokenStore, tokenThrottle, sessionManager, stateProvider};
};

}  // namespace

TEST_CASE("BridgeWorkerPool runs a real session for an accepted connection",
          "[application][bridge_worker_pool]") {
    Fixture fixture;
    fixture.pool.Start(MakeContainedWorkRunner());

    boost::asio::io_context clientIoc;
    boost::asio::ip::tcp::socket clientSocket(clientIoc);
    boost::system::error_code connectEc;
    clientSocket.connect(fixture.listenerV4.LocalEndpoint(), connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    REQUIRE_FALSE(handshakeEc);

    std::string hello = R"({"protocolVersion": 0, "messageType": "hello", "messageId": "message-hello-1", )"
                        R"("sessionId": null, "correlationId": null, "payload": {"endpoint": "client", )"
                        R"("supportedProtocolVersions": [1], "auth": {"method": "one_time_local_token", )"
                        R"("token": ")" +
                        std::string(kValidHexToken) + R"("}}})";
    clientWs.text(true);
    boost::system::error_code writeEc;
    clientWs.write(boost::asio::buffer(hello), writeEc);
    REQUIRE_FALSE(writeEc);

    boost::beast::flat_buffer buffer;
    boost::system::error_code readEc;
    clientWs.read(buffer, readEc);
    REQUIRE_FALSE(readEc);
    auto parsed = dovahlink::protocol::ParseBoundedJson(boost::beast::buffers_to_string(buffer.data()));
    REQUIRE(parsed.has_value());
    auto envelope = dovahlink::protocol::DecodeEnvelope(*parsed);
    REQUIRE(envelope.has_value());
    CHECK(envelope->messageType == "hello_ack");

    boost::system::error_code closeEc;
    clientWs.close(boost::beast::websocket::close_code::normal, closeEc);

    fixture.pool.Stop();
    fixture.pool.Join();
    CHECK_FALSE(fixture.slot.IsOccupied());
}

TEST_CASE("BridgeWorkerPool closes a new connection before any handshake when the slot is already occupied",
          "[application][bridge_worker_pool]") {
    Fixture fixture;
    auto occupiedLease = fixture.slot.TryAcquire();  // simulate an already-active connection.
    REQUIRE(occupiedLease.has_value());
    fixture.pool.Start(MakeContainedWorkRunner());

    boost::asio::io_context clientIoc;
    boost::asio::ip::tcp::socket clientSocket(clientIoc);
    boost::system::error_code connectEc;
    clientSocket.connect(fixture.listenerV4.LocalEndpoint(), connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    CHECK(handshakeEc);  // the server closed the raw socket before ever handshaking.

    occupiedLease.reset();
    fixture.pool.Stop();
    fixture.pool.Join();
    CHECK_FALSE(fixture.slot.IsOccupied());
}

TEST_CASE("BridgeWorkerPool releases the slot and accepts again after contained connection work fails",
          "[application][bridge_worker_pool]") {
    Fixture fixture;
    std::atomic<int> containedConnectionFailures{0};
    ContainedWorkRunner workerRunner = [&containedConnectionFailures](ContainedWork work) noexcept {
        thread_local bool insideThreadBoundary = false;
        if (insideThreadBoundary) {
            containedConnectionFailures.fetch_add(1, std::memory_order_release);
            return false;
        }

        insideThreadBoundary = true;
        try {
            work();
        } catch (...) {
            insideThreadBoundary = false;
            return false;
        }
        insideThreadBoundary = false;
        return true;
    };
    fixture.pool.Start(std::move(workerRunner));

    for (int expectedFailures = 1; expectedFailures <= 2; ++expectedFailures) {
        boost::asio::io_context clientIoc;
        boost::asio::ip::tcp::socket clientSocket(clientIoc);
        boost::system::error_code connectEc;
        clientSocket.connect(fixture.listenerV4.LocalEndpoint(), connectEc);
        REQUIRE_FALSE(connectEc);

        boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
        boost::system::error_code handshakeEc;
        clientWs.handshake("127.0.0.1", "/", handshakeEc);
        REQUIRE(handshakeEc);
        CHECK(containedConnectionFailures.load(std::memory_order_acquire) == expectedFailures);
    }

    fixture.pool.Stop();
    fixture.pool.Join();
    CHECK_FALSE(fixture.slot.IsOccupied());
}

TEST_CASE("BridgeWorkerPool Stop and Join terminate promptly with no active connections",
          "[application][bridge_worker_pool]") {
    Fixture fixture;
    fixture.pool.Start(MakeContainedWorkRunner());

    auto start = std::chrono::steady_clock::now();
    fixture.pool.Stop();
    fixture.pool.Join();
    auto elapsed = std::chrono::steady_clock::now() - start;

    CHECK(elapsed < std::chrono::seconds(5));
}
