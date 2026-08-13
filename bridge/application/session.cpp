#include "application/session.hpp"

namespace dovahlink::application {

/**
 * @brief Creates an active session when no connection is currently active.
 *
 * @param connection Connection identifier for the new session.
 * @param sessionId Identifier for the new session.
 * @return true if the session was created, false if a connection is already active.
 */
bool SessionManager::TryCreateSession(ConnectionId connection, const std::string& sessionId) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (activeConnection_.has_value()) {
        return false;
    }
    activeConnection_ = connection;
    activeSessionId_ = sessionId;
    return true;
}

/**
 * @brief Determines whether the session matches the active connection.
 *
 * @param sessionId Session identifier to validate.
 * @param connection Connection identifier to validate.
 * @return true if both identifiers match the active session, false otherwise.
 */
bool SessionManager::IsValidForConnection(const std::string& sessionId, ConnectionId connection) const {
    std::lock_guard<std::mutex> lock(mutex_);
    if (!activeConnection_.has_value() || !activeSessionId_.has_value()) {
        return false;
    }
    return *activeConnection_ == connection && *activeSessionId_ == sessionId;
}

/**
 * @brief Invalidates the active session for a matching connection.
 *
 * @param connection Connection whose session should be invalidated.
 */
void SessionManager::InvalidateSession(ConnectionId connection) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (activeConnection_.has_value() && *activeConnection_ == connection) {
        activeConnection_.reset();
        activeSessionId_.reset();
    }
}

/**
 * @brief Clears the active connection and session.
 */
void SessionManager::InvalidateAll() {
    std::lock_guard<std::mutex> lock(mutex_);
    activeConnection_.reset();
    activeSessionId_.reset();
}

}  // namespace dovahlink::application
