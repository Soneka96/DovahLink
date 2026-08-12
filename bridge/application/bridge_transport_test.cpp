#include "application/bridge_transport.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/asio/io_context.hpp>
#include <boost/asio/ip/tcp.hpp>
#include <boost/system/error_code.hpp>

using dovahlink::application::BridgeTransport;
using dovahlink::transport::LoopbackListener;

TEST_CASE("BridgeTransport::Close closes both listeners so a subsequent accept fails",
          "[application][bridge_transport]") {
    boost::asio::io_context ioc;
    auto listenerV4 = LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    auto listenerV6 = LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV6, 0);
    REQUIRE(listenerV4.has_value());
    REQUIRE(listenerV6.has_value());

    BridgeTransport transport(*listenerV4, *listenerV6);
    transport.Start();
    transport.CancelCompletions();
    transport.Close();

    auto acceptedV4 = listenerV4->AcceptLoopbackOnly();
    auto acceptedV6 = listenerV6->AcceptLoopbackOnly();
    CHECK_FALSE(acceptedV4.has_value());
    CHECK_FALSE(acceptedV6.has_value());
}

TEST_CASE("BridgeTransport::Close is safe to call after the acceptors are already closed",
          "[application][bridge_transport]") {
    boost::asio::io_context ioc;
    auto listenerV4 = LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV4, 0);
    auto listenerV6 = LoopbackListener::Create(ioc, LoopbackListener::IpVersion::kV6, 0);
    REQUIRE(listenerV4.has_value());
    REQUIRE(listenerV6.has_value());

    boost::system::error_code ec;
    listenerV4->Acceptor().close(ec);
    listenerV6->Acceptor().close(ec);

    BridgeTransport transport(*listenerV4, *listenerV6);
    transport.Close();  // must not throw or crash on an already-closed acceptor.
}
