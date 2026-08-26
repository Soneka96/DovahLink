#pragma once

#include "application/active_session_socket_snapshot.hpp"

#include <memory>
#include <mutex>
#include <optional>

namespace dovahlink::application {

///  Owns publication and shutdown of the one active session socket.
class IActiveSessionSocket {
  public:
    ///  Releases the interface without performing work.
    virtual ~IActiveSessionSocket() = default;

    ///  Publishes a socket for one transport connection.
    virtual void Publish(ConnectionId connection,
                         transport::WebSocketSession::SocketHandle socket) = 0;

    ///  Returns the current socket and connection as one coherent snapshot.
    [[nodiscard]] virtual std::optional<ActiveSessionSocketSnapshot>
    Capture() const = 0;

    ///  Shuts down the currently published socket without a notification.
    virtual void Shutdown() noexcept = 0;
};

///  Thread-safe active socket registry shared by worker and trust boundaries.
class ActiveSessionSocket final : public IActiveSessionSocket {
  public:
    ///  Creates an empty active-socket registry.
    ActiveSessionSocket() = default;

    ///  @copydoc IActiveSessionSocket::Publish
    void Publish(ConnectionId connection,
                 transport::WebSocketSession::SocketHandle socket) override;

    ///  @copydoc IActiveSessionSocket::Capture
    [[nodiscard]] std::optional<ActiveSessionSocketSnapshot>
    Capture() const override;

    ///  @copydoc IActiveSessionSocket::Shutdown
    void Shutdown() noexcept override;

  private:
    ///  Serializes publication and snapshot capture.
    mutable std::mutex mutex_;

    ///  Non-owning handle to the active socket.
    std::weak_ptr<transport::WebSocketSession::Socket> socket_;

    ///  Connection associated with `socket_`.
    ConnectionId connection_{};
};

} //  namespace dovahlink::application
