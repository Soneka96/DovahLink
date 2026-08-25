#include "transport/websocket_session.hpp"

#include "security/limits.hpp"

#include <boost/asio/buffer.hpp>
#include <boost/asio/post.hpp>
#include <boost/asio/steady_timer.hpp>
#include <boost/beast/core/buffers_to_string.hpp>
#include <boost/beast/core/error.hpp>
#include <boost/beast/core/flat_buffer.hpp>
#include <boost/beast/websocket/error.hpp>
#include <boost/beast/websocket/option.hpp>
#include <boost/beast/websocket/rfc6455.hpp>
#include <boost/system/error_code.hpp>

#include <chrono>
#include <future>
#include <memory>
#include <stdexcept>
#include <utility>

namespace dovahlink::transport {
namespace {

/// Runs one asynchronous stream operation to completion on the worker thread.
template <typename StartOperation>
boost::beast::error_code RunOperation(boost::asio::io_context &ioContext,
                                      StartOperation &&startOperation) {
  std::promise<boost::beast::error_code> completion;
  auto result = completion.get_future();

  ioContext.restart();
  std::forward<StartOperation>(startOperation)(
      [&completion](boost::beast::error_code ec, auto...) {
        completion.set_value(ec);
      });

  while (result.wait_for(std::chrono::seconds::zero()) !=
         std::future_status::ready) {
    if (ioContext.run_one() == 0) {
      // Preserve every stack-owned operation buffer even if the
      // worker-exclusive event-loop invariant is violated elsewhere.
      result.wait();
    }
  }
  return result.get();
}

} // namespace

WebSocketSession::Socket::Socket(boost::asio::ip::tcp::socket socket)
    : ioContext_(static_cast<boost::asio::io_context &>(
          socket.get_executor().context())),
      stream_(boost::beast::tcp_stream(std::move(socket))) {}

void WebSocketSession::Socket::Shutdown() noexcept {
  if (shutdownRequested_.exchange(true, std::memory_order_acq_rel)) {
    return;
  }

  try {
    auto socket = weak_from_this();
    boost::asio::post(ioContext_, [socket = std::move(socket)] {
      auto activeSocket = socket.lock();
      if (!activeSocket) {
        return;
      }
      try {
        auto &tcpStream = activeSocket->stream_.next_layer();
        tcpStream.cancel();
        tcpStream.close();
      } catch (...) {
        // Cancellation remains best-effort at this noexcept boundary.
      }
    });
  } catch (...) {
    // Shutdown is a best-effort noexcept boundary. A pending operation still
    // retains its configured timeout if the cancellation handler cannot queue.
  }
}

void WebSocketSession::Socket::ShutdownWithNotification(
    std::string message) noexcept {
  if (shutdownRequested_.exchange(true, std::memory_order_acq_rel)) {
    return;
  }

  try {
    auto socket = weak_from_this();
    // Shared, not captured by value in only the outer lambda:
    // boost::asio::buffer(*sharedMessage) below is a non-owning view, and
    // async_write only registers the write before returning -- the actual bytes
    // are copied later, on a subsequent io_context turn. A message captured
    // only by the outer lambda would be destroyed the moment that lambda
    // returns, leaving async_write reading from a freed buffer. Sharing
    // ownership with the completion lambda keeps it alive for the write's whole
    // duration.
    auto sharedMessage = std::make_shared<std::string>(std::move(message));
    boost::asio::post(ioContext_, [socket = std::move(socket), sharedMessage] {
      auto activeSocket = socket.lock();
      if (!activeSocket) {
        return;
      }
      auto cancelAndClose = [](Socket &target) noexcept {
        try {
          auto &tcpStream = target.stream_.next_layer();
          tcpStream.cancel();
          tcpStream.close();
        } catch (...) {
          // Cancellation remains best-effort at this noexcept boundary.
        }
      };
      if (!activeSocket->handshakeSettled_.load(std::memory_order_acquire)) {
        // Accept()'s own handshake operation may still be using stream_ --
        // starting a websocket-level write now would race it unsafely.
        // Best-effort delivery permits skipping the notification here; the
        // connection is still force-closed.
        cancelAndClose(*activeSocket);
        return;
      }
      try {
        // The connection's own read loop may currently be blocked inside
        // ReadMessage, which explicitly disables the write timeout for its own
        // duration (DisableWriteTimeout) -- this write needs its own bounded
        // deadline so a stalled peer still cannot prevent the force-close this
        // method exists to guarantee.
        boost::beast::get_lowest_layer(activeSocket->stream_)
            .expires_after(security::kHandshakeTimeout);
        activeSocket->stream_.text(true);
        activeSocket->stream_.async_write(
            boost::asio::buffer(*sharedMessage),
            // The write's own error_code is intentionally ignored: whether it
            // succeeded, failed, or timed out, the connection still
            // force-closes either way, matching
            // `ai/context/protocol/security.md`'s "Administrative session
            // invalidation" best-effort ordering -- there is no stronger
            // delivery guarantee to react to.
            [socket, sharedMessage, cancelAndClose](boost::beast::error_code,
                                                    std::size_t) {
              auto activeSocket = socket.lock();
              if (!activeSocket) {
                return;
              }
              cancelAndClose(*activeSocket);
            });
      } catch (...) {
        // The write itself failed to even start -- best-effort delivery permits
        // skipping the notification, matching the handshake-not-settled branch
        // above, but the connection must still be force-closed rather than left
        // open on this rare path.
        cancelAndClose(*activeSocket);
      }
    });
  } catch (...) {
    // Best-effort noexcept boundary, matching Shutdown().
  }
}

WebSocketSession::WebSocketSession(boost::asio::ip::tcp::socket socket)
    : WebSocketSession(CreateSocket(std::move(socket))) {}

WebSocketSession::WebSocketSession(SocketHandle socket)
    : socket_(std::move(socket)),
      operationTimeout_(security::kHandshakeTimeout) {
  static_cast<void>(RequireSocket(socket_));
}

WebSocketSession::SocketHandle
WebSocketSession::CreateSocket(boost::asio::ip::tcp::socket socket) {
  return SocketHandle(new Socket(std::move(socket)));
}

WebSocketSession::Socket &
WebSocketSession::RequireSocket(const SocketHandle &socket) {
  if (!socket) {
    throw std::invalid_argument("WebSocketSession requires a socket");
  }
  return *socket;
}

bool WebSocketSession::IsShutdownRequested() const noexcept {
  return socket_->shutdownRequested_.load(std::memory_order_acquire);
}

void WebSocketSession::SetTimeoutPolicy(
    std::chrono::steady_clock::duration timeout) {
  operationTimeout_ = timeout;
  boost::beast::websocket::stream_base::timeout timeoutOptions;
  timeoutOptions.handshake_timeout = security::kHandshakeTimeout;
  timeoutOptions.idle_timeout = timeout;
  // Genuine WebSocket-level Ping/Pong (ai/context/protocol/security.md's
  // "Connection liveness"): a peer that is actually alive but between
  // application messages answers these automatically and stays connected;
  // ReadMessage's own idleDeadline watchdog is what still bounds total idle
  // time regardless, since ping/pong answers would otherwise let Beast's own
  // per-operation timeout run indefinitely.
  timeoutOptions.keep_alive_pings = true;
  socket_->stream_.set_option(timeoutOptions);
}

void WebSocketSession::ArmWriteTimeout(
    std::chrono::steady_clock::duration timeout) {
  boost::beast::get_lowest_layer(socket_->stream_).expires_after(timeout);
}

void WebSocketSession::DisableWriteTimeout() {
  boost::beast::get_lowest_layer(socket_->stream_).expires_never();
}

std::expected<void, SessionError> WebSocketSession::Accept() {
  if (IsShutdownRequested()) {
    return std::unexpected(SessionError::kHandshakeFailed);
  }

  boost::beast::websocket::permessage_deflate compressionOptions;
  compressionOptions.client_enable = false;
  compressionOptions.server_enable = false;
  socket_->stream_.set_option(compressionOptions);

  socket_->stream_.read_message_max(security::kMaxInboundFrameBytes);

  // Applied before accept() itself so the WebSocket upgrade handshake's
  // own wait is bounded too, not just reads that come after it.
  SetTimeoutPolicy(security::kHandshakeTimeout);

  auto ec = RunOperation(socket_->ioContext_, [this](auto completion) {
    socket_->stream_.async_accept(std::move(completion));
  });
  // No further operation belonging to the handshake itself remains in flight on
  // stream_, regardless of outcome -- safe for ShutdownWithNotification to
  // start its own write now.
  socket_->handshakeSettled_.store(true, std::memory_order_release);
  if (ec) {
    return std::unexpected(SessionError::kHandshakeFailed);
  }
  return {};
}

void WebSocketSession::SwitchToIdleTimeout() {
  if (!IsShutdownRequested()) {
    SetTimeoutPolicy(security::kIdleTimeout);
  }
}

std::expected<std::string, SessionError> WebSocketSession::ReadMessage(
    std::optional<std::chrono::steady_clock::time_point> idleDeadline) {
  if (IsShutdownRequested()) {
    return std::unexpected(SessionError::kReadFailed);
  }
  DisableWriteTimeout();

  // Races this read against idleDeadline independent of Beast's own
  // per-operation timeout: a peer that keeps answering WebSocket-level pings
  // (keep_alive_pings above) would otherwise let that timeout run indefinitely
  // without ever sending a real application message. Captures a copy of the
  // shared socket handle, not `this`, so a fired-but-canceled watchdog stays
  // safe even if this ReadMessage call (and its stack-local timer) has already
  // returned.
  boost::asio::steady_timer idleWatchdog(socket_->ioContext_);
  if (idleDeadline.has_value()) {
    idleWatchdog.expires_at(*idleDeadline);
    idleWatchdog.async_wait([socket = socket_](boost::system::error_code ec) {
      if (!ec) {
        socket->Shutdown();
      }
    });
  }

  boost::beast::flat_buffer buffer;
  auto ec = RunOperation(socket_->ioContext_, [this, &buffer](auto completion) {
    socket_->stream_.async_read(buffer, std::move(completion));
  });
  idleWatchdog.cancel();
  // Drains the watchdog's own now-canceled completion (a no-op, since it
  // observes operation_aborted) so it never sits unprocessed on the io_context
  // across calls -- RunOperation restarts this same io_context on every call
  // and expects no leftover pending work.
  socket_->ioContext_.poll();

  if (ec == boost::beast::websocket::error::message_too_big) {
    return std::unexpected(SessionError::kFrameTooLarge);
  }
  if (ec) {
    return std::unexpected(SessionError::kReadFailed);
  }
  if (socket_->stream_.got_binary()) {
    return std::unexpected(SessionError::kBinaryFrameRejected);
  }
  return boost::beast::buffers_to_string(buffer.data());
}

std::expected<void, SessionError>
WebSocketSession::WriteMessage(const std::string &text) {
  if (IsShutdownRequested()) {
    return std::unexpected(SessionError::kWriteFailed);
  }
  ArmWriteTimeout(operationTimeout_);
  socket_->stream_.text(true);
  auto ec = RunOperation(socket_->ioContext_, [this, &text](auto completion) {
    socket_->stream_.async_write(boost::asio::buffer(text),
                                 std::move(completion));
  });
  if (ec) {
    return std::unexpected(SessionError::kWriteFailed);
  }
  return {};
}

void WebSocketSession::Close() {
  if (IsShutdownRequested()) {
    return;
  }
  ArmWriteTimeout(security::kHandshakeTimeout);
  static_cast<void>(RunOperation(socket_->ioContext_, [this](auto completion) {
    socket_->stream_.async_close(boost::beast::websocket::close_code::normal,
                                 std::move(completion));
  }));
}

} // namespace dovahlink::transport
