//  A genuinely non-loopback peer cannot be originated in this test suite
//  without raw sockets and routing infrastructure. The pure address predicate
//  is therefore tested with synthetic addresses, while accepted loopback peers
//  are still verified through real sockets.

#include "transport/loopback_listener.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/asio/io_context.hpp>
#include <boost/asio/ip/address.hpp>
#include <boost/asio/ip/tcp.hpp>
#include <boost/system/error_code.hpp>

#include <chrono>
#include <expected>
#include <future>
#include <string_view>
#include <thread>

using dovahlink::transport::AcceptError;
using dovahlink::transport::IsAcceptablePeerAddress;
using dovahlink::transport::LoopbackListener;

namespace {

///  Runs the asynchronous listener against one real loopback peer and checks
///  the accepted socket.
void CheckRealLoopbackPeer(LoopbackListener::IpVersion version,
                           std::string_view clientAddress) {
    using namespace std::chrono_literals;
    using AcceptResult =
        std::expected<boost::asio::ip::tcp::socket, AcceptError>;

    boost::asio::io_context listenerIoc;
    auto listener = LoopbackListener::Create(listenerIoc, version, 0);
    REQUIRE(listener.has_value());
    boost::asio::ip::tcp::endpoint endpoint = listener->LocalEndpoint();

    std::promise<AcceptResult> acceptedPromise;
    auto acceptedFuture = acceptedPromise.get_future();
    std::thread acceptThread([&listener, &acceptedPromise] {
        acceptedPromise.set_value(listener->AcceptLoopbackOnly());
    });

    boost::asio::io_context clientIoc;
    boost::asio::ip::tcp::socket client(clientIoc);
    boost::system::error_code connectEc;
    client.connect(endpoint, connectEc);
    if (connectEc) {
        listener->Close();
        acceptThread.join();
        FAIL("loopback client connection failed");
    }
    if (acceptedFuture.wait_for(2s) != std::future_status::ready) {
        listener->Close();
        acceptThread.join();
        FAIL("loopback accept did not complete");
    }

    auto accepted = acceptedFuture.get();
    acceptThread.join();
    REQUIRE(accepted.has_value());
    CHECK(accepted->is_open());
    CHECK(accepted->remote_endpoint().address() ==
          boost::asio::ip::make_address(clientAddress));
}

} //  namespace

TEST_CASE("IsAcceptablePeerAddress accepts 127.0.0.1",
          "[transport][loopback_peer]") {
    CHECK(IsAcceptablePeerAddress(boost::asio::ip::make_address("127.0.0.1")));
}

TEST_CASE("IsAcceptablePeerAddress accepts ::1", "[transport][loopback_peer]") {
    CHECK(IsAcceptablePeerAddress(boost::asio::ip::make_address("::1")));
}

TEST_CASE("IsAcceptablePeerAddress rejects a non-loopback IPv4 address",
          "[transport][loopback_peer]") {
    CHECK_FALSE(
        IsAcceptablePeerAddress(boost::asio::ip::make_address("192.168.1.1")));
}

TEST_CASE("IsAcceptablePeerAddress rejects a non-loopback IPv6 address",
          "[transport][loopback_peer]") {
    //  2001:db8::/32 is IANA-reserved for documentation and never routable.
    CHECK_FALSE(
        IsAcceptablePeerAddress(boost::asio::ip::make_address("2001:db8::1")));
}

TEST_CASE("IsAcceptablePeerAddress rejects the IPv4 unspecified address",
          "[transport][loopback_peer]") {
    CHECK_FALSE(
        IsAcceptablePeerAddress(boost::asio::ip::make_address("0.0.0.0")));
}

TEST_CASE(
    "IsAcceptablePeerAddress rejects the IPv4-mapped IPv6 loopback address",
    "[transport][loopback_peer]") {
    //  Verified directly against the vendored Boost.Asio source
    //  (address_v6::is_loopback, boost/asio/ip/impl/address_v6.ipp): it only
    //  matches the literal 16 bytes of ::1, with no special case for
    //  IPv4-mapped addresses -- a prior version of this test assumed
    //  otherwise and was wrong, caught by actually running it. This
    //  representation is also unreachable in practice here regardless:
    //  LoopbackListener always binds the specific address 127.0.0.1 or ::1,
    //  never a dual-stack wildcard socket, so remote_endpoint() cannot
    //  observe an IPv4-mapped peer address from a real connection to this
    //  bridge.
    CHECK_FALSE(IsAcceptablePeerAddress(
        boost::asio::ip::make_address("::ffff:127.0.0.1")));
}

TEST_CASE(
    "AcceptLoopbackOnly returns a valid socket for a real IPv4 loopback peer",
    "[transport][loopback_peer]") {
    CheckRealLoopbackPeer(LoopbackListener::IpVersion::kV4, "127.0.0.1");
}

TEST_CASE(
    "AcceptLoopbackOnly returns a valid socket for a real IPv6 loopback peer",
    "[transport][loopback_peer]") {
    CheckRealLoopbackPeer(LoopbackListener::IpVersion::kV6, "::1");
}

TEST_CASE(
    "AcceptLoopbackOnly exits when the acceptor is already closed",
    "[transport][loopback_peer]") {
    boost::asio::io_context ioc;
    auto listener =
        LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    REQUIRE(listener.has_value());

    boost::system::error_code closeEc;
    listener->Acceptor().close(closeEc);
    REQUIRE_FALSE(closeEc);

    auto accepted = listener->AcceptLoopbackOnly();
    CHECK_FALSE(accepted.has_value());
}
