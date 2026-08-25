#pragma once

#include <string_view>

namespace dovahlink::application {

/// Force-closes the one active connected session, when its transport can reach
/// it, after best- effort notifying it why. Trust administration owns *when* an
/// active session must be disconnected (a revoked, blocked, reset, or
/// factory-reset client credential, `ai/context/protocol/security.md`'s
/// "Administrative session invalidation"); this seam owns only how the
/// transport that holds that connection is reached, notified, and torn down,
/// mirroring `PairingNotificationSink`'s existing seam pattern.
class ActiveSessionDisconnector {
public:
  /// Releases the interface without performing work.
  virtual ~ActiveSessionDisconnector() = default;

  /// Force-closes the active session if the client identity bound to it is
  /// `clientId`, after best-effort sending it a `session_invalidated(reason)`
  /// event. A no-op when no session is active, the active session belongs to a
  /// different client, or the active session authenticated via the
  /// `one_time_local_token` developer-authentication provider -- a
  /// developer-token session is never treated as a Known Device
  /// (`ai/context/protocol/security.md`'s "Developer authentication"), even
  /// when its self-declared `clientId` happens to match `clientId` here.
  ///
  /// An implementation must resolve the target identity and capture the
  /// specific socket handle to close together under one critical section, then
  /// act only on that captured handle rather than re-resolving "whoever is
  /// active" once the section ends -- otherwise a session change landing
  /// between resolution and shutdown could close a different, newly-connected
  /// client's socket instead of the one actually identified. `BridgeWorkerPool`
  /// satisfies this by capturing the socket handle as a `shared_ptr` under
  /// `activeSocketMutex_` before releasing it, so a later reassignment of the
  /// active socket cannot redirect an already-captured shutdown.
  /// @param clientId The client identity that was just revoked.
  /// @param reason One of `"revoked"`, `"blocked"`, `"trust_reset"`,
  /// `"factory_reset"`.
  virtual void DisconnectIfClientActive(std::string_view clientId,
                                        std::string_view reason) = 0;

  /// Force-closes the active session unconditionally, regardless of which
  /// client it belongs to, after best-effort sending it a
  /// `session_invalidated(reason)` event. A no-op when no session is active.
  ///
  /// Known limitation: a connection accepted moments earlier but not yet past
  /// its own WebSocket handshake (still pre-`hello`) may not be reliably
  /// interrupted by one call alone -- unlike a full pool `Stop()`, this does
  /// not set a pool-wide stopping flag the accept loop's own lambda also checks
  /// as a second safety net once it publishes the socket, so a call landing in
  /// that narrow pre-publication window has nothing left to cancel. Accepted
  /// because this method's real caller (trust reset) targets an *authenticated*
  /// session, which is always long past this window by the time it is
  /// meaningfully "active"; a connection still mid-handshake at the exact
  /// moment of a reset just proceeds normally and is unaffected by it, not
  /// silently corrupted.
  /// @param reason One of `"revoked"`, `"blocked"`, `"trust_reset"`,
  /// `"factory_reset"`.
  virtual void DisconnectActive(std::string_view reason) = 0;
};

} // namespace dovahlink::application
