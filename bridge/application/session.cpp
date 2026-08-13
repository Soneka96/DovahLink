#include "application/session.hpp"

namespace dovahlink::application {

bool SessionManager::TryCreateSession(ConnectionId connection, const std::string& sessionId) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (activeConnection_.has_value()) {
        return false;
    }
    activeConnection_ = connection;
    activeSessionId_ = sessionId;
    return true;
}

bool SessionManager::IsValidForConnection(const std::string& sessionId, ConnectionId connection) const {
    std::lock_guard<std::mutex> lock(mutex_);
    if (!activeConnection_.has_value() || !activeSessionId_.has_value()) {
        return false;
    }
    return *activeConnection_ == connection && *activeSessionId_ == sessionId;
}

void SessionManager::InvalidateSession(ConnectionId connection) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (activeConnection_.has_value() && *activeConnection_ == connection) {
        activeConnection_.reset();
        activeSessionId_.reset();
    }
}

void SessionManager::InvalidateAll() {
    std::lock_guard<std::mutex> lock(mutex_);
    activeConnection_.reset();
    activeSessionId_.reset();
}

}  // namespace dovahlink::application
