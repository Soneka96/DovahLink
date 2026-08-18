#include "application/connection_timeout_tracker.hpp"

#include "security/limits.hpp"

namespace dovahlink::application {

ConnectionTimeoutTracker::ConnectionTimeoutTracker(std::chrono::steady_clock::time_point now)
    : deadline_(now + security::kHandshakeTimeout) {}

void ConnectionTimeoutTracker::MarkAuthenticated(std::chrono::steady_clock::time_point now) {
    if (authenticated_ || IsTimedOut(now)) {
        return;
    }
    authenticated_ = true;
    deadline_ = now + security::kIdleTimeout;
}

void ConnectionTimeoutTracker::RecordActivity(std::chrono::steady_clock::time_point now) {
    if (!authenticated_ || IsTimedOut(now)) {
        return;
    }
    deadline_ = now + security::kIdleTimeout;
}

bool ConnectionTimeoutTracker::IsTimedOut(std::chrono::steady_clock::time_point now) const {
    return now >= deadline_;
}

std::chrono::steady_clock::time_point ConnectionTimeoutTracker::Deadline() const {
    return deadline_;
}

}  // namespace dovahlink::application
