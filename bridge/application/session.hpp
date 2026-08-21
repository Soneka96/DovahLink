#pragma once

#include "shared/enums.hpp"

#include <cstdint>
#include <mutex>
#include <optional>
#include <string>

namespace dovahlink::application {

/// Opaque identifier for one transport-level connection.
using ConnectionId = std::uint64_t;

/// Binds one authenticated session to one connection.
/// The manager is thread-safe and enforces the one-client limit.
class SessionManager {
public:
    /// Move-only ownership of one active authenticated session.
    ///
    /// The originating @ref SessionManager must outlive the lease.
    class Lease {
    public:
        /// Prevents two leases from owning the same active session.
        Lease(const Lease&) = delete;

        /// Prevents copying ownership of an active session.
        Lease& operator=(const Lease&) = delete;

        /// Transfers ownership of an active session.
        Lease(Lease&& other) noexcept;

        /// Releases any current session, then transfers ownership.
        Lease& operator=(Lease&& other) noexcept;

        /// Invalidates the owned session.
        ~Lease();

    private:
        friend class SessionManager;

        /// Creates a lease for a newly admitted session.
        Lease(SessionManager& manager, ConnectionId connection, std::string sessionId) noexcept;

        /// Invalidates the owned session, if any.
        void Reset() noexcept;

        /// Borrowed manager that owns the session registry.
        SessionManager* manager_;

        /// Connection whose session is invalidated on release.
        ConnectionId connection_;

        /// Session identifier that prevents stale leases invalidating replacements.
        std::string sessionId_;
    };

    /// Creates an empty session registry.
    SessionManager() = default;

    /// Attempts to create a session for a connection.
    /// @param connection Connection owning the proposed session.
    /// @param sessionId Fresh server-issued session identifier.
    /// @param clientId The authenticated client's identity, presented at `hello` and now owned by
    ///     this session for its lifetime.
    /// @param trustTier The session's initial message-type allowlist; defaults to `kFull` for
    ///     every caller that predates trust tiers (developer-token authentication).
    /// @param isDeveloperAuthenticated Whether the session was admitted via the `one_time_local_token`
    ///     developer-authentication provider rather than device pairing. Defaults to `false` for
    ///     every caller that predates this distinction. Never implies `trustTier`; a developer session
    ///     and a `trusted_device_credential` session are both `kFull` but must stay distinguishable so
    ///     Known Device administration (`ai/context/protocol/security.md`'s "Developer authentication")
    ///     can exempt developer sessions from clientId-scoped effects.
    /// @return An ownership lease when no active session existed.
    [[nodiscard]] std::optional<Lease> TryCreateSession(ConnectionId connection, const std::string& sessionId,
                                                        std::string clientId,
                                                        SessionTrustTier trustTier = SessionTrustTier::kFull,
                                                        bool isDeveloperAuthenticated = false);

    /// Checks whether a session belongs to a connection.
    /// @param sessionId Session identifier presented by the client.
    /// @param connection Connection presenting the identifier.
    /// @return `true` only for the active session and its owning connection.
    [[nodiscard]] bool IsValidForConnection(const std::string& sessionId, ConnectionId connection) const;

    /// Returns the authenticated client identity bound to a connection's active session. The
    /// single place the Bridge derives "which client is this" from -- callers must not accept a
    /// competing value from elsewhere (e.g. a repeated envelope field) once a session exists.
    /// @param connection Connection to query.
    /// @return The client identity, or no value when `connection` holds no active session.
    [[nodiscard]] std::optional<std::string> ClientIdForConnection(ConnectionId connection) const;

    /// Reports whether `connection` holds the active session and it is `kFull` tier.
    /// @param connection Connection to query.
    /// @return `false` for `kRestricted`, for a connection with no active session, and for a
    ///     connection that does not own the active session.
    [[nodiscard]] bool IsFullyTrusted(ConnectionId connection) const;

    /// Returns the authenticated client identity of the active session, independent of which
    /// connection owns it. Unlike @ref ClientIdForConnection, this does not require the caller to
    /// already know a `ConnectionId` -- needed by a caller (trust administration) that only knows a
    /// `clientId` and must find out whether it is the one currently connected.
    /// @return The active session's client identity, or no value when no session is active.
    [[nodiscard]] std::optional<std::string> ActiveClientId() const;

    /// Returns the identifier of the active session, independent of which connection owns it.
    /// The session-identity counterpart of @ref ActiveClientId, needed by a caller (administrative
    /// session invalidation) that must stamp the active session's own ID onto an outbound message
    /// without already knowing a `ConnectionId`.
    /// @return The active session's identifier, or no value when no session is active.
    [[nodiscard]] std::optional<std::string> ActiveSessionId() const;

    /// Upgrades the active session to `kFull` tier, if `connection` and `sessionId` both match the
    /// active session -- the same stale-caller guard `InvalidateSession` uses, so a delayed
    /// upgrade call arriving after `connection`'s session was invalidated and replaced cannot
    /// promote the unrelated replacement session. A no-op for a mismatched connection or session
    /// ID, and for a session already `kFull`.
    /// @param connection Connection whose session is upgraded.
    /// @param sessionId Session identifier the caller validated its request against.
    void UpgradeToFullTrust(ConnectionId connection, const std::string& sessionId);

    /// Invalidates the active session regardless of its connection.
    void InvalidateAll();

    /// Reports whether `connection` holds the active session and it was admitted via the
    /// `one_time_local_token` developer-authentication provider. Needed by trust administration
    /// (`ai/context/protocol/security.md`'s "Developer authentication") to exempt a developer session
    /// from clientId-scoped disconnection even when its self-declared clientId happens to match a
    /// Known Device being revoked or blocked.
    /// @param connection Connection to query.
    /// @return `false` for a connection with no active session, for a connection that does not own
    ///     the active session, and for an active session admitted by any other means.
    [[nodiscard]] bool IsDeveloperAuthenticated(ConnectionId connection) const;

private:
    /// Invalidates the active session when owned by `connection`.
    void InvalidateSession(ConnectionId connection, const std::string& sessionId) noexcept;

    /// Synchronizes session ownership state.
    mutable std::mutex mutex_;

    /// Connection currently holding the active session.
    std::optional<ConnectionId> activeConnection_;

    /// Identifier of the active session.
    std::optional<std::string> activeSessionId_;

    /// Authenticated client identity owned by the active session.
    std::optional<std::string> activeClientId_;

    /// Message-type allowlist of the active session.
    std::optional<SessionTrustTier> activeTrustTier_;

    /// Whether the active session was admitted via the `one_time_local_token` developer-
    /// authentication provider.
    std::optional<bool> activeIsDeveloperAuthenticated_;
};

}  // namespace dovahlink::application
