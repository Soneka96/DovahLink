#pragma once

#include "shared/enums.hpp"

#include <boost/asio/io_context.hpp>
#include <boost/asio/ip/address.hpp>
#include <boost/asio/ip/tcp.hpp>

#include <cstdint>
#include <expected>

namespace dovahlink::transport {

/// Returns whether an address is allowed for the loopback-only listener.
[[nodiscard]] bool
IsAcceptablePeerAddress(const boost::asio::ip::address &address);

/// Owns a TCP acceptor permanently bound to an IPv4 or IPv6 loopback address.
class LoopbackListener {
public:
  /// Selects the loopback address family used by a listener.
  enum class IpVersion {
    /// Binds `127.0.0.1`.
    kV4,
    /// Binds `::1`.
    kV6,
  };

  /// Opens, binds, and listens on the selected loopback address and port.
  static std::expected<LoopbackListener, ListenerError>
  Create(boost::asio::io_context &ioc, IpVersion version, std::uint16_t port);

  /// Transfers listener ownership from another instance.
  LoopbackListener(LoopbackListener &&) = default;
  /// Transfers listener ownership from another instance.
  LoopbackListener &operator=(LoopbackListener &&) = default;
  /// Prevents copying the underlying acceptor.
  LoopbackListener(const LoopbackListener &) = delete;
  /// Prevents copying the underlying acceptor.
  LoopbackListener &operator=(const LoopbackListener &) = delete;

  /// Returns mutable access to the underlying acceptor.
  [[nodiscard]] boost::asio::ip::tcp::acceptor &Acceptor();

  /// Accepts one socket and rejects peers that are not loopback addresses.
  [[nodiscard]] std::expected<boost::asio::ip::tcp::socket, AcceptError>
  AcceptLoopbackOnly();

  /// Returns the endpoint actually bound by the listener.
  /// The endpoint is always loopback; the underlying accessor may throw if
  /// the acceptor is not open, which cannot occur after successful `Create`.
  [[nodiscard]] boost::asio::ip::tcp::endpoint LocalEndpoint() const;

private:
  /// Stores an already-open TCP acceptor.
  explicit LoopbackListener(boost::asio::ip::tcp::acceptor acceptor);

  /// Owned TCP acceptor bound to the loopback endpoint.
  boost::asio::ip::tcp::acceptor acceptor_;
};

} // namespace dovahlink::transport
