#include "application/session.hpp"

#include <utility>

namespace dovahlink::application {

SessionManager::Lease::Lease(SessionManager &manager, ConnectionId connection,
                             std::string sessionId) noexcept
    : manager_(&manager), connection_(connection),
      sessionId_(std::move(sessionId)) {}

SessionManager::Lease::Lease(Lease &&other) noexcept
    : manager_(other.manager_), connection_(other.connection_),
      sessionId_(std::move(other.sessionId_)) {
  other.manager_ = nullptr;
}

SessionManager::Lease &
SessionManager::Lease::operator=(Lease &&other) noexcept {
  if (this != &other) {
    Reset();
    manager_ = other.manager_;
    connection_ = other.connection_;
    sessionId_ = std::move(other.sessionId_);
    other.manager_ = nullptr;
  }
  return *this;
}

SessionManager::Lease::~Lease() { Reset(); }

void SessionManager::Lease::Reset() noexcept {
  if (manager_ != nullptr) {
    manager_->InvalidateSession(connection_, sessionId_);
    manager_ = nullptr;
  }
}

std::optional<SessionManager::Lease> SessionManager::TryCreateSession(
    ConnectionId connection, const std::string &sessionId, std::string clientId,
    SessionTrustTier trustTier, SessionAuthMethod authMethod) {
  std::lock_guard<std::mutex> lock(mutex_);
  if (activeSession_.has_value()) {
    return std::nullopt;
  }

  // Complete every fallible allocation before mutating registry state so
  // admission either returns an owning lease or changes nothing.
  std::string leaseSessionId = sessionId;
  activeSession_ = ActiveSession{
      .connectionId = connection,
      .sessionId = sessionId,
      .clientId = std::move(clientId),
      .trustTier = trustTier,
      .authMethod = authMethod,
  };
  return Lease(*this, connection, std::move(leaseSessionId));
}

bool SessionManager::IsValidForConnection(const std::string &sessionId,
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
                                        const std::string &sessionId) {
  std::lock_guard<std::mutex> lock(mutex_);
  if (activeSession_.has_value() &&
      activeSession_->connectionId == connection &&
      activeSession_->sessionId == sessionId) {
    activeSession_->trustTier = SessionTrustTier::kFull;
  }
}

void SessionManager::InvalidateSession(ConnectionId connection,
                                       const std::string &sessionId) noexcept {
  std::lock_guard<std::mutex> lock(mutex_);
  if (activeSession_.has_value() &&
      activeSession_->connectionId == connection &&
      activeSession_->sessionId == sessionId) {
    activeSession_.reset();
  }
}

void SessionManager::InvalidateAll() {
  std::lock_guard<std::mutex> lock(mutex_);
  activeSession_.reset();
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
  if (!activeSession_.has_value() ||
      activeSession_->connectionId != connection) {
    return std::nullopt;
  }
  return activeSession_;
}

} // namespace dovahlink::application
