#pragma once

#include "application/active_session.hpp"
#include "shared/enums.hpp"
#include "shared/scoped_release.hpp"

#include <mutex>
#include <optional>
#include <string>

namespace dovahlink::application {

///  Owns one authenticated session bound to one connection: admission,
///  validation, trust-tier promotion, and invalidation. The narrow capability
///  slice each consumer actually calls -- `HandshakeHandler` admits sessions,
///  `MessageDispatcher` validates them, `ActiveSessionController` and
///  administrative trust mutations query and invalidate them,
///  `TrustMutationCoordinator` promotes them -- together cover this
///  interface's full surface, so it mirrors `SessionManager`'s public API
///  rather than a narrower slice.
class ISessionManager {
  public:
    ///  Releases the interface without performing work.
    virtual ~ISessionManager() = default;

    ///  Attempts to create a session for a connection.
    ///  @param connection Connection owning the proposed session.
    ///  @param sessionId Fresh server-issued session identifier.
    ///  @param clientId The client identity presented at `hello` and now owned by
    ///  this session for
    ///      its lifetime.
    ///  @param trustTier The session's initial message-type allowlist. Required,
    ///  not defaulted: the
    ///      caller must state it explicitly rather than a convenience default
    ///      silently selecting it
    ///      (`ai/context/common.md`'s "Domain modeling").
    ///  @param authMethod How the session authenticated at `hello`. Required, not
    ///  defaulted, for the
    ///      same reason as `trustTier`. Never implies `trustTier`: a
    ///      `kDeveloperToken` session and a `kTrustedDeviceCredential` session are
    ///      both `kFull` but must stay distinguishable so Known Device
    ///      administration (`ai/context/protocol/security.md`'s "Developer
    ///      authentication") can exempt developer sessions from clientId-scoped
    ///      effects.
    ///  @return A release that invalidates this session when destroyed or
    ///  triggered, or no value when no session existed.
    [[nodiscard]] virtual std::optional<shared::ScopedRelease>
    TryCreateSession(ConnectionId connection, const std::string& sessionId,
                     std::string clientId, SessionTrustTier trustTier,
                     SessionAuthMethod authMethod) = 0;

    ///  Checks whether a session belongs to a connection.
    ///  @param sessionId Session identifier presented by the client.
    ///  @param connection Connection presenting the identifier.
    ///  @return `true` only for the active session and its owning connection.
    [[nodiscard]] virtual bool
    IsValidForConnection(const std::string& sessionId,
                         ConnectionId connection) const = 0;

    ///  Returns the client identity bound to a connection's active session. The
    ///  single place the Bridge derives "which client is this" from -- callers
    ///  must not accept a competing value from elsewhere (e.g. a repeated envelope
    ///  field) once a session exists.
    ///  @param connection Connection to query.
    ///  @return The client identity, or no value when `connection` holds no active
    ///  session.
    [[nodiscard]] virtual std::optional<std::string>
    ClientIdForConnection(ConnectionId connection) const = 0;

    ///  Reports whether `connection` holds the active session and it is `kFull`
    ///  tier.
    ///  @param connection Connection to query.
    ///  @return `false` for `kRestricted`, for a connection with no active
    ///  session, and for a
    ///      connection that does not own the active session.
    [[nodiscard]] virtual bool IsFullyTrusted(ConnectionId connection) const = 0;

    ///  Upgrades the active session to `kFull` tier, if `connection` and
    ///  `sessionId` both match the active session -- the same stale-caller guard
    ///  `InvalidateSession` uses, so a delayed upgrade call arriving after
    ///  `connection`'s session was invalidated and replaced cannot promote the
    ///  unrelated replacement session. A no-op for a mismatched connection or
    ///  session ID, and for a session already `kFull`.
    ///  @param connection Connection whose session is upgraded.
    ///  @param sessionId Session identifier the caller validated its request
    ///  against.
    virtual void UpgradeToFullTrust(ConnectionId connection,
                                    const std::string& sessionId) = 0;

    ///  Invalidates the active session regardless of its connection.
    virtual void InvalidateAll() = 0;

    ///  Invalidates the active session only when both identity values match.
    ///  @param connection Connection owning the session to invalidate.
    ///  @param sessionId Exact session identifier to invalidate.
    ///  @return `true` when the matching session was invalidated.
    [[nodiscard]] virtual bool
    InvalidateSession(ConnectionId connection,
                      const std::string& sessionId) noexcept = 0;

    ///  Returns how `connection`'s active session authenticated at `hello`. Needed
    ///  by trust administration (`ai/context/protocol/security.md`'s "Developer
    ///  authentication") to exempt a `kDeveloperToken` session from
    ///  clientId-scoped disconnection even when its self-declared clientId happens
    ///  to match a Known Device being revoked or blocked.
    ///  @param connection Connection to query.
    ///  @return The active session's auth method, or no value when `connection`
    ///  holds no active
    ///      session.
    [[nodiscard]] virtual std::optional<SessionAuthMethod>
    AuthMethodForConnection(ConnectionId connection) const = 0;

    ///  Returns a complete, coherent snapshot of `connection`'s active session in
    ///  one locked read. The single query administrative invalidation (targeted
    ///  Revoke/Block/Reset Trust disconnection) uses instead of separately calling
    ///  `ClientIdForConnection` and `AuthMethodForConnection` in sequence: each
    ///  narrow accessor is its own lock acquisition, so nothing outside this
    ///  class's own mutex would hold `activeSession_` stable across them, and a
    ///  caller that needs more than one field together must not reconstruct one
    ///  from multiple independent reads. `IsValidForConnection`,
    ///  `ClientIdForConnection`, `IsFullyTrusted`, and `AuthMethodForConnection`
    ///  are themselves implemented on top of this.
    ///  @param connection Connection to query.
    ///  @return A copy of the active session's complete state, or no value when
    ///  `connection` holds no
    ///      active session.
    [[nodiscard]] virtual std::optional<ActiveSession>
    SessionForConnection(ConnectionId connection) const = 0;
};

///  Binds one authenticated session to one connection.
///  The manager is thread-safe and enforces the one-client limit.
class SessionManager : public ISessionManager {
  public:
    ///  Creates an empty session registry.
    SessionManager() = default;

    ///  @copydoc ISessionManager::TryCreateSession
    [[nodiscard]] std::optional<shared::ScopedRelease>
    TryCreateSession(ConnectionId connection, const std::string& sessionId,
                     std::string clientId, SessionTrustTier trustTier,
                     SessionAuthMethod authMethod) override;

    ///  @copydoc ISessionManager::IsValidForConnection
    [[nodiscard]] bool IsValidForConnection(const std::string& sessionId,
                                            ConnectionId connection) const override;

    ///  @copydoc ISessionManager::ClientIdForConnection
    [[nodiscard]] std::optional<std::string>
    ClientIdForConnection(ConnectionId connection) const override;

    ///  @copydoc ISessionManager::IsFullyTrusted
    [[nodiscard]] bool IsFullyTrusted(ConnectionId connection) const override;

    ///  @copydoc ISessionManager::UpgradeToFullTrust
    void UpgradeToFullTrust(ConnectionId connection,
                            const std::string& sessionId) override;

    ///  @copydoc ISessionManager::InvalidateAll
    void InvalidateAll() override;

    ///  @copydoc ISessionManager::InvalidateSession
    [[nodiscard]] bool InvalidateSession(
        ConnectionId connection, const std::string& sessionId) noexcept override;

    ///  @copydoc ISessionManager::AuthMethodForConnection
    [[nodiscard]] std::optional<SessionAuthMethod>
    AuthMethodForConnection(ConnectionId connection) const override;

    ///  @copydoc ISessionManager::SessionForConnection
    [[nodiscard]] std::optional<ActiveSession>
    SessionForConnection(ConnectionId connection) const override;

  private:
    ///  Synchronizes session ownership state.
    mutable std::mutex mutex_;

    ///  The active session's complete state, or no value when no session is
    ///  active. Every related field lives in this one optional so a session either
    ///  exists as one coherent record or does not exist -- never a state where one
    ///  field is populated while a related one is absent.
    std::optional<ActiveSession> activeSession_;
};

} //  namespace dovahlink::application
