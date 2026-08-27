#include "transport/loopback_listener.hpp"
#include "transport/loopback_listener_state.hpp"

#include <boost/asio/error.hpp>
#include <boost/asio/ip/address.hpp>
#include <boost/asio/post.hpp>
#include <boost/asio/socket_base.hpp>
#include <boost/system/error_code.hpp>

#include <condition_variable>
#include <exception>
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
    const auto lifecycle = lifecycle_;
    auto socket =
        std::make_shared<boost::asio::ip::tcp::socket>(*ioContext_);
    auto acceptEc = std::make_shared<boost::system::error_code>();
    auto acceptCompleted = std::make_shared<std::atomic_bool>(false);
    {
        std::lock_guard lock(lifecycle->mutex);
        if (lifecycle->running || lifecycle->closeRequested) {
            return std::unexpected(AcceptError::kAcceptFailed);
        }
        lifecycle->running = true;
        lifecycle->contextRunning = true;
        lifecycle->acceptCompleted = acceptCompleted;
    }
    std::exception_ptr failure;

    try {
        ioContext_->restart();
        bool shouldAccept = false;
        {
            std::lock_guard lock(lifecycle->mutex);
            shouldAccept = !lifecycle->closeRequested;
        }
        if (shouldAccept) {
            acceptor_.async_accept(
                *socket,
                [acceptEc, acceptCompleted](boost::system::error_code ec) {
                    *acceptEc = ec;
                    acceptCompleted->store(true, std::memory_order_release);
                });
            while (!acceptCompleted->load(std::memory_order_acquire)) {
                const auto ran = ioContext_->run_one();
                if (ran == 0) {
                    break;
                }
            }
        }
    } catch (...) {
        failure = std::current_exception();
    }

    {
        std::lock_guard lock(lifecycle->mutex);
        lifecycle->contextRunning = false;
    }
    //  A Close() posted just before run() returned may still be queued. Drain
    //  it while this thread still owns the acceptor, before publishing that the
    //  one-shot operation is complete.
    ioContext_->poll();

    bool closeRequested = false;
    {
        std::lock_guard lock(lifecycle->mutex);
        closeRequested = lifecycle->closeRequested;
    }

    std::expected<boost::asio::ip::tcp::socket, AcceptError> result =
        std::unexpected(AcceptError::kAcceptFailed);
    if (!failure && !*acceptEc && socket->is_open() && !closeRequested) {
        boost::system::error_code remoteEc;
        const auto remote = socket->remote_endpoint(remoteEc);
        if (remoteEc || !IsAcceptablePeerAddress(remote.address())) {
            boost::system::error_code closeEc;
            socket->close(closeEc);
            result = std::unexpected(AcceptError::kNonLoopbackPeerRejected);
        } else {
            result = std::move(*socket);
        }
    }

    if (failure) {
        boost::system::error_code closeEc;
        acceptor_.close(closeEc);
    }

    {
        std::lock_guard lock(lifecycle->mutex);
        lifecycle->running = false;
        lifecycle->acceptCompleted.reset();
    }
    lifecycle->changed.notify_all();

    if (failure) {
        return std::unexpected(AcceptError::kAcceptFailed);
    }
    return result;
}

boost::asio::ip::tcp::endpoint LoopbackListener::LocalEndpoint() const {
    return acceptor_.local_endpoint();
}

void LoopbackListener::Close() noexcept {
    const auto lifecycle = lifecycle_;
    bool closeDirectly = false;
    bool waitForLoop = false;
    std::shared_ptr<std::atomic_bool> acceptCompleted;

    std::unique_lock lock(lifecycle->mutex);
    lifecycle->closeRequested = true;
    if (!lifecycle->running) {
        closeDirectly = true;
    } else {
        waitForLoop = true;
        if (!lifecycle->contextRunning) {
            //  The accept operation already settled on the worker thread, so
            //  there is nothing in flight to cancel; the wait below still
            //  closes the acceptor once that worker publishes completion.
        } else if (!lifecycle->closePosted) {
            lifecycle->closePosted = true;
            acceptCompleted = lifecycle->acceptCompleted;
            try {
                boost::asio::post(
                    *ioContext_, [this, acceptCompleted] {
                        boost::system::error_code ec;
                        acceptor_.cancel(ec);
                        acceptor_.close(ec);
                        if (acceptCompleted) {
                            acceptCompleted->store(
                                true, std::memory_order_release);
                        }
                    });
            } catch (...) {
                //  Stopping the listener's dedicated context lets
                //  AcceptLoopbackOnly() regain sole access and close the
                //  acceptor on its own thread.
                ioContext_->stop();
            }
        }
    }

    if (closeDirectly) {
        lock.unlock();
        boost::system::error_code ec;
        acceptor_.close(ec);
        return;
    }

    if (waitForLoop) {
        lifecycle->changed.wait(lock,
                                [&lifecycle] { return !lifecycle->running; });
        //  AcceptLoopbackOnly() no longer closes the acceptor on our behalf:
        //  its own closeRequested read can race the write above and observe
        //  a stale false. running == false here is proof this thread has
        //  sole access, so close unconditionally; repeated Close() calls
        //  make this idempotent.
        lock.unlock();
        boost::system::error_code ec;
        acceptor_.close(ec);
    }
}

} //  namespace dovahlink::transport
