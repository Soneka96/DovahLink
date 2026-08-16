#include "application/session.hpp"

#include <utility>

namespace dovahlink::application {

SessionManager::Lease::Lease(SessionManager& manager, ConnectionId connection,
                             std::string sessionId) noexcept
    : manager_(&manager), connection_(connection), sessionId_(std::move(sessionId)) {}

SessionManager::Lease::Lease(Lease&& other) noexcept
    : manager_(other.manager_), connection_(other.connection_), sessionId_(std::move(other.sessionId_)) {
    other.manager_ = nullptr;
}

SessionManager::Lease& SessionManager::Lease::operator=(Lease&& other) noexcept {
    if (this != &other) {
        Reset();
        manager_ = other.manager_;
        connection_ = other.connection_;
        sessionId_ = std::move(other.sessionId_);
        other.manager_ = nullptr;
    }
    return *this;
}

SessionManager::Lease::~Lease() {
    Reset();
}

void SessionManager::Lease::Reset() noexcept {
    if (manager_ != nullptr) {
        manager_->InvalidateSession(connection_, sessionId_);
        manager_ = nullptr;
    }
}

std::optional<SessionManager::Lease> SessionManager::TryCreateSession(
    ConnectionId connection, const std::string& sessionId, std::string clientId, SessionTrustTier trustTier) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (activeConnection_.has_value()) {
        return std::nullopt;
    }

    // Complete every fallible allocation before mutating registry state so
    // admission either returns an owning lease or changes nothing.
    std::string activeSessionId = sessionId;
    std::string leaseSessionId = sessionId;
    activeSessionId_.emplace(std::move(activeSessionId));
    activeClientId_.emplace(std::move(clientId));
    activeTrustTier_ = trustTier;
    activeConnection_ = connection;
    return Lease(*this, connection, std::move(leaseSessionId));
}

bool SessionManager::IsValidForConnection(const std::string& sessionId, ConnectionId connection) const {
    std::lock_guard<std::mutex> lock(mutex_);
    if (!activeConnection_.has_value() || !activeSessionId_.has_value()) {
        return false;
    }
    return *activeConnection_ == connection && *activeSessionId_ == sessionId;
}

std::optional<std::string> SessionManager::ClientIdForConnection(ConnectionId connection) const {
    std::lock_guard<std::mutex> lock(mutex_);
    if (!activeConnection_.has_value() || *activeConnection_ != connection) {
        return std::nullopt;
    }
    return activeClientId_;
}

bool SessionManager::IsFullyTrusted(ConnectionId connection) const {
    std::lock_guard<std::mutex> lock(mutex_);
    if (!activeConnection_.has_value() || *activeConnection_ != connection) {
        return false;
    }
    return activeTrustTier_ == SessionTrustTier::kFull;
}

void SessionManager::UpgradeToFullTrust(ConnectionId connection, const std::string& sessionId) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (activeConnection_.has_value() && activeSessionId_.has_value() && *activeConnection_ == connection &&
        *activeSessionId_ == sessionId) {
        activeTrustTier_ = SessionTrustTier::kFull;
    }
}

void SessionManager::InvalidateSession(ConnectionId connection, const std::string& sessionId) noexcept {
    std::lock_guard<std::mutex> lock(mutex_);
    if (activeConnection_.has_value() && activeSessionId_.has_value() &&
        *activeConnection_ == connection && *activeSessionId_ == sessionId) {
        activeConnection_.reset();
        activeSessionId_.reset();
        activeClientId_.reset();
        activeTrustTier_.reset();
    }
}

void SessionManager::InvalidateAll() {
    std::lock_guard<std::mutex> lock(mutex_);
    activeConnection_.reset();
    activeSessionId_.reset();
    activeClientId_.reset();
    activeTrustTier_.reset();
}

}  // namespace dovahlink::application
