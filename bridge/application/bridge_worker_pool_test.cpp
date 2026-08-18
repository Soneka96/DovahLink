#include "application/bridge_worker_pool.hpp"

#include "protocol/bounded_json.hpp"
#include "protocol/envelope.hpp"
#include "security/hex.hpp"
#include "security/trust_store.hpp"
#include "transport/loopback_test_support.hpp"

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
#include <future>
#include <stdexcept>
#include <string>
#include <string_view>
#include <thread>
#include <vector>

using dovahlink::application::ActivePlayContext;
using dovahlink::application::BridgeWorkerPool;
using dovahlink::application::ContainedWork;
using dovahlink::application::ContainedWorkRunner;
using dovahlink::application::PairingNotificationSink;
using dovahlink::application::SessionManager;
using dovahlink::security::DecodeHex;
using dovahlink::security::EncodeHex;
using dovahlink::security::FailedTokenThrottle;
using dovahlink::security::ITrustStorePersistence;
using dovahlink::security::PairingSession;
using dovahlink::security::TokenStore;
using dovahlink::security::TrustStore;
using dovahlink::security::TrustStoreSnapshot;
using dovahlink::transport::ConnectionSlot;
using dovahlink::transport::LoopbackListener;
using dovahlink::transport::test_support::RequireLoopbackListener;

namespace {

constexpr const char* kValidHexToken = "0123456789abcdefABCDEF00112233445566778899aabbccddeeff0011223344";
/// Bridge version reported by the fixture's worker pool.
constexpr const char* kBridgeVersion = "0.2.0";

/// Builds the valid client hello used by real worker-pool sessions.
std::string ValidHello() {
    return R"({"messageType": "hello", "messageId": "message-hello-1", )"
           R"("sessionId": null, "correlationId": null, "payload": {"endpoint": "client", )"
           R"("clientId": "client-1", "auth": {"method": "one_time_local_token", )"
           R"("token": ")" +
           std::string(kValidHexToken) + R"("}}, )"
           R"("bridgeInstanceId": null, "playContextId": null, "clientId": null})";
}

/// `ITrustStorePersistence` double that always loads an empty snapshot -- these tests only
/// exercise the one_time_local_token auth path and never touch the trust store.
class EmptyPersistence : public ITrustStorePersistence {
public:
    /// Always reports a valid, empty snapshot.
    std::optional<TrustStoreSnapshot> Load() override { return TrustStoreSnapshot{}; }

    /// Always succeeds without recording anything.
    bool Save(const TrustStoreSnapshot&) override { return true; }
};

/// `PairingNotificationSink` double that records every code it is given. Pairing behavior itself
/// is exercised in message_dispatcher_test.cpp and pairing_handler_test.cpp; these tests only need
/// a working sink to satisfy `BridgeWorkerPool`'s signature.
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

/// Provides the same catch-all semantics as the coordinator for isolated pool
/// tests.
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

/// Owns the real transport and application dependencies shared by worker-pool
/// tests.
struct Fixture {
    /// I/O context supplied to both loopback listeners.
    boost::asio::io_context ioc;
    /// Accepts test connections over IPv4.
    LoopbackListener listenerV4 = RequireLoopbackListener(ioc, LoopbackListener::IpVersion::kV4);
    /// Accepts test connections over IPv6.
    LoopbackListener listenerV6 = RequireLoopbackListener(ioc, LoopbackListener::IpVersion::kV6);
    /// Enforces the one-active-connection limit.
    ConnectionSlot slot;
    /// Holds the one-time token accepted by the test session.
    TokenStore tokenStore{*DecodeHex(kValidHexToken)};
    /// Tracks failed token attempts for the test session.
    FailedTokenThrottle tokenThrottle;
    /// Backing store for `trustStore`, empty for these tests.
    EmptyPersistence persistence;
    /// Persistent trust store; unused by the one_time_local_token path these tests exercise.
    TrustStore trustStore = TrustStore::Load(persistence);
    /// Tracks failed device-credential attempts; unused by these tests.
    FailedTokenThrottle credentialThrottle;
    /// Pairing challenge/pending-credential state machine; unused by these tests.
    PairingSession pairingSession;
    /// Records pairing codes displayed to the user; unused by these tests.
    RecordingPairingNotificationSink pairingNotificationSink;
    /// Tracks the authenticated test session.
    SessionManager sessionManager;
    /// Source of the acquired play context; empty (kNoContext) for these tests.
    ActivePlayContext activePlayContext;
    /// Runs the production worker-pool/session path under test.
    BridgeWorkerPool pool{listenerV4,        listenerV6,          slot,      tokenStore,
                         tokenThrottle,     trustStore,          credentialThrottle,
                         sessionManager,    activePlayContext,   pairingSession,
                         pairingNotificationSink, /*bridgeInstanceId=*/std::nullopt,
                         /*bridgeVersion=*/kBridgeVersion};
};

}  // namespace

TEST_CASE("RequireLoopbackListener reports listener creation failure before dependent construction",
          "[application][bridge_worker_pool][test_support]") {
    boost::asio::io_context ioc;
    LoopbackListener occupied = RequireLoopbackListener(ioc, LoopbackListener::IpVersion::kV4);

    CHECK_THROWS_AS(RequireLoopbackListener(ioc, LoopbackListener::IpVersion::kV4, occupied.LocalEndpoint().port()),
                    std::runtime_error);
}

TEST_CASE("BridgeWorkerPool runs a real session for an accepted connection", "[application][bridge_worker_pool]") {
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

    std::string hello = ValidHello();
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

TEST_CASE("BridgeWorkerPool closes a new connection before any handshake when "
          "the slot is already occupied",
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

TEST_CASE("BridgeWorkerPool releases the slot and accepts again after "
          "contained connection work fails",
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

TEST_CASE("BridgeWorkerPool Stop and Join terminate promptly with no active "
          "connections",
          "[application][bridge_worker_pool]") {
    Fixture fixture;
    fixture.pool.Start(MakeContainedWorkRunner());

    auto start = std::chrono::steady_clock::now();
    fixture.pool.Stop();
    fixture.pool.Join();
    auto elapsed = std::chrono::steady_clock::now() - start;

    CHECK(elapsed < std::chrono::seconds(5));
}

TEST_CASE("BridgeWorkerPool Stop interrupts a connection blocked on the "
          "WebSocket handshake",
          "[application][bridge_worker_pool]") {
    using namespace std::chrono_literals;

    Fixture fixture;
    fixture.pool.Start(MakeContainedWorkRunner());

    boost::asio::io_context clientIoc;
    boost::asio::ip::tcp::socket clientSocket(clientIoc);
    boost::system::error_code connectEc;
    clientSocket.connect(fixture.listenerV4.LocalEndpoint(), connectEc);
    REQUIRE_FALSE(connectEc);

    auto acceptedDeadline = std::chrono::steady_clock::now() + 2s;
    while (!fixture.slot.IsOccupied() && std::chrono::steady_clock::now() < acceptedDeadline) {
        std::this_thread::yield();
    }
    REQUIRE(fixture.slot.IsOccupied());

    auto shutdown = std::async(std::launch::async, [&fixture] {
        fixture.pool.Stop();
        fixture.pool.Join();
    });

    REQUIRE(shutdown.wait_for(2s) == std::future_status::ready);
    shutdown.get();
    CHECK_FALSE(fixture.slot.IsOccupied());
}

TEST_CASE("BridgeWorkerPool Stop interrupts an authenticated session blocked "
          "on an idle read",
          "[application][bridge_worker_pool]") {
    using namespace std::chrono_literals;

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

    clientWs.text(true);
    boost::system::error_code writeEc;
    clientWs.write(boost::asio::buffer(ValidHello()), writeEc);
    REQUIRE_FALSE(writeEc);

    boost::beast::flat_buffer helloBuffer;
    boost::system::error_code helloReadEc;
    clientWs.read(helloBuffer, helloReadEc);
    REQUIRE_FALSE(helloReadEc);
    auto parsedHello = dovahlink::protocol::ParseBoundedJson(boost::beast::buffers_to_string(helloBuffer.data()));
    REQUIRE(parsedHello.has_value());
    auto helloAck = dovahlink::protocol::DecodeEnvelope(*parsedHello);
    REQUIRE(helloAck.has_value());
    REQUIRE(helloAck->sessionId.has_value());
    std::string sessionId = *helloAck->sessionId;

    boost::beast::flat_buffer capabilitiesBuffer;
    boost::system::error_code capabilitiesReadEc;
    clientWs.read(capabilitiesBuffer, capabilitiesReadEc);
    REQUIRE_FALSE(capabilitiesReadEc);

    auto shutdown = std::async(std::launch::async, [&fixture] {
        fixture.pool.Stop();
        fixture.pool.Join();
    });

    REQUIRE(shutdown.wait_for(2s) == std::future_status::ready);
    shutdown.get();
    CHECK_FALSE(fixture.slot.IsOccupied());
    CHECK_FALSE(fixture.sessionManager.IsValidForConnection(sessionId, 1));
}

TEST_CASE("BridgeWorkerPool DisconnectIfClientActive is a no-op when no session is active",
          "[application][bridge_worker_pool]") {
    Fixture fixture;
    fixture.pool.Start(MakeContainedWorkRunner());

    fixture.pool.DisconnectIfClientActive("client-1");

    fixture.pool.Stop();
    fixture.pool.Join();
}

TEST_CASE("BridgeWorkerPool DisconnectActive is a no-op when no connection was ever accepted",
          "[application][bridge_worker_pool]") {
    Fixture fixture;
    fixture.pool.Start(MakeContainedWorkRunner());

    fixture.pool.DisconnectActive();

    fixture.pool.Stop();
    fixture.pool.Join();
}

TEST_CASE("BridgeWorkerPool DisconnectIfClientActive interrupts an authenticated session blocked "
          "on an idle read when the clientId matches",
          "[application][bridge_worker_pool]") {
    using namespace std::chrono_literals;

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

    clientWs.text(true);
    boost::system::error_code writeEc;
    clientWs.write(boost::asio::buffer(ValidHello()), writeEc);
    REQUIRE_FALSE(writeEc);

    boost::beast::flat_buffer helloBuffer;
    boost::system::error_code helloReadEc;
    clientWs.read(helloBuffer, helloReadEc);
    REQUIRE_FALSE(helloReadEc);
    auto parsedHello = dovahlink::protocol::ParseBoundedJson(boost::beast::buffers_to_string(helloBuffer.data()));
    REQUIRE(parsedHello.has_value());
    auto helloAck = dovahlink::protocol::DecodeEnvelope(*parsedHello);
    REQUIRE(helloAck.has_value());
    REQUIRE(helloAck->sessionId.has_value());
    std::string sessionId = *helloAck->sessionId;

    boost::beast::flat_buffer capabilitiesBuffer;
    boost::system::error_code capabilitiesReadEc;
    clientWs.read(capabilitiesBuffer, capabilitiesReadEc);
    REQUIRE_FALSE(capabilitiesReadEc);

    fixture.pool.DisconnectIfClientActive("client-1");

    auto deadline = std::chrono::steady_clock::now() + 2s;
    while (fixture.sessionManager.IsValidForConnection(sessionId, 1) && std::chrono::steady_clock::now() < deadline) {
        std::this_thread::yield();
    }
    CHECK_FALSE(fixture.sessionManager.IsValidForConnection(sessionId, 1));

    fixture.pool.Stop();
    fixture.pool.Join();
}

TEST_CASE("BridgeWorkerPool DisconnectIfClientActive leaves the active session running for a "
          "non-matching clientId",
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

    clientWs.text(true);
    boost::system::error_code writeEc;
    clientWs.write(boost::asio::buffer(ValidHello()), writeEc);
    REQUIRE_FALSE(writeEc);

    boost::beast::flat_buffer helloBuffer;
    boost::system::error_code helloReadEc;
    clientWs.read(helloBuffer, helloReadEc);
    REQUIRE_FALSE(helloReadEc);
    auto parsedHello = dovahlink::protocol::ParseBoundedJson(boost::beast::buffers_to_string(helloBuffer.data()));
    REQUIRE(parsedHello.has_value());
    auto helloAck = dovahlink::protocol::DecodeEnvelope(*parsedHello);
    REQUIRE(helloAck.has_value());
    REQUIRE(helloAck->sessionId.has_value());
    std::string sessionId = *helloAck->sessionId;

    boost::beast::flat_buffer capabilitiesBuffer;
    boost::system::error_code capabilitiesReadEc;
    clientWs.read(capabilitiesBuffer, capabilitiesReadEc);
    REQUIRE_FALSE(capabilitiesReadEc);

    fixture.pool.DisconnectIfClientActive("someone-else");

    // Proves the mismatch genuinely left the session alone, rather than merely not yet having
    // torn it down: a ping sent afterward still round-trips over the same connection.
    std::string ping = R"({"messageType": "ping", "messageId": "message-ping-1", "sessionId": ")" + sessionId +
                       R"(", "correlationId": null, "payload": {}, )"
                       R"("bridgeInstanceId": null, "playContextId": null, "clientId": null})";
    clientWs.write(boost::asio::buffer(ping), writeEc);
    REQUIRE_FALSE(writeEc);

    boost::beast::flat_buffer pongBuffer;
    boost::system::error_code pongReadEc;
    clientWs.read(pongBuffer, pongReadEc);
    REQUIRE_FALSE(pongReadEc);
    auto parsedPong = dovahlink::protocol::ParseBoundedJson(boost::beast::buffers_to_string(pongBuffer.data()));
    REQUIRE(parsedPong.has_value());
    auto pong = dovahlink::protocol::DecodeEnvelope(*parsedPong);
    REQUIRE(pong.has_value());
    CHECK(pong->messageType == "pong");
    CHECK(fixture.sessionManager.IsValidForConnection(sessionId, 1));

    fixture.pool.Stop();
    fixture.pool.Join();
}

TEST_CASE("BridgeWorkerPool DisconnectIfClientActive does not disconnect a new connection using a "
          "stale clientId from a previous, already-ended connection",
          "[application][bridge_worker_pool]") {
    Fixture fixture;
    fixture.pool.Start(MakeContainedWorkRunner());

    // First connection: authenticates as "client-1" using the fixture's one-time token, then
    // closes cleanly, freeing the slot for a second, different connection.
    {
        boost::asio::io_context clientIoc;
        boost::asio::ip::tcp::socket clientSocket(clientIoc);
        boost::system::error_code connectEc;
        clientSocket.connect(fixture.listenerV4.LocalEndpoint(), connectEc);
        REQUIRE_FALSE(connectEc);

        boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
        boost::system::error_code handshakeEc;
        clientWs.handshake("127.0.0.1", "/", handshakeEc);
        REQUIRE_FALSE(handshakeEc);

        clientWs.text(true);
        boost::system::error_code writeEc;
        clientWs.write(boost::asio::buffer(ValidHello()), writeEc);
        REQUIRE_FALSE(writeEc);

        boost::beast::flat_buffer helloBuffer;
        boost::system::error_code helloReadEc;
        clientWs.read(helloBuffer, helloReadEc);
        REQUIRE_FALSE(helloReadEc);

        boost::beast::flat_buffer capabilitiesBuffer;
        boost::system::error_code capabilitiesReadEc;
        clientWs.read(capabilitiesBuffer, capabilitiesReadEc);
        REQUIRE_FALSE(capabilitiesReadEc);

        boost::system::error_code closeEc;
        clientWs.close(boost::beast::websocket::close_code::normal, closeEc);
    }

    auto slotFreedDeadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);
    while (fixture.slot.IsOccupied() && std::chrono::steady_clock::now() < slotFreedDeadline) {
        std::this_thread::yield();
    }
    REQUIRE_FALSE(fixture.slot.IsOccupied());

    // Second connection: a different client, authenticated by a persisted trust credential --
    // the fixture's one-time token was already consumed above, so it cannot authenticate a
    // second session as "client-1" again even if this test wanted it to.
    std::vector<std::uint8_t> credential{1, 2, 3, 4, 5, 6, 7, 8};
    REQUIRE(fixture.trustStore.Persist("client-2", credential, std::nullopt).has_value());

    boost::asio::io_context secondClientIoc;
    boost::asio::ip::tcp::socket secondClientSocket(secondClientIoc);
    boost::system::error_code secondConnectEc;
    secondClientSocket.connect(fixture.listenerV4.LocalEndpoint(), secondConnectEc);
    REQUIRE_FALSE(secondConnectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> secondClientWs(std::move(secondClientSocket));
    boost::system::error_code secondHandshakeEc;
    secondClientWs.handshake("127.0.0.1", "/", secondHandshakeEc);
    REQUIRE_FALSE(secondHandshakeEc);

    std::string secondHello = R"({"messageType": "hello", "messageId": "message-hello-2", )"
                              R"("sessionId": null, "correlationId": null, "payload": {"endpoint": "client", )"
                              R"("clientId": "client-2", "auth": {"method": "trusted_device_credential", )"
                              R"("token": ")" +
                              EncodeHex(credential) + R"("}}, )"
                              R"("bridgeInstanceId": null, "playContextId": null, "clientId": null})";
    secondClientWs.text(true);
    boost::system::error_code secondWriteEc;
    secondClientWs.write(boost::asio::buffer(secondHello), secondWriteEc);
    REQUIRE_FALSE(secondWriteEc);

    boost::beast::flat_buffer secondHelloBuffer;
    boost::system::error_code secondHelloReadEc;
    secondClientWs.read(secondHelloBuffer, secondHelloReadEc);
    REQUIRE_FALSE(secondHelloReadEc);
    auto secondParsedHello =
        dovahlink::protocol::ParseBoundedJson(boost::beast::buffers_to_string(secondHelloBuffer.data()));
    REQUIRE(secondParsedHello.has_value());
    auto secondHelloAck = dovahlink::protocol::DecodeEnvelope(*secondParsedHello);
    REQUIRE(secondHelloAck.has_value());
    REQUIRE(secondHelloAck->messageType == "hello_ack");
    REQUIRE(secondHelloAck->sessionId.has_value());
    std::string secondSessionId = *secondHelloAck->sessionId;

    boost::beast::flat_buffer secondCapabilitiesBuffer;
    boost::system::error_code secondCapabilitiesReadEc;
    secondClientWs.read(secondCapabilitiesBuffer, secondCapabilitiesReadEc);
    REQUIRE_FALSE(secondCapabilitiesReadEc);

    // The stale identity from the connection that already ended above must not match the
    // connection now active, even though both connections were published as activeSocket_ in turn.
    fixture.pool.DisconnectIfClientActive("client-1");

    // Proves the mismatch genuinely left the session alone, rather than merely not yet having torn
    // it down: a ping sent afterward still round-trips over the same connection.
    std::string ping = R"({"messageType": "ping", "messageId": "message-ping-1", "sessionId": ")" +
                       secondSessionId +
                       R"(", "correlationId": null, "payload": {}, )"
                       R"("bridgeInstanceId": null, "playContextId": null, "clientId": null})";
    boost::system::error_code pingWriteEc;
    secondClientWs.write(boost::asio::buffer(ping), pingWriteEc);
    REQUIRE_FALSE(pingWriteEc);

    boost::beast::flat_buffer pongBuffer;
    boost::system::error_code pongReadEc;
    secondClientWs.read(pongBuffer, pongReadEc);
    REQUIRE_FALSE(pongReadEc);
    auto parsedPong = dovahlink::protocol::ParseBoundedJson(boost::beast::buffers_to_string(pongBuffer.data()));
    REQUIRE(parsedPong.has_value());
    auto pong = dovahlink::protocol::DecodeEnvelope(*parsedPong);
    REQUIRE(pong.has_value());
    CHECK(pong->messageType == "pong");
    CHECK(fixture.sessionManager.IsValidForConnection(secondSessionId, 2));

    // The negative result above isn't proof activeConnectionId_ tracks the new connection at all --
    // confirm the matching id, "client-2", still tears the session down correctly.
    fixture.pool.DisconnectIfClientActive("client-2");

    auto revokedDeadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);
    while (fixture.sessionManager.IsValidForConnection(secondSessionId, 2) &&
           std::chrono::steady_clock::now() < revokedDeadline) {
        std::this_thread::yield();
    }
    CHECK_FALSE(fixture.sessionManager.IsValidForConnection(secondSessionId, 2));

    fixture.pool.Stop();
    fixture.pool.Join();
}

TEST_CASE("BridgeWorkerPool DisconnectActive interrupts an authenticated session blocked on an "
          "idle read regardless of clientId",
          "[application][bridge_worker_pool]") {
    using namespace std::chrono_literals;

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

    clientWs.text(true);
    boost::system::error_code writeEc;
    clientWs.write(boost::asio::buffer(ValidHello()), writeEc);
    REQUIRE_FALSE(writeEc);

    boost::beast::flat_buffer helloBuffer;
    boost::system::error_code helloReadEc;
    clientWs.read(helloBuffer, helloReadEc);
    REQUIRE_FALSE(helloReadEc);
    auto parsedHello = dovahlink::protocol::ParseBoundedJson(boost::beast::buffers_to_string(helloBuffer.data()));
    REQUIRE(parsedHello.has_value());
    auto helloAck = dovahlink::protocol::DecodeEnvelope(*parsedHello);
    REQUIRE(helloAck.has_value());
    REQUIRE(helloAck->sessionId.has_value());
    std::string sessionId = *helloAck->sessionId;

    boost::beast::flat_buffer capabilitiesBuffer;
    boost::system::error_code capabilitiesReadEc;
    clientWs.read(capabilitiesBuffer, capabilitiesReadEc);
    REQUIRE_FALSE(capabilitiesReadEc);

    fixture.pool.DisconnectActive();

    auto deadline = std::chrono::steady_clock::now() + 2s;
    while (fixture.sessionManager.IsValidForConnection(sessionId, 1) && std::chrono::steady_clock::now() < deadline) {
        std::this_thread::yield();
    }
    CHECK_FALSE(fixture.sessionManager.IsValidForConnection(sessionId, 1));

    // A second call after the session already ended finds activeSocket_'s weak_ptr expired rather
    // than merely unset -- a distinct guard path from "never had a connection at all," and must be
    // just as safe to call again (for example a double revoke).
    fixture.pool.DisconnectActive();
    fixture.pool.DisconnectIfClientActive("client-1");

    fixture.pool.Stop();
    fixture.pool.Join();
}
