#include "application/session_manager.hpp"

#include "security/constants.hpp"

#include <utility>

namespace dovahlink::application {

SessionManager::SessionManager(std::size_t maxConnectedClients)
    : maxConnectedClients_(maxConnectedClients) {}

std::optional<shared::ScopedRelease> SessionManager::TryCreateSession(
    ConnectionId connection, const std::string& sessionId, std::string clientId,
    SessionTrustTier trustTier, SessionAuthMethod authMethod) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (activeSessions_.size() >= maxConnectedClients_) {
        return std::nullopt;
    }
    for (const auto& [_, activeSession] : activeSessions_) {
        if (activeSession.sessionId == sessionId) {
            return std::nullopt;
        }
    }

    auto inserted = activeSessions_.emplace(connection, ActiveSession{
                                                            .connectionId = connection,
                                                            .sessionId = sessionId,
                                                            .clientId = std::move(clientId),
                                                            .trustTier = trustTier,
                                                            .authMethod = authMethod,
                                                        });
    if (!inserted.second) {
        return std::nullopt;
    }

    return shared::ScopedRelease(
        [this, connection, sessionId = std::move(sessionId)] {
            static_cast<void>(InvalidateSession(connection, sessionId));
        });
}

bool SessionManager::IsValidForConnection(const std::string& sessionId,
                                          ConnectionId connection) const {
    auto session = SessionForConnection(connection);
    return session.has_value() && session->sessionId == sessionId;
}

std::optional<std::string>
SessionManager::ClientIdForConnection(ConnectionId connection) const {
    auto session = SessionForConnection(connection);
    if (!session.has_value()) {
        return std::nullopt;
    }
    return session->clientId;
}

bool SessionManager::IsFullyTrusted(ConnectionId connection) const {
    auto session = SessionForConnection(connection);
    return session.has_value() && session->trustTier == SessionTrustTier::kFull;
}

void SessionManager::UpgradeToFullTrust(ConnectionId connection,
                                        const std::string& sessionId) {
    std::lock_guard<std::mutex> lock(mutex_);
    auto it = activeSessions_.find(connection);
    if (it != activeSessions_.end() && it->second.sessionId == sessionId) {
        it->second.trustTier = SessionTrustTier::kFull;
    }
}

bool SessionManager::InvalidateSession(ConnectionId connection,
                                       const std::string& sessionId) noexcept {
    std::lock_guard<std::mutex> lock(mutex_);
    auto it = activeSessions_.find(connection);
    if (it != activeSessions_.end() && it->second.sessionId == sessionId) {
        activeSessions_.erase(it);
        return true;
    }
    return false;
}

void SessionManager::InvalidateAll() {
    std::lock_guard<std::mutex> lock(mutex_);
    activeSessions_.clear();
}

std::optional<SessionAuthMethod>
SessionManager::AuthMethodForConnection(ConnectionId connection) const {
    auto session = SessionForConnection(connection);
    if (!session.has_value()) {
        return std::nullopt;
    }
    return session->authMethod;
}

std::optional<ActiveSession>
SessionManager::SessionForConnection(ConnectionId connection) const {
    std::lock_guard<std::mutex> lock(mutex_);
    auto it = activeSessions_.find(connection);
    if (it == activeSessions_.end()) {
        return std::nullopt;
    }
    return it->second;
}

} //  namespace dovahlink::application
