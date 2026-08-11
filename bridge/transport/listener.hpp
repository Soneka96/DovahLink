#pragma once

#include <boost/asio/io_context.hpp>
#include <boost/asio/ip/tcp.hpp>

#include <cstdint>
#include <expected>

namespace dovahlink::transport {

enum class ListenerError {
    kBindFailed,
};

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
