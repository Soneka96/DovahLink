#include "transport/loopback_listener.hpp"
#include "transport/loopback_listener_state.hpp"

#include <boost/asio/error.hpp>
#include <boost/asio/ip/address.hpp>
#include <boost/asio/socket_base.hpp>
#include <boost/system/error_code.hpp>

#include <condition_variable>
#include <exception>
#include <functional>
#include <memory>
#include <mutex>
#include <utility>

namespace dovahlink::transport {

bool IsAcceptablePeerAddress(const boost::asio::ip::address& address) {
    return address.is_loopback();
}

std::expected<LoopbackListener, ListenerError>
LoopbackListener::Create(boost::asio::io_context& ioc, IpVersion version,
                         std::uint16_t port) {
    boost::system::error_code ec;

    const char* loopbackAddress =
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

    //  SO_REUSEADDR is deliberately NOT set here. On POSIX it would only
    //  permit rebinding a port still lingering in TIME_WAIT; on Windows it
    //  has much looser semantics and can let a second socket bind to a port
    //  an existing socket is actively listening on, silently defeating the
    //  "one listener per port" guarantee this bridge relies on (confirmed by
    //  an actual failing test: a second LoopbackListener::Create on an
    //  already-bound port succeeded instead of returning kBindFailed, until
    //  this option was removed). Windows' actual TIME_WAIT-only equivalent
    //  is SO_EXCLUSIVEADDRUSE, not needed here since binding without
    //  SO_REUSEADDR already fails closed the way this bridge needs; a quick
    //  dev-loop restart hitting TIME_WAIT is a minor, rare inconvenience,
    //  not a correctness requirement.
    acceptor.bind(endpoint, ec);
    if (ec) {
        return std::unexpected(ListenerError::kBindFailed);
    }

    acceptor.listen(boost::asio::socket_base::max_listen_connections, ec);
    if (ec) {
        return std::unexpected(ListenerError::kBindFailed);
    }

    return LoopbackListener(ioc, std::move(acceptor));
}

LoopbackListener::LoopbackListener(
    boost::asio::io_context& ioc, boost::asio::ip::tcp::acceptor acceptor)
    : ioContext_(&ioc), acceptor_(std::move(acceptor)),
      lifecycle_(std::make_shared<LifecycleState>()) {}

boost::asio::ip::tcp::acceptor& LoopbackListener::Acceptor() {
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

void LoopbackListener::RunAcceptLoop(AcceptHandler handler) {
    const auto lifecycle = lifecycle_;
    {
        std::lock_guard lock(lifecycle->mutex);
        if (lifecycle->started || lifecycle->closeRequested) {
            return;
        }
        lifecycle->started = true;
        lifecycle->running = true;
    }

    using WorkGuard = boost::asio::executor_work_guard<
        boost::asio::ip::tcp::acceptor::executor_type>;
    std::shared_ptr<WorkGuard> workGuard;
    std::exception_ptr failure;

    try {
        workGuard = std::make_shared<WorkGuard>(
            boost::asio::make_work_guard(acceptor_.get_executor()));
        auto acceptNext = std::make_shared<std::function<void()>>();

        *acceptNext = [this, lifecycle, handler = std::move(handler), acceptNext,
                       workGuard]() mutable {
            {
                std::lock_guard lock(lifecycle->mutex);
                if (lifecycle->closeRequested) {
                    workGuard->reset();
                    return;
                }
            }

            auto socket =
                std::make_shared<boost::asio::ip::tcp::socket>(*ioContext_);
            acceptor_.async_accept(
                *socket,
                [this, lifecycle, handler, acceptNext, workGuard,
                 socket](boost::system::error_code ec) mutable {
                    auto stop = [this, workGuard] {
                        boost::system::error_code ignored;
                        acceptor_.close(ignored);
                        workGuard->reset();
                    };

                    try {
                        {
                            std::lock_guard lock(lifecycle->mutex);
                            if (lifecycle->closeRequested) {
                                workGuard->reset();
                                return;
                            }
                        }

                        if (ec) {
                            if (ec == boost::asio::error::operation_aborted) {
                                workGuard->reset();
                                return;
                            }
                            if (!handler(
                                    std::unexpected(AcceptError::kAcceptFailed))) {
                                stop();
                                return;
                            }
                            (*acceptNext)();
                            return;
                        }

                        boost::system::error_code remoteEc;
                        const auto remote = socket->remote_endpoint(remoteEc);
                        if (remoteEc ||
                            !IsAcceptablePeerAddress(remote.address())) {
                            boost::system::error_code closeEc;
                            socket->close(closeEc);
                            if (!handler(std::unexpected(
                                    AcceptError::kNonLoopbackPeerRejected))) {
                                stop();
                                return;
                            }
                        } else if (!handler(std::move(*socket))) {
                            stop();
                            return;
                        }

                        (*acceptNext)();
                    } catch (...) {
                        {
                            std::lock_guard lock(lifecycle->mutex);
                            lifecycle->failure = std::current_exception();
                        }
                        stop();
                    }
                });
        };

        (*acceptNext)();
        ioContext_->run();
    } catch (...) {
        failure = std::current_exception();
        if (workGuard) {
            workGuard->reset();
        }
    }

    if (workGuard) {
        workGuard->reset();
    }

    //  The fallback path for a failed cancellation post stops the context
    //  without running the queued handler, so close the acceptor only after
    //  this accept-loop thread has regained exclusive access to it.
    {
        boost::system::error_code ec;
        acceptor_.close(ec);
    }

    {
        std::lock_guard lock(lifecycle->mutex);
        lifecycle->running = false;
        if (!failure) {
            failure = lifecycle->failure;
        }
    }
    lifecycle->changed.notify_all();

    if (failure) {
        std::rethrow_exception(failure);
    }
}

boost::asio::ip::tcp::endpoint LoopbackListener::LocalEndpoint() const {
    return acceptor_.local_endpoint();
}

void LoopbackListener::Close() noexcept {
    const auto lifecycle = lifecycle_;
    bool closeDirectly = false;
    bool postCancellation = false;
    bool waitForLoop = false;

    {
        std::lock_guard lock(lifecycle->mutex);
        lifecycle->closeRequested = true;
        if (!lifecycle->running) {
            closeDirectly = true;
        } else {
            waitForLoop = true;
            if (!lifecycle->closePosted) {
                lifecycle->closePosted = true;
                postCancellation = true;
            }
        }
    }

    if (closeDirectly) {
        boost::system::error_code ec;
        acceptor_.close(ec);
        return;
    }

    if (postCancellation) {
        try {
            boost::asio::post(*ioContext_, [this] {
                boost::system::error_code ec;
                acceptor_.cancel(ec);
                acceptor_.close(ec);
            });
        } catch (...) {
            //  Stopping the listener's dedicated context lets RunAcceptLoop()
            //  regain sole access and close the acceptor on its own thread.
            ioContext_->stop();
        }
    }

    if (waitForLoop) {
        std::unique_lock lock(lifecycle->mutex);
        lifecycle->changed.wait(lock,
                                [&lifecycle] { return !lifecycle->running; });
    }
}

} //  namespace dovahlink::transport
