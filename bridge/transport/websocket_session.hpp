#pragma once

#include "shared/enums.hpp"

#include <boost/asio/io_context.hpp>
#include <boost/asio/ip/tcp.hpp>
#include <boost/beast/core/tcp_stream.hpp>
#include <boost/beast/websocket/stream.hpp>

#include <atomic>
#include <chrono>
#include <expected>
#include <functional>
#include <memory>
#include <optional>
#include <string>

namespace dovahlink::transport {

///  The synchronous per-connection WebSocket capability consumed by session
///  orchestration: accept the handshake, switch to the authenticated idle
///  timeout, exchange text messages, and close.
class IWebSocketSession {
  public:
    ///  Releases the interface without performing work.
    virtual ~IWebSocketSession() = default;

    ///  Performs a compression-disabled, size-bounded WebSocket handshake.
    ///  Starts the handshake timeout; callers must switch to the idle timeout
    ///  after authentication succeeds.
    [[nodiscard]] virtual std::expected<void, SessionError> Accept() = 0;

    ///  Replaces the handshake timeout with the authenticated idle timeout.
    virtual void SwitchToIdleTimeout() = 0;

    ///  Reads one text message, rejecting binary frames and oversized frames.
    ///  @param idleDeadline When supplied, arms a watchdog that closes the
    ///  connection if this read
    ///      does not complete by this absolute time, independent of Beast's own
    ///      per-operation timeout -- needed because WebSocket-level keep-alive
    ///      pings can otherwise keep that timeout from ever firing against a peer
    ///      that answers pings but sends no application message. Canceled the
    ///      instant this read completes, successfully or not. Omitted, this
    ///      behaves exactly as before.
    [[nodiscard]] virtual std::expected<std::string, SessionError> ReadMessage(
        std::optional<std::chrono::steady_clock::time_point> idleDeadline =
            std::nullopt) = 0;

    ///  Writes one UTF-8 text message.
    [[nodiscard]] virtual std::expected<void, SessionError>
    WriteMessage(const std::string& text) = 0;

    ///  Closes the WebSocket with a normal close code.
    virtual void Close() = 0;
};

///  Cross-thread shutdown capability for one accepted socket: lets an external
///  collaborator cancel or notify-then-cancel the connection without
///  depending on `WebSocketSession::Socket`'s internal Beast/asio state.
class ISocket {
  public:
    ///  Releases the interface without performing work.
    virtual ~ISocket() = default;

    ///  Posts cancellation to the socket's event loop without touching Beast
    ///  from the caller.
    virtual void Shutdown() noexcept = 0;

    ///  Best-effort sends `message` as one text frame, then shuts down exactly
    ///  like `Shutdown()` -- one coupled, cross-thread-safe operation for an
    ///  administrative terminal event (`ai/context/protocol/security.md`'s
    ///  "Administrative session invalidation": "best-effort send and flush...
    ///  then force-close"). Shuts down only once the write's own completion
    ///  handler fires (success or failure), so shutdown never races ahead of a
    ///  write still in flight; whether the peer actually receives it beyond that
    ///  is still never awaited, acknowledged, or guaranteed. Shares
    ///  `Shutdown()`'s single-fire guard, so whichever of the two is called
    ///  first wins and the other becomes a no-op. If the WebSocket upgrade
    ///  handshake has not yet finished using `stream_`, the notification is
    ///  skipped entirely (best-effort delivery permits this) and the connection
    ///  is still force-closed exactly like `Shutdown()` -- writing a
    ///  notification concurrently with `Accept()`'s own in-flight handshake
    ///  operation would race the same stream unsafely.
    ///  @param message Pre-encoded UTF-8 text to send before shutting down.
    virtual void ShutdownWithNotification(std::string message) noexcept = 0;

    ///  Best-effort sends `message` as one text frame, cross-thread-safe like
    ///  `ShutdownWithNotification` but, unlike it and `Shutdown()`, repeatable
    ///  over the session's whole life rather than single-fire: intended for a
    ///  collaborator that pushes more than one unsolicited message across the
    ///  connection's life (for example a bounded outbound queue). The caller
    ///  must keep at most one `Send` in flight at a time -- Beast permits only
    ///  one outstanding write per stream -- by waiting for `onComplete` before
    ///  calling again; this method does not queue or serialize overlapping
    ///  calls itself. `onComplete` is invoked exactly once, with `true` once
    ///  the write completes successfully, or `false` if the socket has
    ///  already been shut down, the handshake has not yet settled, or the
    ///  write itself fails or times out. Unlike `Shutdown()` and
    ///  `ShutdownWithNotification()`, a failed or timed-out `Send` does not
    ///  close the connection by itself; a caller that requires the
    ///  connection to end on `onComplete(false)` must call `Shutdown()`
    ///  explicitly.
    ///  @param message Pre-encoded UTF-8 text to send.
    ///  @param onComplete Invoked exactly once with the outcome.
    virtual void Send(std::string message,
                      std::function<void(bool)> onComplete) noexcept = 0;
};

///  Owns one accepted TCP connection and applies the Phase 1 WebSocket rules:
///  compression and WebSocket keep-alive pings are disabled, only text frames
///  are accepted, and handshake/idle I/O timeouts are enforced.
class WebSocketSession final : public IWebSocketSession {
  public:
    ///  Owns the TCP socket shared between the worker and shutdown path.
    ///  Declared here rather than in its own file: every method beyond
    ///  `ISocket`'s two is private and reached only through `friend class
    ///  WebSocketSession`, which manipulates `stream_`/`ioContext_` directly
    ///  as an extension of its own state (see websocket_session.cpp) -- the
    ///  genuine structural-coupling carve-out `ai/context/skse/cpp-style.md`
    ///  documents, not a convenience nesting.
    class Socket : public ISocket,
                   public std::enable_shared_from_this<Socket> {
      public:
        ///  @copydoc ISocket::Shutdown
        void Shutdown() noexcept override;

        ///  @copydoc ISocket::ShutdownWithNotification
        void ShutdownWithNotification(std::string message) noexcept override;

        ///  @copydoc ISocket::Send
        void Send(std::string message,
                  std::function<void(bool)> onComplete) noexcept override;

      private:
        friend class WebSocketSession;

        ///  Transfers an incoming TCP socket onto the session-owned context.
        static boost::asio::ip::tcp::socket RebindSocket(
            boost::asio::ip::tcp::socket socket,
            boost::asio::io_context& ioContext);

        ///  Takes ownership of one accepted TCP socket.
        explicit Socket(boost::asio::ip::tcp::socket socket);

        ///  Event loop that owns every WebSocket operation and cancellation handler.
        std::shared_ptr<boost::asio::io_context> ioContext_;

        ///  Worker-owned WebSocket stream and its accepted TCP socket.
        boost::beast::websocket::stream<boost::beast::tcp_stream> stream_;

        ///  Whether executor-owned cancellation has been requested.
        std::atomic<bool> shutdownRequested_{false};

        ///  Whether the WebSocket upgrade handshake has finished issuing its own
        ///  operations on `stream_` (successfully or not).
        ///  `ShutdownWithNotification` must not start a websocket-level write until
        ///  this is true.
        std::atomic<bool> handshakeSettled_{false};
    };

    ///  Shared lifetime handle used to interrupt an active session safely.
    using SocketHandle = std::shared_ptr<Socket>;

    ///  Takes ownership of the accepted TCP socket.
    explicit WebSocketSession(boost::asio::ip::tcp::socket socket);

    ///  Uses a shared socket whose shutdown handle may be retained by the worker
    ///  pool.
    ///  @param socket Non-null socket handle that outlives the WebSocket stream.
    explicit WebSocketSession(SocketHandle socket);

    ///  Creates a shared shutdown-capable handle for an accepted TCP socket.
    [[nodiscard]] static SocketHandle
    CreateSocket(boost::asio::ip::tcp::socket socket);

    ///  @copydoc IWebSocketSession::Accept
    [[nodiscard]] std::expected<void, SessionError> Accept() override;

    ///  @copydoc IWebSocketSession::SwitchToIdleTimeout
    void SwitchToIdleTimeout() override;

    ///  @copydoc IWebSocketSession::ReadMessage
    ///  Repeats the base interface's default so existing callers that hold a
    ///  concrete `WebSocketSession&` (rather than an `IWebSocketSession&`) can
    ///  still omit `idleDeadline`; default arguments resolve by the static
    ///  type of the call expression, not the dynamic type, so the interface's
    ///  own default does not apply through a concrete-typed reference.
    [[nodiscard]] std::expected<std::string, SessionError>
    ReadMessage(std::optional<std::chrono::steady_clock::time_point>
                    idleDeadline = std::nullopt) override;

    ///  @copydoc IWebSocketSession::WriteMessage
    [[nodiscard]] std::expected<void, SessionError>
    WriteMessage(const std::string& text) override;

    ///  @copydoc IWebSocketSession::Close
    void Close() override;

  private:
    ///  Returns the referenced socket state or rejects an invalid shared handle.
    static Socket& RequireSocket(const SocketHandle& socket);

    ///  Reports whether cancellation has disabled further session operations.
    [[nodiscard]] bool IsShutdownRequested() const noexcept;

    ///  Updates the WebSocket inactivity policy for the current session phase.
    void SetTimeoutPolicy(std::chrono::steady_clock::duration timeout);

    ///  Restarts the lower-layer deadline for one write or close operation.
    void ArmWriteTimeout(std::chrono::steady_clock::duration timeout);

    ///  Disables lower-layer deadlines before a WebSocket-managed read.
    void DisableWriteTimeout();

    ///  Shared owner of the worker-confined WebSocket state.
    SocketHandle socket_;

    ///  Deadline applied to writes in the current session phase.
    std::chrono::steady_clock::duration operationTimeout_;
};

} //  namespace dovahlink::transport
