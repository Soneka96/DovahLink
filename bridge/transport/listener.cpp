#include "transport/listener.hpp"

#include <boost/asio/ip/address.hpp>
#include <boost/asio/socket_base.hpp>
#include <boost/system/error_code.hpp>

#include <utility>

namespace dovahlink::transport {

bool IsAcceptablePeerAddress(const boost::asio::ip::address& address) {
    return address.is_loopback();
}

std::expected<LoopbackListener, ListenerError> LoopbackListener::Create(boost::asio::io_context& ioc,
                                                                          IpVersion version,
                                                                          std::uint16_t port) {
    boost::system::error_code ec;

    const char* loopbackAddress = (version == IpVersion::kV4) ? "127.0.0.1" : "::1";
    boost::asio::ip::address address = boost::asio::ip::make_address(loopbackAddress, ec);
    if (ec) {
        return std::unexpected(ListenerError::kBindFailed);
    }

    boost::asio::ip::tcp::endpoint endpoint(address, port);
    boost::asio::ip::tcp::acceptor acceptor(ioc);

    acceptor.open(endpoint.protocol(), ec);
    if (ec) {
        return std::unexpected(ListenerError::kBindFailed);
    }

    // Allows rebinding a port still lingering in TIME_WAIT from a previous
    // listener (e.g. across quick restarts during development). This does
    // not let two sockets listen on the same port simultaneously -- that
    // would need SO_REUSEPORT, a different option -- so it does not weaken
    // the one-connected-client or loopback-only guarantees.
    acceptor.set_option(boost::asio::socket_base::reuse_address(true), ec);
    if (ec) {
        return std::unexpected(ListenerError::kBindFailed);
    }

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

LoopbackListener::LoopbackListener(boost::asio::ip::tcp::acceptor acceptor) : acceptor_(std::move(acceptor)) {}

boost::asio::ip::tcp::acceptor& LoopbackListener::Acceptor() {
    return acceptor_;
}

std::expected<boost::asio::ip::tcp::socket, AcceptError> LoopbackListener::AcceptLoopbackOnly() {
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

}  // namespace dovahlink::transport
