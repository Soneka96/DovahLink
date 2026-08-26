#pragma once

#include "application/session.hpp"
#include "transport/websocket_session.hpp"

namespace dovahlink::application {

///  Identifies one published active socket without borrowing mutable registry state.
struct ActiveSessionSocketSnapshot {
    ///  Connection associated with the socket.
    ConnectionId connection;

    ///  Shared handle used for cross-thread socket shutdown.
    transport::WebSocketSession::SocketHandle socket;
};

} //  namespace dovahlink::application
