#pragma once

#include "application/session_manager.hpp"
#include "transport/websocket_session.hpp"

#include <memory>

namespace dovahlink::application {

///  Identifies one published active socket without borrowing mutable registry state.
struct ActiveSessionSocketSnapshot {
    ///  Connection associated with the socket.
    ConnectionId connection;

    ///  Shared handle used for cross-thread socket shutdown.
    std::shared_ptr<transport::ISocket> socket;
};

} //  namespace dovahlink::application
