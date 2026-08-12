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

#include <chrono>
#include <string>

using dovahlink::application::BridgeWorkerPool;
using dovahlink::application::CharacterSnapshot;
using dovahlink::application::CharacterStateProvider;
using dovahlink::application::SessionManager;
using dovahlink::security::DecodeHex;
using dovahlink::security::FailedTokenThrottle;
using dovahlink::security::TokenStore;
using dovahlink::transport::ConnectionSlot;
using dovahlink::transport::LoopbackListener;

namespace {

constexpr const char* kValidHexToken = "0123456789abcdefABCDEF00112233445566778899aabbccddeeff0011223344";

class FakeCharacterStateProvider : public CharacterStateProvider {
public:
    [[nodiscard]] CharacterSnapshot CurrentCharacterSnapshot() const override {
        return CharacterSnapshot{.level = 1};
    }
};

struct Fixture {
    boost::asio::io_context ioc;
    LoopbackListener listenerV4 = *LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    LoopbackListener listenerV6 = *LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV6, 0);
    ConnectionSlot slot;
    TokenStore tokenStore{*DecodeHex(kValidHexToken)};
    FailedTokenThrottle tokenThrottle;
    SessionManager sessionManager;
    FakeCharacterStateProvider stateProvider;
    BridgeWorkerPool pool{listenerV4, listenerV6, slot, tokenStore, tokenThrottle, sessionManager, stateProvider};
};

}  // namespace

TEST_CASE("BridgeWorkerPool runs a real session for an accepted connection",
          "[application][bridge_worker_pool]") {
    Fixture fixture;
    fixture.pool.Start();

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
}

TEST_CASE("BridgeWorkerPool closes a new connection before any handshake when the slot is already occupied",
          "[application][bridge_worker_pool]") {
    Fixture fixture;
    REQUIRE(fixture.slot.TryAcquire());  // simulate an already-active connection.
    fixture.pool.Start();

    boost::asio::io_context clientIoc;
    boost::asio::ip::tcp::socket clientSocket(clientIoc);
    boost::system::error_code connectEc;
    clientSocket.connect(fixture.listenerV4.LocalEndpoint(), connectEc);
    REQUIRE_FALSE(connectEc);

    boost::beast::websocket::stream<boost::asio::ip::tcp::socket> clientWs(std::move(clientSocket));
    boost::system::error_code handshakeEc;
    clientWs.handshake("127.0.0.1", "/", handshakeEc);
    CHECK(handshakeEc);  // the server closed the raw socket before ever handshaking.

    fixture.slot.Release();
    fixture.pool.Stop();
    fixture.pool.Join();
}

TEST_CASE("BridgeWorkerPool Stop and Join terminate promptly with no active connections",
          "[application][bridge_worker_pool]") {
    Fixture fixture;
    fixture.pool.Start();

    auto start = std::chrono::steady_clock::now();
    fixture.pool.Stop();
    fixture.pool.Join();
    auto elapsed = std::chrono::steady_clock::now() - start;

    CHECK(elapsed < std::chrono::seconds(5));
}
