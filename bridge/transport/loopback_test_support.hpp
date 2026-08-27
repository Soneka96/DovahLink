#pragma once

#include "transport/loopback_listener.hpp"
#include "transport/websocket_session.hpp"

#include <gmock/gmock.h>

#include <boost/asio/error.hpp>
#include <boost/asio/ip/tcp.hpp>
#include <boost/system/error_code.hpp>

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <exception>
#include <functional>
#include <memory>
#include <mutex>
#include <optional>
#include <stdexcept>
#include <stop_token>
#include <string>
#include <thread>
#include <utility>

namespace dovahlink::transport::test_support {

///  Creates a loopback listener or throws before dependent test objects are
///  constructed.
inline LoopbackListener
RequireLoopbackListener(LoopbackListener::IpVersion version,
                        std::uint16_t port = 0) {
    auto listener = LoopbackListener::Create(version, port);
    if (!listener.has_value()) {
        throw std::runtime_error("failed to create loopback test listener");
    }
    return std::move(*listener);
}

///  Owns one checked IPv4 loopback listener and an exception-contained server
///  thread.
class LoopbackWebSocketServer {
  public:
    ///  Work performed with the accepted server-side WebSocket session.
    using Work = std::function<void(WebSocketSession&)>;

    ///  Starts accepting one loopback connection for the supplied server work.
    explicit LoopbackWebSocketServer(Work work)
        : listener_(
              RequireLoopbackListener(LoopbackListener::IpVersion::kV4)),
          endpoint_(listener_.LocalEndpoint()),
          thread_([this, work = std::move(work)](
                      std::stop_token stopToken) mutable noexcept {
              Run(stopToken, std::move(work));
          }) {}

    ///  Cancels pending transport work and joins the server thread.
    ~LoopbackWebSocketServer() { StopAndJoin(); }

    ///  Prevents copying a thread-owning server fixture.
    LoopbackWebSocketServer(const LoopbackWebSocketServer&) = delete;
    ///  Prevents copying a thread-owning server fixture.
    LoopbackWebSocketServer& operator=(const LoopbackWebSocketServer&) = delete;

    ///  Returns the endpoint to which the test client connects.
    [[nodiscard]] boost::asio::ip::tcp::endpoint LocalEndpoint() const {
        return endpoint_;
    }

    ///  Returns whether server work completed within the supplied duration.
    template <class Rep, class Period>
    [[nodiscard]] bool
    WaitFor(const std::chrono::duration<Rep, Period>& timeout) {
        std::unique_lock lock(completionMutex_);
        return completionChanged_.wait_for(lock, timeout,
                                           [this] { return completed_; });
    }

    ///  Returns whether the server thread is about to call `AcceptLoopbackOnly()`
    ///  within the supplied duration.
    template <class Rep, class Period>
    [[nodiscard]] bool
    WaitForAcceptReady(const std::chrono::duration<Rep, Period>& timeout) {
        std::unique_lock lock(acceptReadyMutex_);
        return acceptReadyChanged_.wait_for(lock, timeout,
                                            [this] { return acceptReady_; });
    }

    ///  Returns whether the accepted session has been published to the
    ///  shutdown controller within the supplied duration.
    template <class Rep, class Period>
    [[nodiscard]] bool
    WaitForSessionReady(const std::chrono::duration<Rep, Period>& timeout) {
        std::unique_lock lock(sessionReadyMutex_);
        return sessionReadyChanged_.wait_for(lock, timeout,
                                             [this] { return sessionReady_; });
    }

    ///  Requests cancellation of the accepted WebSocket session, when present.
    void ShutdownActiveSession() noexcept {
        std::lock_guard lock(socketMutex_);
        if (activeSocket_) {
            activeSocket_->Shutdown();
        }
    }

    ///  Requests a best-effort notify-then-shutdown of the accepted WebSocket
    ///  session, when present, mirroring `ShutdownActiveSession()`.
    void NotifyAndShutdownActiveSession(std::string message) noexcept {
        std::lock_guard lock(socketMutex_);
        if (activeSocket_) {
            activeSocket_->ShutdownWithNotification(std::move(message));
        }
    }

    ///  Forces a pending accept to fail so infrastructure error reporting can be
    ///  exercised.
    void CancelPendingAccept() noexcept {
        cancellationRequested_.store(true, std::memory_order_release);
        //  Close() is idempotent and non-blocking, and the listener's owner
        //  thread is always pumping, so this always reaches a pending accept
        //  without the allocation-failure fallback the old post-based version
        //  needed.
        listener_.Close();
    }

    ///  Joins completed server work and rethrows server-side failures on the test
    ///  thread.
    void Join() {
        if (thread_.joinable()) {
            thread_.join();
        }
        if (serverException_) {
            std::rethrow_exception(serverException_);
        }
        if (acceptError_) {
            throw std::runtime_error("loopback test server accept failed: " +
                                     acceptError_.message());
        }
    }

  private:
    ///  Accepts one socket and runs server work without allowing exceptions to
    ///  escape the thread.
    void Run(std::stop_token stopToken, Work work) noexcept {
        try {
            std::optional<boost::asio::ip::tcp::socket> acceptedSocket;
            if (stopToken.stop_requested() ||
                cancellationRequested_.load(std::memory_order_acquire)) {
                acceptError_ = boost::asio::error::operation_aborted;
            } else {
                {
                    std::lock_guard lock(acceptReadyMutex_);
                    acceptReady_ = true;
                }
                acceptReadyChanged_.notify_all();

                auto accepted = listener_.AcceptLoopbackOnly();
                if (accepted.has_value()) {
                    acceptedSocket = std::move(*accepted);
                } else {
                    acceptError_ = boost::asio::error::operation_aborted;
                }
                if ((stopToken.stop_requested() ||
                     cancellationRequested_.load(std::memory_order_acquire)) &&
                    !acceptError_) {
                    acceptError_ = boost::asio::error::operation_aborted;
                }
            }

            if (!acceptError_) {
                WebSocketSession::SocketHandle socket =
                    WebSocketSession::CreateSocket(std::move(*acceptedSocket));
                {
                    std::lock_guard lock(socketMutex_);
                    activeSocket_ = socket;
                }
                {
                    std::lock_guard lock(sessionReadyMutex_);
                    sessionReady_ = true;
                }
                sessionReadyChanged_.notify_all();

                if (stopToken.stop_requested()) {
                    socket->Shutdown();
                } else {
                    WebSocketSession session(std::move(socket));
                    work(session);
                }
            }
        } catch (...) {
            serverException_ = std::current_exception();
        }

        std::lock_guard lock(socketMutex_);
        activeSocket_.reset();
        PublishCompletion();
    }

    ///  Publishes server-thread completion to bounded test waits.
    void PublishCompletion() noexcept {
        {
            std::lock_guard lock(completionMutex_);
            completed_ = true;
        }
        completionChanged_.notify_all();
    }

    ///  Stops an accept or active session and joins without throwing from
    ///  destruction.
    void StopAndJoin() noexcept {
        if (!thread_.joinable()) {
            return;
        }

        thread_.request_stop();
        CancelPendingAccept();
        ShutdownActiveSession();
        thread_.join();
    }

    ///  Accepts the one loopback connection this server drives.
    LoopbackListener listener_;
    ///  Cached before the server thread starts so callers never read the acceptor
    ///  concurrently.
    boost::asio::ip::tcp::endpoint endpoint_;
    ///  Records cancellation before its server-thread post is attempted.
    std::atomic_bool cancellationRequested_{false};
    ///  Serializes readiness publication and bounded readiness waits.
    std::mutex acceptReadyMutex_;
    ///  Wakes tests once the server thread is about to accept.
    std::condition_variable acceptReadyChanged_;
    ///  Records that the server thread is about to call `AcceptLoopbackOnly()`.
    bool acceptReady_{false};
    ///  Serializes session-publication readiness and bounded waits.
    std::mutex sessionReadyMutex_;
    ///  Wakes tests after the accepted session has been published.
    std::condition_variable sessionReadyChanged_;
    ///  Records that the accepted session has been published.
    bool sessionReady_{false};
    ///  Serializes access to the active socket retained for cleanup.
    std::mutex socketMutex_;
    ///  Interrupts session work when client-side setup or assertions exit early.
    WebSocketSession::SocketHandle activeSocket_;
    ///  Records accept failure for reporting from the main test thread.
    boost::system::error_code acceptError_;
    ///  Records unexpected server work failure for reporting from the main test
    ///  thread.
    std::exception_ptr serverException_;
    ///  Serializes completion publication and bounded waiting.
    std::mutex completionMutex_;
    ///  Wakes tests when the server thread finishes.
    std::condition_variable completionChanged_;
    ///  Records that the server thread reached its terminal state.
    bool completed_{false};
    ///  Owns and joins the one server worker.
    std::jthread thread_;
};

//  ---- Reusable contract mocks ----

///  GoogleMock loopback-listener contract double.
class MockLoopbackListener : public ILoopbackListener {
  public:
    MOCK_METHOD((std::expected<boost::asio::ip::tcp::socket, AcceptError>),
                AcceptLoopbackOnly, (), (override));
    MOCK_METHOD(void, Close, (), (noexcept, override));
    MOCK_METHOD(void, Join, (), (override));
};

} //  namespace dovahlink::transport::test_support
