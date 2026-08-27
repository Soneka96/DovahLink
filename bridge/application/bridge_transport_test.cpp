#include "application/bridge_transport.hpp"

#include "transport/loopback_test_support.hpp"

#include <catch2/catch_test_macros.hpp>
#include <gmock/gmock.h>

#include <boost/asio/ip/tcp.hpp>
#include <boost/system/error_code.hpp>

using dovahlink::application::BridgeTransport;
using dovahlink::transport::LoopbackListener;
using dovahlink::transport::test_support::MockLoopbackListener;
using testing::StrictMock;

TEST_CASE(
    "BridgeTransport::Close closes both listeners so a subsequent accept fails",
    "[application][bridge_transport]") {
    auto listenerV4 =
        LoopbackListener::Create(LoopbackListener::IpVersion::kV4, 0);
    auto listenerV6 =
        LoopbackListener::Create(LoopbackListener::IpVersion::kV6, 0);
    REQUIRE(listenerV4.has_value());
    REQUIRE(listenerV6.has_value());

    BridgeTransport transport(*listenerV4, *listenerV6);
    transport.Start();
    transport.CancelCompletions();
    transport.Close();
    //  BridgeTransport::Close() only requests closure (matching
    //  ILoopbackListener::Close()'s non-blocking contract); Join() is the
    //  barrier that actually waits for the acceptor to close, normally
    //  provided by BridgeWorkerPool::Join() running before this adapter's
    //  Close() in the real shutdown order.
    listenerV4->Join();
    listenerV6->Join();

    CHECK_FALSE(listenerV4->Acceptor().is_open());
    CHECK_FALSE(listenerV6->Acceptor().is_open());
}

TEST_CASE("BridgeTransport::Close is safe to call after the acceptors are "
          "already closed",
          "[application][bridge_transport]") {
    auto listenerV4 =
        LoopbackListener::Create(LoopbackListener::IpVersion::kV4, 0);
    auto listenerV6 =
        LoopbackListener::Create(LoopbackListener::IpVersion::kV6, 0);
    REQUIRE(listenerV4.has_value());
    REQUIRE(listenerV6.has_value());

    boost::system::error_code ec;
    listenerV4->Acceptor().close(ec);
    listenerV6->Acceptor().close(ec);

    BridgeTransport transport(*listenerV4, *listenerV6);
    transport.Close(); //  must not throw or crash on an already-closed acceptor.
}

TEST_CASE("BridgeTransport::Close closes both listeners through the contract",
          "[application][bridge_transport]") {
    StrictMock<MockLoopbackListener> listenerV4;
    StrictMock<MockLoopbackListener> listenerV6;
    EXPECT_CALL(listenerV4, Close()).Times(1);
    EXPECT_CALL(listenerV6, Close()).Times(1);

    BridgeTransport transport(listenerV4, listenerV6);
    transport.Close();
}

TEST_CASE("BridgeTransport::Start and CancelCompletions never touch the "
          "listeners",
          "[application][bridge_transport]") {
    StrictMock<MockLoopbackListener> listenerV4;
    StrictMock<MockLoopbackListener> listenerV6;
    EXPECT_CALL(listenerV4, Close()).Times(0);
    EXPECT_CALL(listenerV6, Close()).Times(0);

    BridgeTransport transport(listenerV4, listenerV6);
    transport.Start();
    transport.CancelCompletions();
}
