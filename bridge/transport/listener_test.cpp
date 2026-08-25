#include "transport/listener.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/asio/io_context.hpp>
#include <boost/asio/ip/address.hpp>
#include <boost/asio/ip/tcp.hpp>
#include <boost/system/error_code.hpp>

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
