#pragma once

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
    /// Creates an empty session registry.
    SessionManager() = default;

    /// Attempts to create a session for a connection.
    /// @param connection Connection owning the proposed session.
    /// @param sessionId Fresh server-issued session identifier.
    /// @return `true` when no active session existed and the session was created.
    [[nodiscard]] bool TryCreateSession(ConnectionId connection, const std::string& sessionId);

    /// Checks whether a session belongs to a connection.
    /// @param sessionId Session identifier presented by the client.
    /// @param connection Connection presenting the identifier.
    /// @return `true` only for the active session and its owning connection.
    [[nodiscard]] bool IsValidForConnection(const std::string& sessionId, ConnectionId connection) const;

    /// Invalidates the active session when owned by `connection`.
    /// @param connection Connection whose session should be invalidated.
    void InvalidateSession(ConnectionId connection);

    /// Invalidates the active session regardless of its connection.
    void InvalidateAll();

private:
    /// Synchronizes session ownership state.
    mutable std::mutex mutex_;

    /// Connection currently holding the active session.
    std::optional<ConnectionId> activeConnection_;

    /// Identifier of the active session.
    std::optional<std::string> activeSessionId_;
};

}  // namespace dovahlink::application
