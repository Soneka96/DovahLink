#include "transport/loopback_listener.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/asio/io_context.hpp>
#include <boost/asio/ip/address.hpp>
#include <boost/asio/ip/tcp.hpp>
#include <boost/system/error_code.hpp>

#include <chrono>
#include <cstdint>
#include <expected>
#include <future>
#include <stdexcept>
#include <thread>
#include <utility>

using dovahlink::transport::AcceptError;
using dovahlink::transport::ListenerError;
using dovahlink::transport::LoopbackListener;

TEST_CASE("Create binds an IPv4 listener to exactly 127.0.0.1",
          "[transport][listener]") {
    auto listener = LoopbackListener::Create(LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());

    boost::asio::ip::tcp::endpoint endpoint = listener->LocalEndpoint();
    CHECK(endpoint.address() == boost::asio::ip::make_address("127.0.0.1"));
    CHECK(endpoint.address().is_loopback());
}

TEST_CASE("Create binds an IPv6 listener to exactly ::1",
          "[transport][listener]") {
    auto listener = LoopbackListener::Create(LoopbackListener::IpVersion::kV6, 0);
    REQUIRE(listener.has_value());

    boost::asio::ip::tcp::endpoint endpoint = listener->LocalEndpoint();
    CHECK(endpoint.address() == boost::asio::ip::make_address("::1"));
    CHECK(endpoint.address().is_loopback());
}

TEST_CASE("port 0 lets the OS assign an ephemeral port for each listener",
          "[transport][listener]") {
    auto first = LoopbackListener::Create(LoopbackListener::IpVersion::kV4, 0);
    auto second = LoopbackListener::Create(LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(first.has_value());
    REQUIRE(second.has_value());

    CHECK(first->LocalEndpoint().port() != 0);
    CHECK(second->LocalEndpoint().port() != 0);
    CHECK(first->LocalEndpoint().port() != second->LocalEndpoint().port());
}

TEST_CASE("binding a second IPv4 listener to a port already in use fails",
          "[transport][listener]") {
    auto first = LoopbackListener::Create(LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(first.has_value());
    std::uint16_t boundPort = first->LocalEndpoint().port();

    auto second =
        LoopbackListener::Create(LoopbackListener::IpVersion::kV4, boundPort);
    REQUIRE_FALSE(second.has_value());
    CHECK(second.error() == ListenerError::kBindFailed);
}

TEST_CASE("binding a second IPv6 listener to a port already in use fails",
          "[transport][listener]") {
    auto first = LoopbackListener::Create(LoopbackListener::IpVersion::kV6, 0);
    REQUIRE(first.has_value());
    std::uint16_t boundPort = first->LocalEndpoint().port();

    auto second =
        LoopbackListener::Create(LoopbackListener::IpVersion::kV6, boundPort);
    REQUIRE_FALSE(second.has_value());
    CHECK(second.error() == ListenerError::kBindFailed);
}

TEST_CASE("a moved-from listener's new owner still accepts connections",
          "[transport][listener]") {
    auto created = LoopbackListener::Create(LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(created.has_value());
    boost::asio::ip::tcp::endpoint endpoint = created->LocalEndpoint();

    LoopbackListener moved = std::move(*created);
    CHECK(moved.LocalEndpoint() == endpoint);

    boost::asio::io_context clientIoc;
    boost::asio::ip::tcp::socket client(clientIoc);
    boost::system::error_code connectEc;
    client.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    //  Exercises the owner thread through the moved-to handle, not just a
    //  direct synchronous Acceptor() call: proves the move genuinely carried
    //  the owner thread/io_context along, not merely the acceptor.
    auto accepted = moved.AcceptLoopbackOnly();
    REQUIRE(accepted.has_value());
    CHECK(accepted->is_open());
}

TEST_CASE("a move-assigned-to listener's new owner still accepts connections",
          "[transport][listener]") {
    auto created = LoopbackListener::Create(LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(created.has_value());
    boost::asio::ip::tcp::endpoint endpoint = created->LocalEndpoint();

    auto other = LoopbackListener::Create(LoopbackListener::IpVersion::kV6, 0);
    REQUIRE(other.has_value());
    *other = std::move(*created);
    CHECK(other->LocalEndpoint() == endpoint);

    boost::asio::io_context clientIoc;
    boost::asio::ip::tcp::socket client(clientIoc);
    boost::system::error_code connectEc;
    client.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    auto accepted = other->AcceptLoopbackOnly();
    REQUIRE(accepted.has_value());
    CHECK(accepted->is_open());
}

TEST_CASE("a moved-to listener's Close and Join still release the port",
          "[transport][listener]") {
    auto created = LoopbackListener::Create(LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(created.has_value());
    std::uint16_t boundPort = created->LocalEndpoint().port();

    LoopbackListener moved = std::move(*created);
    moved.Close();
    moved.Join();

    auto rebound =
        LoopbackListener::Create(LoopbackListener::IpVersion::kV4, boundPort);
    REQUIRE(rebound.has_value());
}

TEST_CASE("the destructor is safe to run after an explicit Join",
          "[transport][listener]") {
    std::uint16_t boundPort = 0;
    {
        auto listener =
            LoopbackListener::Create(LoopbackListener::IpVersion::kV4, 0);
        REQUIRE(listener.has_value());
        boundPort = listener->LocalEndpoint().port();

        listener->Close();
        listener->Join();
    }

    auto rebound =
        LoopbackListener::Create(LoopbackListener::IpVersion::kV4, boundPort);
    REQUIRE(rebound.has_value());
}

TEST_CASE("an IPv4 and an IPv6 listener can coexist",
          "[transport][listener]") {
    auto v4 = LoopbackListener::Create(LoopbackListener::IpVersion::kV4, 0);
    auto v6 = LoopbackListener::Create(LoopbackListener::IpVersion::kV6, 0);

    REQUIRE(v4.has_value());
    REQUIRE(v6.has_value());
    CHECK(v4->LocalEndpoint().address().is_v4());
    CHECK(v6->LocalEndpoint().address().is_v6());
}

TEST_CASE("a real client can connect to the IPv4 listener and the server can "
          "accept it",
          "[transport][listener]") {
    auto listener = LoopbackListener::Create(LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());

    boost::asio::io_context clientIoc;
    boost::asio::ip::tcp::socket client(clientIoc);
    boost::system::error_code connectEc;
    client.connect(listener->LocalEndpoint(), connectEc);
    REQUIRE_FALSE(connectEc);

    boost::system::error_code acceptEc;
    boost::asio::ip::tcp::socket accepted = listener->Acceptor().accept(acceptEc);
    REQUIRE_FALSE(acceptEc);
    CHECK(accepted.is_open());
    CHECK(accepted.remote_endpoint().address().is_loopback());
}

TEST_CASE("a real client can connect to the IPv6 listener and the server can "
          "accept it",
          "[transport][listener]") {
    auto listener = LoopbackListener::Create(LoopbackListener::IpVersion::kV6, 0);
    REQUIRE(listener.has_value());

    boost::asio::io_context clientIoc;
    boost::asio::ip::tcp::socket client(clientIoc);
    boost::system::error_code connectEc;
    client.connect(listener->LocalEndpoint(), connectEc);
    REQUIRE_FALSE(connectEc);

    boost::system::error_code acceptEc;
    boost::asio::ip::tcp::socket accepted = listener->Acceptor().accept(acceptEc);
    REQUIRE_FALSE(acceptEc);
    CHECK(accepted.is_open());
    CHECK(accepted.remote_endpoint().address().is_loopback());
}

TEST_CASE("Close makes the listener unavailable for a subsequent accept loop",
          "[transport][listener]") {
    auto listener = LoopbackListener::Create(LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());

    listener->Close();

    auto accepted = listener->AcceptLoopbackOnly();
    REQUIRE_FALSE(accepted.has_value());
    CHECK(accepted.error() == AcceptError::kAcceptFailed);
    CHECK_FALSE(listener->Acceptor().is_open());
}

TEST_CASE("Close is safe to call more than once",
          "[transport][listener]") {
    auto listener = LoopbackListener::Create(LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());

    listener->Close();
    listener->Close();

    auto accepted = listener->AcceptLoopbackOnly();
    CHECK_FALSE(accepted.has_value());
}

TEST_CASE("Close cancels a pending asynchronous accept before returning",
          "[transport][listener]") {
    using namespace std::chrono_literals;

    using AcceptResult = std::expected<boost::asio::ip::tcp::socket, AcceptError>;
    auto listener = LoopbackListener::Create(LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());

    std::promise<AcceptResult> resultPromise;
    auto resultFuture = resultPromise.get_future();
    std::thread acceptThread([&listener, &resultPromise] {
        resultPromise.set_value(listener->AcceptLoopbackOnly());
    });

    listener->Close();
    REQUIRE(resultFuture.wait_for(2s) == std::future_status::ready);
    auto accepted = resultFuture.get();
    REQUIRE_FALSE(accepted.has_value());
    CHECK(accepted.error() == AcceptError::kAcceptFailed);

    acceptThread.join();
    CHECK_FALSE(listener->Acceptor().is_open());
}

TEST_CASE("Close returns promptly even while an accept is outstanding",
          "[transport][listener]") {
    using namespace std::chrono_literals;

    auto listener = LoopbackListener::Create(LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());

    std::thread acceptThread(
        [&listener] { (void)listener->AcceptLoopbackOnly(); });

    auto start = std::chrono::steady_clock::now();
    listener->Close();
    auto elapsed = std::chrono::steady_clock::now() - start;
    CHECK(elapsed < 100ms);

    acceptThread.join();
}

TEST_CASE("Join is safe to call more than once",
          "[transport][listener]") {
    auto listener = LoopbackListener::Create(LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());

    listener->Close();
    listener->Join();
    listener->Join();
}

TEST_CASE("Join alone closes the listener even without an explicit prior "
          "Close",
          "[transport][listener]") {
    auto listener = LoopbackListener::Create(LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());
    std::uint16_t boundPort = listener->LocalEndpoint().port();

    listener->Join();

    auto rebound =
        LoopbackListener::Create(LoopbackListener::IpVersion::kV4, boundPort);
    REQUIRE(rebound.has_value());
}

TEST_CASE("Close and Join release the port so a new IPv4 listener can bind it "
          "immediately",
          "[transport][listener]") {
    //  Direct regression test for the original bug: LoopbackListener::Close()
    //  used to block indefinitely instead of guaranteeing the acceptor was
    //  closed before returning control to its caller, which left the port
    //  bound after shutdown ("Failed to bind the IPv4 loopback listener on
    //  port 58231").
    auto listener = LoopbackListener::Create(LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());
    std::uint16_t boundPort = listener->LocalEndpoint().port();

    listener->Close();
    listener->Join();

    auto rebound =
        LoopbackListener::Create(LoopbackListener::IpVersion::kV4, boundPort);
    REQUIRE(rebound.has_value());
}

TEST_CASE("Close and Join release the port so a new IPv6 listener can bind it "
          "immediately",
          "[transport][listener]") {
    auto listener = LoopbackListener::Create(LoopbackListener::IpVersion::kV6, 0);
    REQUIRE(listener.has_value());
    std::uint16_t boundPort = listener->LocalEndpoint().port();

    listener->Close();
    listener->Join();

    auto rebound =
        LoopbackListener::Create(LoopbackListener::IpVersion::kV6, boundPort);
    REQUIRE(rebound.has_value());
}

TEST_CASE("Close and Join release the port even while an accept was "
          "outstanding",
          "[transport][listener]") {
    auto listener = LoopbackListener::Create(LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());
    std::uint16_t boundPort = listener->LocalEndpoint().port();

    std::thread acceptThread(
        [&listener] { (void)listener->AcceptLoopbackOnly(); });

    listener->Close();
    //  Join()'s documented precondition: the thread that calls
    //  AcceptLoopbackOnly() must be joined first, so no call can still be
    //  starting once Join() stops the owner thread. BridgeWorkerPool::Join()
    //  follows the same order (its accept-loop threads before the listener's
    //  own Join()).
    acceptThread.join();
    listener->Join();

    auto rebound =
        LoopbackListener::Create(LoopbackListener::IpVersion::kV4, boundPort);
    REQUIRE(rebound.has_value());
}

TEST_CASE("Close and Join release the port after a real accept already "
          "completed",
          "[transport][listener]") {
    auto listener = LoopbackListener::Create(LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());
    std::uint16_t boundPort = listener->LocalEndpoint().port();
    boost::asio::ip::tcp::endpoint endpoint = listener->LocalEndpoint();

    using AcceptResult = std::expected<boost::asio::ip::tcp::socket, AcceptError>;
    std::promise<AcceptResult> resultPromise;
    auto resultFuture = resultPromise.get_future();
    std::thread acceptThread([&listener, &resultPromise] {
        resultPromise.set_value(listener->AcceptLoopbackOnly());
    });

    boost::asio::io_context clientIoc;
    boost::asio::ip::tcp::socket client(clientIoc);
    boost::system::error_code connectEc;
    client.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    auto accepted = resultFuture.get();
    acceptThread.join();
    REQUIRE(accepted.has_value());

    listener->Close();
    listener->Join();

    auto rebound =
        LoopbackListener::Create(LoopbackListener::IpVersion::kV4, boundPort);
    REQUIRE(rebound.has_value());
}
