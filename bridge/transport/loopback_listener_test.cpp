#include "transport/loopback_listener.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/asio/io_context.hpp>
#include <boost/asio/ip/address.hpp>
#include <boost/asio/ip/tcp.hpp>
#include <boost/system/error_code.hpp>

#include <chrono>
#include <exception>
#include <future>
#include <stdexcept>
#include <thread>
#include <utility>

using dovahlink::transport::ListenerError;
using dovahlink::transport::LoopbackListener;

TEST_CASE("Create binds an IPv4 listener to exactly 127.0.0.1",
          "[transport][listener]") {
    boost::asio::io_context ioc;
    auto listener =
        LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());

    boost::asio::ip::tcp::endpoint endpoint = listener->LocalEndpoint();
    CHECK(endpoint.address() == boost::asio::ip::make_address("127.0.0.1"));
    CHECK(endpoint.address().is_loopback());
}

TEST_CASE("Create binds an IPv6 listener to exactly ::1",
          "[transport][listener]") {
    boost::asio::io_context ioc;
    auto listener =
        LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV6, 0);
    REQUIRE(listener.has_value());

    boost::asio::ip::tcp::endpoint endpoint = listener->LocalEndpoint();
    CHECK(endpoint.address() == boost::asio::ip::make_address("::1"));
    CHECK(endpoint.address().is_loopback());
}

TEST_CASE("port 0 lets the OS assign an ephemeral port for each listener",
          "[transport][listener]") {
    boost::asio::io_context ioc;
    auto first =
        LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    auto second =
        LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(first.has_value());
    REQUIRE(second.has_value());

    CHECK(first->LocalEndpoint().port() != 0);
    CHECK(second->LocalEndpoint().port() != 0);
    CHECK(first->LocalEndpoint().port() != second->LocalEndpoint().port());
}

TEST_CASE("binding a second IPv4 listener to a port already in use fails",
          "[transport][listener]") {
    boost::asio::io_context ioc;
    auto first =
        LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(first.has_value());
    std::uint16_t boundPort = first->LocalEndpoint().port();

    auto second = LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4,
                                           boundPort);
    REQUIRE_FALSE(second.has_value());
    CHECK(second.error() == ListenerError::kBindFailed);
}

TEST_CASE("binding a second IPv6 listener to a port already in use fails",
          "[transport][listener]") {
    boost::asio::io_context ioc;
    auto first =
        LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV6, 0);
    REQUIRE(first.has_value());
    std::uint16_t boundPort = first->LocalEndpoint().port();

    auto second = LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV6,
                                           boundPort);
    REQUIRE_FALSE(second.has_value());
    CHECK(second.error() == ListenerError::kBindFailed);
}

TEST_CASE("a moved-from listener's new owner still accepts connections",
          "[transport][listener]") {
    boost::asio::io_context ioc;
    auto created =
        LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(created.has_value());
    boost::asio::ip::tcp::endpoint endpoint = created->LocalEndpoint();

    LoopbackListener moved = std::move(*created);
    CHECK(moved.LocalEndpoint() == endpoint);

    boost::asio::ip::tcp::socket client(ioc);
    boost::system::error_code connectEc;
    client.connect(endpoint, connectEc);
    REQUIRE_FALSE(connectEc);

    boost::system::error_code acceptEc;
    boost::asio::ip::tcp::socket accepted = moved.Acceptor().accept(acceptEc);
    REQUIRE_FALSE(acceptEc);
    CHECK(accepted.is_open());
}

TEST_CASE("an IPv4 and an IPv6 listener can coexist on the same io_context",
          "[transport][listener]") {
    boost::asio::io_context ioc;
    auto v4 = LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    auto v6 = LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV6, 0);

    REQUIRE(v4.has_value());
    REQUIRE(v6.has_value());
    CHECK(v4->LocalEndpoint().address().is_v4());
    CHECK(v6->LocalEndpoint().address().is_v6());
}

TEST_CASE("a real client can connect to the IPv4 listener and the server can "
          "accept it",
          "[transport][listener]") {
    boost::asio::io_context ioc;
    auto listener =
        LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());

    boost::asio::ip::tcp::socket client(ioc);
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
    boost::asio::io_context ioc;
    auto listener =
        LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV6, 0);
    REQUIRE(listener.has_value());

    boost::asio::ip::tcp::socket client(ioc);
    boost::system::error_code connectEc;
    client.connect(listener->LocalEndpoint(), connectEc);
    REQUIRE_FALSE(connectEc);

    boost::system::error_code acceptEc;
    boost::asio::ip::tcp::socket accepted = listener->Acceptor().accept(acceptEc);
    REQUIRE_FALSE(acceptEc);
    CHECK(accepted.is_open());
    CHECK(accepted.remote_endpoint().address().is_loopback());
}

TEST_CASE("Close makes a subsequent AcceptLoopbackOnly fail",
          "[transport][listener]") {
    boost::asio::io_context ioc;
    auto listener =
        LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());

    listener->Close();

    auto accepted = listener->AcceptLoopbackOnly();
    REQUIRE_FALSE(accepted.has_value());
    CHECK(accepted.error() == dovahlink::transport::AcceptError::kAcceptFailed);
}

TEST_CASE("Close is safe to call more than once",
          "[transport][listener]") {
    boost::asio::io_context ioc;
    auto listener =
        LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());

    listener->Close();
    listener->Close();

    auto accepted = listener->AcceptLoopbackOnly();
    CHECK_FALSE(accepted.has_value());
}

TEST_CASE("RunAcceptLoop accepts a client and Close cancels its next accept",
          "[transport][listener]") {
    using namespace std::chrono_literals;

    boost::asio::io_context listenerIoc;
    auto listener =
        LoopbackListener::Create(listenerIoc, LoopbackListener::IpVersion::kV4,
                                 0);
    REQUIRE(listener.has_value());

    std::promise<void> acceptedPromise;
    auto accepted = acceptedPromise.get_future();
    std::thread acceptThread([&listener, &acceptedPromise] {
        listener->RunAcceptLoop(
            [&acceptedPromise](auto result) {
                if (result.has_value()) {
                    acceptedPromise.set_value();
                }
                return true;
            });
    });

    boost::asio::io_context clientIoc;
    boost::asio::ip::tcp::socket client(clientIoc);
    boost::system::error_code connectEc;
    client.connect(listener->LocalEndpoint(), connectEc);
    REQUIRE_FALSE(connectEc);
    REQUIRE(accepted.wait_for(2s) == std::future_status::ready);

    auto closeFuture = std::async(std::launch::async, [&listener] {
        listener->Close();
    });
    REQUIRE(closeFuture.wait_for(2s) == std::future_status::ready);
    closeFuture.get();

    acceptThread.join();
    CHECK_FALSE(listener->Acceptor().is_open());
}

TEST_CASE("RunAcceptLoop does not start after Close",
          "[transport][listener]") {
    boost::asio::io_context ioc;
    auto listener =
        LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());

    listener->Close();
    bool handlerCalled = false;
    listener->RunAcceptLoop([&handlerCalled](auto) {
        handlerCalled = true;
        return false;
    });

    CHECK_FALSE(handlerCalled);
    CHECK_FALSE(listener->Acceptor().is_open());
}

TEST_CASE("RunAcceptLoop stops cleanly when its handler returns false",
          "[transport][listener]") {
    using namespace std::chrono_literals;

    boost::asio::io_context listenerIoc;
    auto listener =
        LoopbackListener::Create(listenerIoc, LoopbackListener::IpVersion::kV4,
                                 0);
    REQUIRE(listener.has_value());

    std::promise<bool> handledPromise;
    auto handled = handledPromise.get_future();
    std::thread acceptThread([&listener, &handledPromise] {
        listener->RunAcceptLoop([&handledPromise](auto result) {
            handledPromise.set_value(result.has_value());
            return false;
        });
    });

    boost::asio::io_context clientIoc;
    boost::asio::ip::tcp::socket client(clientIoc);
    boost::system::error_code connectEc;
    client.connect(listener->LocalEndpoint(), connectEc);
    REQUIRE_FALSE(connectEc);
    REQUIRE(handled.wait_for(2s) == std::future_status::ready);
    CHECK(handled.get());

    acceptThread.join();
    CHECK_FALSE(listener->Acceptor().is_open());
}

TEST_CASE("RunAcceptLoop propagates handler exceptions after stopping",
          "[transport][listener]") {
    using namespace std::chrono_literals;

    boost::asio::io_context listenerIoc;
    auto listener =
        LoopbackListener::Create(listenerIoc, LoopbackListener::IpVersion::kV4,
                                 0);
    REQUIRE(listener.has_value());

    std::promise<std::exception_ptr> failurePromise;
    auto failure = failurePromise.get_future();
    std::thread acceptThread([&listener, &failurePromise] {
        try {
            listener->RunAcceptLoop([](auto) -> bool {
                throw std::runtime_error("accept handler failed");
            });
        } catch (...) {
            failurePromise.set_value(std::current_exception());
        }
    });

    boost::asio::io_context clientIoc;
    boost::asio::ip::tcp::socket client(clientIoc);
    boost::system::error_code connectEc;
    client.connect(listener->LocalEndpoint(), connectEc);
    REQUIRE_FALSE(connectEc);
    REQUIRE(failure.wait_for(2s) == std::future_status::ready);

    acceptThread.join();
    CHECK_THROWS_AS(std::rethrow_exception(failure.get()),
                    std::runtime_error);
}

TEST_CASE("RunAcceptLoop accepts IPv6 and Close cancels its next accept",
          "[transport][listener]") {
    using namespace std::chrono_literals;

    boost::asio::io_context listenerIoc;
    auto listener =
        LoopbackListener::Create(listenerIoc, LoopbackListener::IpVersion::kV6,
                                 0);
    REQUIRE(listener.has_value());

    std::promise<void> acceptedPromise;
    auto accepted = acceptedPromise.get_future();
    std::thread acceptThread([&listener, &acceptedPromise] {
        listener->RunAcceptLoop(
            [&acceptedPromise](auto result) {
                if (result.has_value()) {
                    acceptedPromise.set_value();
                }
                return true;
            });
    });

    boost::asio::io_context clientIoc;
    boost::asio::ip::tcp::socket client(clientIoc);
    boost::system::error_code connectEc;
    client.connect(listener->LocalEndpoint(), connectEc);
    REQUIRE_FALSE(connectEc);
    REQUIRE(accepted.wait_for(2s) == std::future_status::ready);

    auto firstClose = std::async(std::launch::async, [&listener] {
        listener->Close();
    });
    auto secondClose = std::async(std::launch::async, [&listener] {
        listener->Close();
    });
    REQUIRE(firstClose.wait_for(2s) == std::future_status::ready);
    REQUIRE(secondClose.wait_for(2s) == std::future_status::ready);
    firstClose.get();
    secondClose.get();

    acceptThread.join();
    CHECK_FALSE(listener->Acceptor().is_open());
}
