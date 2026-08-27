#pragma once

#include "shared/enums.hpp"

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

    ///  Requests that the underlying acceptor close and returns without
    ///  waiting for that to complete; repeated calls are harmless. Any
    ///  in-flight `AcceptLoopbackOnly()` call unblocks with a failure once the
    ///  request is serviced.
    virtual void Close() noexcept = 0;

    ///  Blocks until a previously requested close has actually completed;
    ///  repeated calls are harmless. Implicitly requests a close first if
    ///  `Close()` was never called. The caller must ensure no
    ///  `AcceptLoopbackOnly()` call can still be starting when this is
    ///  called -- for example by joining whatever thread calls
    ///  `AcceptLoopbackOnly()` before calling this -- since a call arriving
    ///  after `Join()` has stopped the owner thread would never complete.
    virtual void Join() = 0;
};

///  Owns a TCP acceptor permanently bound to an IPv4 or IPv6 loopback address,
///  together with the private `io_context` and background thread that drive
///  its asynchronous accept lifecycle for the object's whole life.
class LoopbackListener final : public ILoopbackListener {
  public:
    ///  Selects the loopback address family used by a listener.
    enum class IpVersion {
        ///  Binds `127.0.0.1`.
        kV4,
        ///  Binds `::1`.
        kV6,
    };

    ///  Opens, binds, and listens on the selected loopback address and port,
    ///  then starts the owner thread that drives its asynchronous accepts.
    static std::expected<LoopbackListener, ListenerError>
    Create(IpVersion version, std::uint16_t port);

    ///  Transfers listener ownership from another instance.
    LoopbackListener(LoopbackListener&&) = default;
    ///  Transfers listener ownership from another instance.
    LoopbackListener& operator=(LoopbackListener&&) = default;
    ///  Prevents copying the underlying acceptor.
    LoopbackListener(const LoopbackListener&) = delete;
    ///  Prevents copying the underlying acceptor.
    LoopbackListener& operator=(const LoopbackListener&) = delete;

    ///  Defensively joins the owner thread so destruction can never race it,
    ///  even if the caller never called `Join()`.
    ~LoopbackListener() override;

    ///  Returns mutable access to the underlying acceptor. Bypasses the owner
    ///  thread: safe only for direct, synchronous acceptor use (as several
    ///  tests do) sequenced so it never overlaps an `AcceptLoopbackOnly()` or
    ///  `Close()` call on the same instance.
    [[nodiscard]] boost::asio::ip::tcp::acceptor& Acceptor();

    ///  @copydoc ILoopbackListener::AcceptLoopbackOnly
    [[nodiscard]] std::expected<boost::asio::ip::tcp::socket, AcceptError>
    AcceptLoopbackOnly() override;

    ///  @copydoc ILoopbackListener::Close
    void Close() noexcept override;

    ///  @copydoc ILoopbackListener::Join
    void Join() override;

    ///  Returns the endpoint actually bound by the listener.
    ///  The endpoint is always loopback; the underlying accessor may throw if
    ///  the acceptor is not open, which cannot occur after successful `Create`.
    [[nodiscard]] boost::asio::ip::tcp::endpoint LocalEndpoint() const;

  private:
    ///  Owns the acceptor, its `io_context`, and the owner thread; see
    ///  `loopback_listener_state.hpp`.
    struct OwnerState;

    ///  Takes ownership of an already-listening acceptor's owner state.
    explicit LoopbackListener(std::shared_ptr<OwnerState> state);

    ///  Runs `state`'s `io_context` for the owner thread's whole life. A
    ///  handler exception must never escape a worker-thread entry point;
    ///  there is no further recovery once that happens, so this only stops
    ///  the thread.
    static void RunOwnerThread(std::shared_ptr<OwnerState> state) noexcept;

    ///  Private asynchronous resources; see `OwnerState`.
    std::shared_ptr<OwnerState> state_;
};

} //  namespace dovahlink::transport
