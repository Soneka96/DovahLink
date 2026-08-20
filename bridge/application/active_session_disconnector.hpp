#pragma once

#include <string_view>

namespace dovahlink::application {

/// Force-closes the one active connected session, when its transport can reach it, after best-
/// effort notifying it why. Trust administration owns *when* an active session must be
/// disconnected (a revoked, blocked, reset, or factory-reset client credential,
/// `ai/context/protocol/security.md`'s "Administrative session invalidation"); this seam owns only
/// how the transport that holds that connection is reached, notified, and torn down, mirroring
/// `PairingNotificationSink`'s existing seam pattern.
class ActiveSessionDisconnector {
public:
    /// Releases the interface without performing work.
    virtual ~ActiveSessionDisconnector() = default;

    /// Force-closes the active session if its authenticated client identity is `clientId`, after
    /// best-effort sending it a `session_invalidated(reason)` event. A no-op when no session is
    /// active or the active session belongs to a different client.
    ///
    /// ponytail: reads the active client identity and shuts down the active socket as two separate,
    /// unsynchronized steps; a session change landing in that narrow window could in principle shut
    /// down a different, newly-connected client's socket instead. Accepted for the same reason
    /// `TrustAdminService`'s own list-then-act race is: revocation is driven by one serialized admin
    /// operator, and the single-connected-client model means the window is only reachable by a full
    /// disconnect-reconnect-reauthenticate cycle completing inside it. Revisit alongside that
    /// service's own TOCTOU note if a second concurrent admin surface is ever introduced.
    /// @param clientId The client identity that was just revoked.
    /// @param reason One of `"revoked"`, `"blocked"`, `"trust_reset"`, `"factory_reset"`.
    virtual void DisconnectIfClientActive(std::string_view clientId, std::string_view reason) = 0;

    /// Force-closes the active session unconditionally, regardless of which client it belongs to,
    /// after best-effort sending it a `session_invalidated(reason)` event. A no-op when no session
    /// is active.
    ///
    /// ponytail: a connection accepted moments earlier but not yet past its own WebSocket handshake
    /// (still pre-`hello`) may not be reliably interrupted by one call alone -- unlike a full pool
    /// `Stop()`, this does not set a pool-wide stopping flag the accept loop's own lambda also
    /// checks as a second safety net once it publishes the socket, so a call landing in that narrow
    /// pre-publication window has nothing left to cancel. Accepted because this method's real
    /// caller (trust reset) targets an *authenticated* session, which is always long past this
    /// window by the time it is meaningfully "active"; a connection still mid-handshake at the
    /// exact moment of a reset just proceeds normally and is unaffected by it, not silently
    /// corrupted.
    /// @param reason One of `"revoked"`, `"blocked"`, `"trust_reset"`, `"factory_reset"`.
    virtual void DisconnectActive(std::string_view reason) = 0;
};

}  // namespace dovahlink::application
