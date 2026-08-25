#pragma once

#include "transport/listener.hpp"
#include "transport/websocket_session.hpp"

#include <boost/asio/error.hpp>
#include <boost/asio/io_context.hpp>
#include <boost/asio/ip/tcp.hpp>
#include <boost/asio/post.hpp>
#include <boost/system/error_code.hpp>

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <exception>
#include <functional>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <stop_token>
#include <string>
#include <thread>
#include <utility>

namespace dovahlink::transport::test_support {

/// Creates a loopback listener or throws before dependent test objects are
/// constructed.
inline LoopbackListener
RequireLoopbackListener(boost::asio::io_context &ioc,
                        LoopbackListener::IpVersion version,
                        std::uint16_t port = 0) {
  auto listener = LoopbackListener::Create(ioc, version, port);
  if (!listener.has_value()) {
    throw std::runtime_error("failed to create loopback test listener");
  }
  return std::move(*listener);
}

/// Owns one checked IPv4 loopback listener and an exception-contained server
/// thread.
class LoopbackWebSocketServer {
public:
  /// Work performed with the accepted server-side WebSocket session.
  using Work = std::function<void(WebSocketSession &)>;
  /// Server work that also coordinates directly with the server event loop.
  using ContextWork =
      std::function<void(WebSocketSession &, boost::asio::io_context &)>;

  /// Starts accepting one loopback connection for the supplied server work.
  explicit LoopbackWebSocketServer(Work work)
      : LoopbackWebSocketServer(ContextWork(
            [work = std::move(work)](WebSocketSession &session,
                                     boost::asio::io_context &) mutable {
              work(session);
            })) {}

  /// Starts accepting one loopback connection for work that needs the server
  /// event loop.
  explicit LoopbackWebSocketServer(ContextWork work)
      : listener_(
            RequireLoopbackListener(ioc_, LoopbackListener::IpVersion::kV4)),
        endpoint_(listener_.LocalEndpoint()),
        thread_([this, work = std::move(work)](
                    std::stop_token stopToken) mutable noexcept {
          Run(stopToken, std::move(work));
        }) {}

  /// Cancels pending transport work and joins the server thread.
  ~LoopbackWebSocketServer() { StopAndJoin(); }

  /// Prevents copying a thread-owning server fixture.
  LoopbackWebSocketServer(const LoopbackWebSocketServer &) = delete;
  /// Prevents copying a thread-owning server fixture.
  LoopbackWebSocketServer &operator=(const LoopbackWebSocketServer &) = delete;

  /// Returns the endpoint to which the test client connects.
  [[nodiscard]] boost::asio::ip::tcp::endpoint LocalEndpoint() const {
    return endpoint_;
  }

  /// Returns whether server work completed within the supplied duration.
  template <class Rep, class Period>
  [[nodiscard]] bool
  WaitFor(const std::chrono::duration<Rep, Period> &timeout) {
    std::unique_lock lock(completionMutex_);
    return completionChanged_.wait_for(lock, timeout,
                                       [this] { return completed_; });
  }

  /// Returns whether the server thread has posted its pending accept within the
  /// supplied duration.
  template <class Rep, class Period>
  [[nodiscard]] bool
  WaitForAcceptReady(const std::chrono::duration<Rep, Period> &timeout) {
    std::unique_lock lock(acceptReadyMutex_);
    return acceptReadyChanged_.wait_for(lock, timeout,
                                        [this] { return acceptReady_; });
  }

  /// Requests cancellation of the accepted WebSocket session, when present.
  void ShutdownActiveSession() noexcept {
    std::lock_guard lock(socketMutex_);
    if (activeSocket_) {
      activeSocket_->Shutdown();
    }
  }

  /// Requests a best-effort notify-then-shutdown of the accepted WebSocket
  /// session, when present, mirroring `ShutdownActiveSession()`.
  void NotifyAndShutdownActiveSession(std::string message) noexcept {
    std::lock_guard lock(socketMutex_);
    if (activeSocket_) {
      activeSocket_->ShutdownWithNotification(std::move(message));
    }
  }

  /// Forces a pending accept to fail so infrastructure error reporting can be
  /// exercised.
  void CancelPendingAccept() noexcept {
    cancellationRequested_.store(true, std::memory_order_release);
    try {
      boost::asio::post(ioc_, [this] noexcept {
        boost::system::error_code ignored;
        listener_.Acceptor().cancel(ignored);
        listener_.Acceptor().close(ignored);
      });
    } catch (...) {
      // A failed post must still wake a pending accept without touching
      // the acceptor or stopping an active session from this thread.
      WakePendingAccept();
    }
  }

  /// Joins completed server work and rethrows server-side failures on the test
  /// thread.
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
  /// Accepts one socket and runs server work without allowing exceptions to
  /// escape the thread.
  void Run(std::stop_token stopToken, ContextWork work) noexcept {
    try {
      auto acceptedSocket =
          std::make_shared<boost::asio::ip::tcp::socket>(ioc_);
      if (stopToken.stop_requested() ||
          cancellationRequested_.load(std::memory_order_acquire)) {
        acceptError_ = boost::asio::error::operation_aborted;
      } else {
        auto asyncAcceptError = std::make_shared<boost::system::error_code>();
        ioc_.restart();
        listener_.Acceptor().async_accept(
            *acceptedSocket,
            [asyncAcceptError](boost::system::error_code error) {
              *asyncAcceptError = error;
            });
        {
          std::lock_guard lock(acceptReadyMutex_);
          acceptReady_ = true;
        }
        acceptReadyChanged_.notify_all();
        ioc_.run();

        acceptError_ = *asyncAcceptError;
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

  /// Connects to the listener to wake a pending accept when posting
  /// cancellation fails.
  void WakePendingAccept() noexcept {
    try {
      boost::asio::io_context wakeIoc;
      boost::asio::ip::tcp::socket wakeSocket(wakeIoc);
      boost::system::error_code ignored;
      wakeSocket.connect(endpoint_, ignored);
    } catch (...) {
      // The normal posted cancellation remains the primary path; this is
      // only a best-effort allocation-failure fallback.
    }
  }

  /// Publishes server-thread completion to bounded test waits.
  void PublishCompletion() noexcept {
    {
      std::lock_guard lock(completionMutex_);
      completed_ = true;
    }
    completionChanged_.notify_all();
  }

  /// Stops an accept or active session and joins without throwing from
  /// destruction.
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
  /// The acceptor is accessed only by the server thread after construction.
  LoopbackListener listener_;
  /// Cached before the server thread starts so callers never read the acceptor
  /// concurrently.
  boost::asio::ip::tcp::endpoint endpoint_;
  /// Records cancellation before its server-thread post is attempted.
  std::atomic_bool cancellationRequested_{false};
  /// Serializes readiness publication and bounded readiness waits.
  std::mutex acceptReadyMutex_;
  /// Wakes tests after the asynchronous accept has been posted.
  std::condition_variable acceptReadyChanged_;
  /// Records that the server thread has posted its asynchronous accept.
  bool acceptReady_{false};
  /// Serializes access to the active socket retained for cleanup.
  std::mutex socketMutex_;
  /// Interrupts session work when client-side setup or assertions exit early.
  WebSocketSession::SocketHandle activeSocket_;
  /// Records accept failure for reporting from the main test thread.
  boost::system::error_code acceptError_;
  /// Records unexpected server work failure for reporting from the main test
  /// thread.
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

} // namespace dovahlink::transport::test_support
