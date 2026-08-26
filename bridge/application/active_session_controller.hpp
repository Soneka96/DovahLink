#pragma once

#include "application/active_play_context_reader.hpp"
#include "application/active_session_socket.hpp"
#include "application/session.hpp"

#include <memory>
#include <mutex>
#include <optional>
#include <string>
#include <string_view>

namespace dovahlink::application {

///  Owns the active socket handle and coordinates its invalidation and shutdown.
class IActiveSessionController {
  public:
    ///  Releases the interface without performing work.
    virtual ~IActiveSessionController() = default;

    ///  Invalidates and disconnects the active session for a matching client.
    virtual void DisconnectIfClientActive(std::string_view clientId,
                                          std::string_view reason) = 0;

    ///  Invalidates and disconnects the active session unconditionally.
    virtual void DisconnectActive(std::string_view reason) = 0;
};

///  Coordinates active-session socket lifetime with session invalidation.
class ActiveSessionController final : public IActiveSessionController {
  public:
    ///  Binds session identity, context metadata, and active-socket ownership.
    ActiveSessionController(SessionManager& sessionManager,
                            IActiveSessionSocket& activeSessionSocket,
                            const IActivePlayContextReader& activePlayContext,
                            std::optional<std::string> bridgeInstanceId);

    ///  @copydoc IActiveSessionController::DisconnectIfClientActive
    void DisconnectIfClientActive(std::string_view clientId,
                                  std::string_view reason) override;

    ///  @copydoc IActiveSessionController::DisconnectActive
    void DisconnectActive(std::string_view reason) override;

  private:
    ///  Sends an invalidation event when possible, then shuts down the socket.
    void NotifyAndShutdownActiveSocket(
        const transport::WebSocketSession::SocketHandle& socket,
        std::optional<std::string> sessionId, std::string_view reason);

    ///  Session registry used to resolve and invalidate the captured connection.
    SessionManager& sessionManager_;

    ///  Socket lifecycle boundary shared with the worker pool.
    IActiveSessionSocket& activeSessionSocket_;

    ///  Context identity stamped on invalidation messages.
    const IActivePlayContextReader& activePlayContext_;

    ///  Bridge identity stamped on invalidation messages.
    std::optional<std::string> bridgeInstanceId_;
};

} //  namespace dovahlink::application
