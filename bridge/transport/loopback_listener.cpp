#include "transport/loopback_listener.hpp"
#include "transport/loopback_listener_state.hpp"

#include <boost/asio/error.hpp>
#include <boost/asio/executor_work_guard.hpp>
#include <boost/asio/ip/address.hpp>
#include <boost/asio/post.hpp>
#include <boost/asio/socket_base.hpp>
#include <boost/system/error_code.hpp>

#include <future>
#include <memory>
#include <thread>
#include <utility>

namespace dovahlink::transport {

bool IsAcceptablePeerAddress(const boost::asio::ip::address& address) {
    return address.is_loopback();
}

LoopbackListener::OwnerState::OwnerState()
    : ioContext(), workGuard(boost::asio::make_work_guard(ioContext)),
      acceptor(ioContext) {}

std::expected<LoopbackListener, ListenerError>
LoopbackListener::Create(IpVersion version, std::uint16_t port) {
    boost::system::error_code ec;

    const char* loopbackAddress =
        (version == IpVersion::kV4) ? "127.0.0.1" : "::1";
    boost::asio::ip::address address =
        boost::asio::ip::make_address(loopbackAddress, ec);
    if (ec) {
        return std::unexpected(ListenerError::kBindFailed);
    }
    boost::asio::ip::tcp::endpoint endpoint(address, port);

    auto state = std::make_shared<OwnerState>();

    state->acceptor.open(endpoint.protocol(), ec);
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
    state->acceptor.bind(endpoint, ec);
    if (ec) {
        return std::unexpected(ListenerError::kBindFailed);
    }

    state->acceptor.listen(boost::asio::socket_base::max_listen_connections, ec);
    if (ec) {
        return std::unexpected(ListenerError::kBindFailed);
    }

    state->ownerThread =
        std::thread([state] { LoopbackListener::RunOwnerThread(state); });
    return LoopbackListener(std::move(state));
}

LoopbackListener::LoopbackListener(std::shared_ptr<OwnerState> state)
    : state_(std::move(state)) {}

void LoopbackListener::RunOwnerThread(
    std::shared_ptr<OwnerState> state) noexcept {
    try {
        state->ioContext.run();
    } catch (...) {
        //  Nothing further can safely run on this listener's owner thread.
    }
}

LoopbackListener::~LoopbackListener() {
    if (!state_) {
        //  Moved-from: nothing owned to join.
        return;
    }
    try {
        Join();
    } catch (...) {
        //  A destructor must never throw; there is no further recovery
        //  possible for a listener that fails to join its owner thread.
    }
}

boost::asio::ip::tcp::acceptor& LoopbackListener::Acceptor() {
    return state_->acceptor;
}

std::expected<boost::asio::ip::tcp::socket, AcceptError>
LoopbackListener::AcceptLoopbackOnly() {
    if (state_->acceptInFlight.exchange(true, std::memory_order_acq_rel)) {
        return std::unexpected(AcceptError::kAcceptFailed);
    }

    using AcceptResult =
        std::expected<boost::asio::ip::tcp::socket, AcceptError>;
    std::promise<AcceptResult> outcome;
    auto future = outcome.get_future();

    try {
        boost::asio::post(state_->ioContext, [state = state_,
                                              &outcome]() noexcept {
            try {
                if (state->closeRequested.load(std::memory_order_acquire)) {
                    outcome.set_value(std::unexpected(AcceptError::kAcceptFailed));
                    return;
                }
                auto socket = std::make_shared<boost::asio::ip::tcp::socket>(
                    state->ioContext);
                state->acceptor.async_accept(
                    *socket, [state, socket,
                              &outcome](boost::system::error_code ec) noexcept {
                        try {
                            if (ec || !socket->is_open()) {
                                outcome.set_value(
                                    std::unexpected(AcceptError::kAcceptFailed));
                                return;
                            }
                            boost::system::error_code remoteEc;
                            const auto remote =
                                socket->remote_endpoint(remoteEc);
                            if (remoteEc ||
                                !IsAcceptablePeerAddress(remote.address())) {
                                boost::system::error_code closeEc;
                                socket->close(closeEc);
                                outcome.set_value(std::unexpected(
                                    AcceptError::kNonLoopbackPeerRejected));
                                return;
                            }
                            outcome.set_value(std::move(*socket));
                        } catch (...) {
                            boost::system::error_code closeEc;
                            state->acceptor.close(closeEc);
                            outcome.set_value(
                                std::unexpected(AcceptError::kAcceptFailed));
                        }
                    });
            } catch (...) {
                boost::system::error_code closeEc;
                state->acceptor.close(closeEc);
                outcome.set_value(std::unexpected(AcceptError::kAcceptFailed));
            }
        });
    } catch (...) {
        state_->acceptInFlight.store(false, std::memory_order_release);
        return std::unexpected(AcceptError::kAcceptFailed);
    }

    auto result = future.get();
    state_->acceptInFlight.store(false, std::memory_order_release);
    return result;
}

boost::asio::ip::tcp::endpoint LoopbackListener::LocalEndpoint() const {
    return state_->acceptor.local_endpoint();
}

void LoopbackListener::Close() noexcept {
    if (state_->closeRequested.exchange(true, std::memory_order_acq_rel)) {
        return;
    }
    try {
        boost::asio::post(state_->ioContext, [state = state_] {
            boost::system::error_code ec;
            state->acceptor.cancel(ec);
            state->acceptor.close(ec);
            state->closed.set_value();
        });
    } catch (...) {
        //  Posting failed (allocation failure, or the io_context is already
        //  stopping); nothing further can safely touch the acceptor from this
        //  thread, so unblock Join() directly instead of leaving it waiting
        //  on a job that will never run.
        state_->closed.set_value();
    }
}

void LoopbackListener::Join() {
    if (state_->joined.exchange(true, std::memory_order_acq_rel)) {
        return;
    }
    Close();
    state_->closed.get_future().wait();
    state_->workGuard.reset();
    state_->ioContext.stop();
    if (state_->ownerThread.joinable()) {
        state_->ownerThread.join();
    }
}

} //  namespace dovahlink::transport
