#include "application/active_session_socket.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/asio/io_context.hpp>
#include <boost/asio/ip/tcp.hpp>

#include <atomic>
#include <barrier>
#include <thread>

using dovahlink::application::ActiveSessionSocket;
using dovahlink::transport::WebSocketSession;

TEST_CASE("ActiveSessionSocket captures a published connection and socket pair",
          "[application][active_session_socket]") {
    boost::asio::io_context ioContext;
    auto socket = WebSocketSession::CreateSocket(
        boost::asio::ip::tcp::socket(ioContext));
    ActiveSessionSocket registry;

    registry.Publish(7, socket);

    const auto snapshot = registry.Capture();
    REQUIRE(snapshot.has_value());
    CHECK(snapshot->connection == 7);
    CHECK(snapshot->socket == socket);
}

TEST_CASE("ActiveSessionSocket Shutdown is a no-op when nothing was published",
          "[application][active_session_socket]") {
    ActiveSessionSocket registry;

    registry.Shutdown();

    CHECK_FALSE(registry.Capture().has_value());
}

TEST_CASE(
    "ActiveSessionSocket Shutdown forwards to the published socket without "
    "clearing it",
    "[application][active_session_socket]") {
    boost::asio::io_context ioContext;
    auto socket = WebSocketSession::CreateSocket(
        boost::asio::ip::tcp::socket(ioContext));
    ActiveSessionSocket registry;
    registry.Publish(7, socket);

    registry.Shutdown();

    //  Shutdown only forwards to the captured socket's own Shutdown(); it does
    //  not unpublish the registry entry itself.
    const auto snapshot = registry.Capture();
    REQUIRE(snapshot.has_value());
    CHECK(snapshot->connection == 7);
    CHECK(snapshot->socket == socket);

    //  A second call must not crash: Socket::Shutdown() owns its own
    //  single-fire guard, so ActiveSessionSocket must stay safe calling it
    //  repeatedly.
    registry.Shutdown();
}

TEST_CASE(
    "ActiveSessionSocket Shutdown is safe when the published socket has "
    "already expired",
    "[application][active_session_socket]") {
    boost::asio::io_context ioContext;
    ActiveSessionSocket registry;
    {
        auto socket = WebSocketSession::CreateSocket(
            boost::asio::ip::tcp::socket(ioContext));
        registry.Publish(7, socket);
    } //  socket destroyed here; the registry only ever held a weak_ptr.

    registry.Shutdown();

    CHECK_FALSE(registry.Capture().has_value());
}

TEST_CASE("ActiveSessionSocket never separates a connection from its socket",
          "[application][active_session_socket]") {
    boost::asio::io_context ioContext;
    auto firstSocket = WebSocketSession::CreateSocket(
        boost::asio::ip::tcp::socket(ioContext));
    auto secondSocket = WebSocketSession::CreateSocket(
        boost::asio::ip::tcp::socket(ioContext));
    ActiveSessionSocket registry;
    std::barrier startBarrier(2);
    std::atomic<bool> running = true;
    std::atomic<bool> coherent = true;

    std::thread writer([&] {
        startBarrier.arrive_and_wait();
        for (int index = 0; index < 10000; ++index) {
            if ((index % 2) == 0) {
                registry.Publish(1, firstSocket);
            } else {
                registry.Publish(2, secondSocket);
            }
        }
        running.store(false, std::memory_order_release);
    });
    std::thread reader([&] {
        startBarrier.arrive_and_wait();
        while (running.load(std::memory_order_acquire)) {
            const auto snapshot = registry.Capture();
            if (!snapshot.has_value()) {
                continue;
            }
            const bool isFirstPair = snapshot->connection == 1 &&
                                     snapshot->socket == firstSocket;
            const bool isSecondPair = snapshot->connection == 2 &&
                                      snapshot->socket == secondSocket;
            if (!isFirstPair && !isSecondPair) {
                coherent.store(false, std::memory_order_release);
                break;
            }
        }
    });

    writer.join();
    reader.join();

    CHECK(coherent.load(std::memory_order_acquire));
}
