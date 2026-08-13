#pragma once

#include <boost/asio/io_context.hpp>
#include <boost/asio/ip/tcp.hpp>
#include <boost/beast/core/tcp_stream.hpp>
#include <boost/beast/websocket/stream.hpp>

#include <atomic>
#include <chrono>
#include <expected>
#include <memory>
#include <string>

namespace dovahlink::transport {

/// Identifies WebSocket session operation failures.
enum class SessionError {
    /// The WebSocket upgrade handshake failed.
    kHandshakeFailed,
    /// A message could not be read.
    kReadFailed,
    /// A message could not be written.
    kWriteFailed,
    /// A binary frame was received where text is required.
    kBinaryFrameRejected,
    /// The inbound frame exceeded the configured size limit and must be closed
    /// immediately rather than routed through post-decode violation handling.
    kFrameTooLarge,
};

/// Owns one accepted TCP connection and applies the Phase 1 WebSocket rules:
/// compression and WebSocket keep-alive pings are disabled, only text frames
/// are accepted, and handshake/idle I/O timeouts are enforced.
class WebSocketSession {
public:
    /// Owns the TCP socket shared between the worker and shutdown path.
    class Socket : public std::enable_shared_from_this<Socket> {
    public:
        /// Posts cancellation to the socket's event loop without touching Beast
        /// from the caller.
        void Shutdown() noexcept;

    private:
        friend class WebSocketSession;

        /// Takes ownership of one accepted TCP socket.
        explicit Socket(boost::asio::ip::tcp::socket socket);

        /// Event loop that owns every WebSocket operation and cancellation handler.
        boost::asio::io_context& ioContext_;

        /// Worker-owned WebSocket stream and its accepted TCP socket.
        boost::beast::websocket::stream<boost::beast::tcp_stream> stream_;

        /// Whether executor-owned cancellation has been requested.
        std::atomic<bool> shutdownRequested_{false};
    };

    /// Shared lifetime handle used to interrupt an active session safely.
    using SocketHandle = std::shared_ptr<Socket>;

    /// Takes ownership of the accepted TCP socket.
    explicit WebSocketSession(boost::asio::ip::tcp::socket socket);

    /// Uses a shared socket whose shutdown handle may be retained by the worker
    /// pool.
    /// @param socket Non-null socket handle that outlives the WebSocket stream.
    explicit WebSocketSession(SocketHandle socket);

    /// Creates a shared shutdown-capable handle for an accepted TCP socket.
    [[nodiscard]] static SocketHandle CreateSocket(boost::asio::ip::tcp::socket socket);

    /// Performs a compression-disabled, size-bounded WebSocket handshake.
    /// Starts the handshake timeout; callers must switch to the idle timeout
    /// after authentication succeeds.
    [[nodiscard]] std::expected<void, SessionError> Accept();

    /// Replaces the handshake timeout with the authenticated idle timeout.
    void SwitchToIdleTimeout();

    /// Reads one text message, rejecting binary frames and oversized frames.
    [[nodiscard]] std::expected<std::string, SessionError> ReadMessage();

    /// Writes one UTF-8 text message.
    [[nodiscard]] std::expected<void, SessionError> WriteMessage(const std::string& text);

    /// Closes the WebSocket with a normal close code.
    void Close();

private:
    /// Returns the referenced socket state or rejects an invalid shared handle.
    static Socket& RequireSocket(const SocketHandle& socket);

    /// Reports whether cancellation has disabled further session operations.
    [[nodiscard]] bool IsShutdownRequested() const noexcept;

    /// Updates the WebSocket inactivity policy for the current session phase.
    void SetTimeoutPolicy(std::chrono::steady_clock::duration timeout);

    /// Restarts the lower-layer deadline for one write or close operation.
    void ArmWriteTimeout(std::chrono::steady_clock::duration timeout);

    /// Disables lower-layer deadlines before a WebSocket-managed read.
    void DisableWriteTimeout();

    /// Shared owner of the worker-confined WebSocket state.
    SocketHandle socket_;

    /// Deadline applied to writes in the current session phase.
    std::chrono::steady_clock::duration operationTimeout_;
};

}  // namespace dovahlink::transport
