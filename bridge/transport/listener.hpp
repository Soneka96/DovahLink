#pragma once

#include <boost/asio/io_context.hpp>
#include <boost/asio/ip/address.hpp>
#include <boost/asio/ip/tcp.hpp>

#include <cstdint>
#include <expected>

namespace dovahlink::transport {

enum class ListenerError {
    kBindFailed,
};

enum class AcceptError {
    kAcceptFailed,
    // The connection was closed because its remote address could not be
    // confirmed as loopback -- including the case where the remote address
    // could not be determined at all, which fails closed the same way.
    kNonLoopbackPeerRejected,
};

// True if `address` is an acceptable peer address for the Phase 1 loopback
// proof. See ai/context/protocol/security.md: "The bridge must reject
// connections whose remote address is not loopback during this phase." This
// is checked explicitly by AcceptLoopbackOnly as defense in depth beyond the
// listener only ever binding to loopback addresses (see class docs below);
// it is a free function so it can be tested directly against synthetic
// addresses without needing an actual non-loopback connection to originate.
[[nodiscard]] bool IsAcceptablePeerAddress(const boost::asio::ip::address& address);

// A TCP acceptor bound to loopback only, per ai/context/protocol/security.md:
// "Keep the listening address permanently restricted to 127.0.0.1 and ::1
// during Phase 1... Allow only a documented loopback port setting; it must
// never alter the listening address."
//
// There is deliberately no way to construct this bound to any other
// address: Create takes only an IP version selector (which loopback
// address, v4 or v6) and a port. No raw address string or endpoint is
// accepted anywhere in this API, so a non-loopback bind is not just
// rejected at runtime, it has no code path to reach at all.
class LoopbackListener {
public:
    enum class IpVersion {
        kV4,  // binds 127.0.0.1
        kV6,  // binds ::1
    };

    // Opens, binds, and starts listening on `port` for the selected loopback
    // address. Returns ListenerError::kBindFailed if any step fails (for
    // example, the port is already in use).
    static std::expected<LoopbackListener, ListenerError> Create(boost::asio::io_context& ioc,
                                                                   IpVersion version,
                                                                   std::uint16_t port);

    LoopbackListener(LoopbackListener&&) = default;
    LoopbackListener& operator=(LoopbackListener&&) = default;
    LoopbackListener(const LoopbackListener&) = delete;
    LoopbackListener& operator=(const LoopbackListener&) = delete;

    // The acceptor, for use by whatever drives the accept loop (a later step).
    [[nodiscard]] boost::asio::ip::tcp::acceptor& Acceptor();

    // Accepts one connection and validates its remote address with
    // IsAcceptablePeerAddress, closing and rejecting it otherwise rather
    // than returning a socket for a peer that should never have been able
    // to reach this listener in the first place.
    [[nodiscard]] std::expected<boost::asio::ip::tcp::socket, AcceptError> AcceptLoopbackOnly();

    // The endpoint actually bound: always 127.0.0.1 or ::1 at the requested
    // port, never anything else (see class docs). Throws boost::system::
    // system_error in principle (it calls the throwing acceptor accessor),
    // but that is unreachable through this type's public API: the only way
    // to obtain a LoopbackListener is a successful Create, which guarantees
    // the acceptor is already open and bound.
    [[nodiscard]] boost::asio::ip::tcp::endpoint LocalEndpoint() const;

private:
    explicit LoopbackListener(boost::asio::ip::tcp::acceptor acceptor);

    boost::asio::ip::tcp::acceptor acceptor_;
};

}  // namespace dovahlink::transport
