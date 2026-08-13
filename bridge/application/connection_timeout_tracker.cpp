#include "application/connection_timeout_tracker.hpp"

#include "security/limits.hpp"

namespace dovahlink::application {

/**
     * @brief Initializes the connection's handshake deadline.
     *
     * @param now Current monotonic time used as the deadline's starting point.
     */
    ConnectionTimeoutTracker::ConnectionTimeoutTracker(std::chrono::steady_clock::time_point now)
    : deadline_(now + security::kHandshakeTimeout) {}

/**
 * @brief Marks the connection as authenticated and starts its idle timeout.
 *
 * Authentication is ignored if the connection is already authenticated or has
 * timed out.
 *
 * @param now Current time used to evaluate the deadline and set the idle deadline.
 */
void ConnectionTimeoutTracker::MarkAuthenticated(std::chrono::steady_clock::time_point now) {
    if (authenticated_ || IsTimedOut(now)) {
        return;
    }
    authenticated_ = true;
    deadline_ = now + security::kIdleTimeout;
}

/**
 * @brief Refreshes the idle timeout after connection activity.
 *
 * @param now Current time used to calculate the new idle deadline.
 */
void ConnectionTimeoutTracker::RecordActivity(std::chrono::steady_clock::time_point now) {
    if (!authenticated_ || IsTimedOut(now)) {
        return;
    }
    deadline_ = now + security::kIdleTimeout;
}

/**
 * @brief Determines whether the connection deadline has elapsed.
 *
 * @param now Current time used for the deadline comparison.
 * @return true if the deadline has been reached or passed, false otherwise.
 */
bool ConnectionTimeoutTracker::IsTimedOut(std::chrono::steady_clock::time_point now) const {
    return now >= deadline_;
}

}  // namespace dovahlink::application
