#pragma once

#include "shared/enums.hpp"

#include <boost/asio/io_context.hpp>
#include <boost/asio/ip/address.hpp>
#include <boost/asio/ip/tcp.hpp>

#include <cstdint>
#include <expected>
#include <memory>

namespace dovahlink::transport {

///  Returns whether an address is allowed for the loopback-only listener.
[[nodiscard]] bool
IsAcceptablePeerAddress(const boost::asio::ip::address& address);

///  Performs serialized loopback-only acceptance and closes the underlying
///  acceptor.
class ILoopbackListener {
  public:
    ///  Releases the interface without performing work.
    virtual ~ILoopbackListener() = default;

    ///  Accepts one socket asynchronously and returns when that operation settles.
    [[nodiscard]] virtual std::expected<boost::asio::ip::tcp::socket, AcceptError>
    AcceptLoopbackOnly() = 0;

    ///  Closes the underlying acceptor; repeated calls are harmless.
    virtual void Close() noexcept = 0;
};

///  Owns a TCP acceptor permanently bound to an IPv4 or IPv6 loopback address.
class LoopbackListener final : public ILoopbackListener {
  public:
    ///  Selects the loopback address family used by a listener.
    enum class IpVersion {
        ///  Binds `127.0.0.1`.
        kV4,
        ///  Binds `::1`.
        kV6,
    };

    ///  Opens, binds, and listens on the selected loopback address and port.
    static std::expected<LoopbackListener, ListenerError>
    Create(boost::asio::io_context& ioc, IpVersion version, std::uint16_t port);

    ///  Transfers listener ownership from another instance.
    LoopbackListener(LoopbackListener&&) = default;
    ///  Transfers listener ownership from another instance.
    LoopbackListener& operator=(LoopbackListener&&) = default;
    ///  Prevents copying the underlying acceptor.
    LoopbackListener(const LoopbackListener&) = delete;
    ///  Prevents copying the underlying acceptor.
    LoopbackListener& operator=(const LoopbackListener&) = delete;

    ///  Returns mutable access to the underlying acceptor.
    [[nodiscard]] boost::asio::ip::tcp::acceptor& Acceptor();

    ///  @copydoc ILoopbackListener::AcceptLoopbackOnly
    [[nodiscard]] std::expected<boost::asio::ip::tcp::socket, AcceptError>
    AcceptLoopbackOnly() override;

    ///  @copydoc ILoopbackListener::Close
    void Close() noexcept override;

    ///  Returns the endpoint actually bound by the listener.
    ///  The endpoint is always loopback; the underlying accessor may throw if
    ///  the acceptor is not open, which cannot occur after successful `Create`.
    [[nodiscard]] boost::asio::ip::tcp::endpoint LocalEndpoint() const;

  private:
    ///  Tracks asynchronous accept-loop state shared with Close().
    struct LifecycleState;

    ///  Stores an already-open TCP acceptor.
    LoopbackListener(boost::asio::io_context& ioc,
                     boost::asio::ip::tcp::acceptor acceptor);

    ///  I/O context that owns the acceptor's executor.
    boost::asio::io_context* ioContext_;
    ///  Owned TCP acceptor bound to the loopback endpoint.
    boost::asio::ip::tcp::acceptor acceptor_;
    ///  Synchronizes the accept loop with Close().
    std::shared_ptr<LifecycleState> lifecycle_;
};

} //  namespace dovahlink::transport
