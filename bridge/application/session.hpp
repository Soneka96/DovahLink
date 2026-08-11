#pragma once

#include <cstdint>
#include <mutex>
#include <optional>
#include <string>

namespace dovahlink::application {

// Opaque identifier for one transport-level connection (e.g. one accepted
// socket), assigned by the transport/coordinator layer. Only compared for
// equality here; this layer never interprets its value.
using ConnectionId = std::uint64_t;

// Binds one authenticated session to the socket that created it, enforcing
// the Phase 1 one-connected-client limit. See the session and recovery rules
// in protocol/schema/README.md and ai/context/protocol/security.md.
// Thread-safe.
class SessionManager {
public:
    SessionManager() = default;

    // Attempts to create a new session bound to `connection`. Fails (returns
    // false) if a session is already active for any connection, enforcing
    // the one-connected-client limit; an active session is never replaced.
    // `sessionId` must already be a cryptographically random, freshly
    // generated identifier (see bridge/security/csprng.hpp); this type does
    // not generate it.
    [[nodiscard]] bool TryCreateSession(ConnectionId connection, const std::string& sessionId);

    // True if `sessionId` names the currently active session and it is bound
    // to `connection`. False for no active session, an unrecognized ID, or a
    // foreign connection presenting the active session's ID -- the caller
    // maps that last case to the wire error code `stale_session`.
    [[nodiscard]] bool IsValidForConnection(const std::string& sessionId, ConnectionId connection) const;

    // Invalidates the active session if it is bound to `connection`; a no-op
    // if `connection` does not hold the active session (it cannot invalidate
    // a session it does not own). Called on disconnect, idle timeout, or
    // protocol-violation closure for that connection.
    void InvalidateSession(ConnectionId connection);

    // Unconditionally invalidates the active session, regardless of which
    // connection holds it. Called on bridge shutdown.
    void InvalidateAll();

private:
    mutable std::mutex mutex_;
    std::optional<ConnectionId> activeConnection_;
    std::optional<std::string> activeSessionId_;
};

}  // namespace dovahlink::application
