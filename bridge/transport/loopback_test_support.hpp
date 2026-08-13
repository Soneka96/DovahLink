#pragma once

#include "transport/listener.hpp"
#include "transport/websocket_session.hpp"

#include <boost/asio/io_context.hpp>
#include <boost/asio/ip/tcp.hpp>
#include <boost/system/error_code.hpp>

#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <exception>
#include <functional>
#include <mutex>
#include <stdexcept>
#include <stop_token>
#include <string>
#include <thread>
#include <utility>

namespace dovahlink::transport::test_support {

/// Creates a loopback listener or throws before dependent test objects are constructed.
inline LoopbackListener RequireLoopbackListener(boost::asio::io_context& ioc, LoopbackListener::IpVersion version,
                                                std::uint16_t port = 0) {
    auto listener = LoopbackListener::Create(ioc, version, port);
    if (!listener.has_value()) {
        throw std::runtime_error("failed to create loopback test listener");
    }
    return std::move(*listener);
}

/// Owns one checked IPv4 loopback listener and an exception-contained server thread.
class LoopbackWebSocketServer {
public:
    /// Work performed with the accepted server-side WebSocket session.
    using Work = std::function<void(WebSocketSession&)>;
    /// Server work that also coordinates directly with the server event loop.
    using ContextWork = std::function<void(WebSocketSession&, boost::asio::io_context&)>;

    /// Starts accepting one loopback connection for the supplied server work.
    explicit LoopbackWebSocketServer(Work work)
        : LoopbackWebSocketServer(
              ContextWork([work = std::move(work)](WebSocketSession& session, boost::asio::io_context&) mutable {
                  work(session);
              })) {}

    /// Starts accepting one loopback connection for work that needs the server event loop.
    explicit LoopbackWebSocketServer(ContextWork work)
        : listener_(RequireLoopbackListener(ioc_, LoopbackListener::IpVersion::kV4)),
          thread_([this, work = std::move(work)](std::stop_token stopToken) mutable noexcept {
              Run(stopToken, std::move(work));
          }) {}

    /// Cancels pending transport work and joins the server thread.
    ~LoopbackWebSocketServer() { StopAndJoin(); }

    /// Prevents copying a thread-owning server fixture.
    LoopbackWebSocketServer(const LoopbackWebSocketServer&) = delete;
    /// Prevents copying a thread-owning server fixture.
    LoopbackWebSocketServer& operator=(const LoopbackWebSocketServer&) = delete;

    /// Returns the endpoint to which the test client connects.
    [[nodiscard]] boost::asio::ip::tcp::endpoint LocalEndpoint() const { return listener_.LocalEndpoint(); }

    /// Returns whether server work completed within the supplied duration.
    template <class Rep, class Period> [[nodiscard]] bool WaitFor(const std::chrono::duration<Rep, Period>& timeout) {
        std::unique_lock lock(completionMutex_);
        return completionChanged_.wait_for(lock, timeout, [this] { return completed_; });
    }

    /// Requests cancellation of the accepted WebSocket session, when present.
    void ShutdownActiveSession() noexcept {
        std::lock_guard lock(socketMutex_);
        if (activeSocket_) {
            activeSocket_->Shutdown();
        }
    }

    /// Forces a pending accept to fail so infrastructure error reporting can be exercised.
    void CancelPendingAccept() noexcept {
        boost::system::error_code ignored;
        listener_.Acceptor().cancel(ignored);
        listener_.Acceptor().close(ignored);
    }

    /// Joins completed server work and rethrows server-side failures on the test thread.
    void Join() {
        if (thread_.joinable()) {
            thread_.join();
        }
        if (serverException_) {
            std::rethrow_exception(serverException_);
        }
        if (acceptError_) {
            throw std::runtime_error("loopback test server accept failed: " + acceptError_.message());
        }
    }

private:
    /// Accepts one socket and runs server work without allowing exceptions to escape the thread.
    void Run(std::stop_token stopToken, ContextWork work) noexcept {
        try {
            boost::asio::ip::tcp::socket acceptedSocket = listener_.Acceptor().accept(acceptError_);
            if (!acceptError_) {
                WebSocketSession::SocketHandle socket = WebSocketSession::CreateSocket(std::move(acceptedSocket));
                {
                    std::lock_guard lock(socketMutex_);
                    activeSocket_ = socket;
                }

                if (stopToken.stop_requested()) {
                    socket->Shutdown();
                } else {
                    WebSocketSession session(std::move(socket));
                    work(session, ioc_);
                }
            }
        } catch (...) {
            serverException_ = std::current_exception();
        }

        std::lock_guard lock(socketMutex_);
        activeSocket_.reset();
        PublishCompletion();
    }

    /// Publishes server-thread completion to bounded test waits.
    void PublishCompletion() noexcept {
        {
            std::lock_guard lock(completionMutex_);
            completed_ = true;
        }
        completionChanged_.notify_all();
    }

    /// Stops an accept or active session and joins without throwing from destruction.
    void StopAndJoin() noexcept {
        if (!thread_.joinable()) {
            return;
        }

        thread_.request_stop();
        CancelPendingAccept();
        ShutdownActiveSession();
        thread_.join();
    }

    /// Drives the accepted socket's asynchronous WebSocket operations.
    boost::asio::io_context ioc_;
    /// Owns the acceptor until cleanup has stopped the server thread.
    LoopbackListener listener_;
    /// Serializes access to the active socket retained for cleanup.
    std::mutex socketMutex_;
    /// Interrupts session work when client-side setup or assertions exit early.
    WebSocketSession::SocketHandle activeSocket_;
    /// Records accept failure for reporting from the main test thread.
    boost::system::error_code acceptError_;
    /// Records unexpected server work failure for reporting from the main test thread.
    std::exception_ptr serverException_;
    /// Serializes completion publication and bounded waiting.
    std::mutex completionMutex_;
    /// Wakes tests when the server thread finishes.
    std::condition_variable completionChanged_;
    /// Records that the server thread reached its terminal state.
    bool completed_{false};
    /// Owns and joins the one server worker.
    std::jthread thread_;
};

}  // namespace dovahlink::transport::test_support
