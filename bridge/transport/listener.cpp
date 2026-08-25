#include "transport/listener.hpp"

#include <boost/asio/ip/address.hpp>
#include <boost/asio/socket_base.hpp>
#include <boost/system/error_code.hpp>

#include <utility>

namespace dovahlink::transport {

bool IsAcceptablePeerAddress(const boost::asio::ip::address &address) {
  return address.is_loopback();
}

std::expected<LoopbackListener, ListenerError>
LoopbackListener::Create(boost::asio::io_context &ioc, IpVersion version,
                         std::uint16_t port) {
  boost::system::error_code ec;

  const char *loopbackAddress =
      (version == IpVersion::kV4) ? "127.0.0.1" : "::1";
  boost::asio::ip::address address =
      boost::asio::ip::make_address(loopbackAddress, ec);
  if (ec) {
    return std::unexpected(ListenerError::kBindFailed);
  }

  boost::asio::ip::tcp::endpoint endpoint(address, port);
  boost::asio::ip::tcp::acceptor acceptor(ioc);

  acceptor.open(endpoint.protocol(), ec);
  if (ec) {
    return std::unexpected(ListenerError::kBindFailed);
  }

  // SO_REUSEADDR is deliberately NOT set here. On POSIX it would only
  // permit rebinding a port still lingering in TIME_WAIT; on Windows it
  // has much looser semantics and can let a second socket bind to a port
  // an existing socket is actively listening on, silently defeating the
  // "one listener per port" guarantee this bridge relies on (confirmed by
  // an actual failing test: a second LoopbackListener::Create on an
  // already-bound port succeeded instead of returning kBindFailed, until
  // this option was removed). Windows' actual TIME_WAIT-only equivalent
  // is SO_EXCLUSIVEADDRUSE, not needed here since binding without
  // SO_REUSEADDR already fails closed the way this bridge needs; a quick
  // dev-loop restart hitting TIME_WAIT is a minor, rare inconvenience,
  // not a correctness requirement.
  acceptor.bind(endpoint, ec);
  if (ec) {
    return std::unexpected(ListenerError::kBindFailed);
  }

  acceptor.listen(boost::asio::socket_base::max_listen_connections, ec);
  if (ec) {
    return std::unexpected(ListenerError::kBindFailed);
  }

  return LoopbackListener(std::move(acceptor));
}

LoopbackListener::LoopbackListener(boost::asio::ip::tcp::acceptor acceptor)
    : acceptor_(std::move(acceptor)) {}

boost::asio::ip::tcp::acceptor &LoopbackListener::Acceptor() {
  return acceptor_;
}

std::expected<boost::asio::ip::tcp::socket, AcceptError>
LoopbackListener::AcceptLoopbackOnly() {
  boost::system::error_code acceptEc;
  boost::asio::ip::tcp::socket socket = acceptor_.accept(acceptEc);
  if (acceptEc) {
    return std::unexpected(AcceptError::kAcceptFailed);
  }

  boost::system::error_code remoteEc;
  boost::asio::ip::tcp::endpoint remote = socket.remote_endpoint(remoteEc);
  if (remoteEc || !IsAcceptablePeerAddress(remote.address())) {
    boost::system::error_code closeEc;
    socket.close(closeEc);
    return std::unexpected(AcceptError::kNonLoopbackPeerRejected);
  }

  return socket;
}

boost::asio::ip::tcp::endpoint LoopbackListener::LocalEndpoint() const {
  return acceptor_.local_endpoint();
}

} // namespace dovahlink::transport
