#include "application/active_session_socket.hpp"

#include <utility>

namespace dovahlink::application {

void ActiveSessionSocket::Publish(ConnectionId connection,
                                  std::shared_ptr<transport::ISocket> socket) {
    std::lock_guard<std::mutex> lock(mutex_);
    socket_ = std::move(socket);
    connection_ = connection;
}

std::optional<ActiveSessionSocketSnapshot> ActiveSessionSocket::Capture() const {
    std::lock_guard<std::mutex> lock(mutex_);
    auto socket = socket_.lock();
    if (!socket) {
        return std::nullopt;
    }
    return ActiveSessionSocketSnapshot{.connection = connection_,
                                       .socket = std::move(socket)};
}

void ActiveSessionSocket::Shutdown() noexcept {
    auto snapshot = Capture();
    if (snapshot.has_value()) {
        snapshot->socket->Shutdown();
    }
}

} //  namespace dovahlink::application
