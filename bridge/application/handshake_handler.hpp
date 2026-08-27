#pragma once

#include "application/active_play_context_reader.hpp"
#include "application/connection_timeout_tracker.hpp"
#include "application/handshake_result.hpp"
#include "application/session.hpp"
#include "protocol/envelope.hpp"
#include "security/failed_token_throttle.hpp"
#include "security/token_store.hpp"
#include "security/trust_store.hpp"

#include <chrono>
#include <optional>
#include <string>
#include <string_view>

namespace dovahlink::application {

///  Validates and admits one client hello.
class IHandshakeHandler {
  public:
    ///  Releases the interface without performing work.
    virtual ~IHandshakeHandler() = default;

    ///  Validates one decoded hello and consumes the presented credential only
    ///  after session admission succeeds. Credentialed attempts reserve one
    ///  failed-auth slot before validation; invalid credentials commit that
    ///  slot, while successful or abandoned attempts release it. Successful
    ///  handshakes bind a new session to `connection`; failures close the
    ///  connection. Branches on `hello.auth.method`: `one_time_local_token`
    ///  (developer authentication, admits `kFull`) and
    ///  `trusted_device_credential` (a persisted pairing credential checked via
    ///  the trust store's `Authenticate`, admits `kFull`) both require a
    ///  matching secret; `unpaired` requires none and admits `kRestricted`.
    ///  `hello_ack.clientIdentityKind` is `"paired"` only for
    ///  `trusted_device_credential`; both other methods report `"unpaired"`,
    ///  per `security.md`'s "Hello authentication and session trust tiers".
    ///  @param helloEnvelope Decoded client hello envelope.
    ///  @param connection Transport connection identifier.
    ///  @param timeoutTracker Connection timeout tracker.
    ///  @param now Current monotonic time.
    ///  @return Response envelope and close decision for the connection.
    [[nodiscard]] virtual HandshakeResult
    Handle(const protocol::Envelope& helloEnvelope, ConnectionId connection,
           ConnectionTimeoutTracker& timeoutTracker,
           std::chrono::steady_clock::time_point now) = 0;
};

///  Binds handshake validation to its plugin-lifetime collaborators, per
///  `ai/context/skse/cpp-style.md`'s rule against a free function mixing
///  lifetime collaborators with per-call data.
class HandshakeHandler final : public IHandshakeHandler {
  public:
    ///  Binds every collaborator `Handle` needs.
    ///  @param tokenStore One-time token store, consulted for `auth.method:
    ///  "one_time_local_token"`.
    ///  @param tokenThrottle Global failed one-time-token attempt throttle; its
    ///      reservation is held through validation and session admission.
    ///  @param trustStore Persistent trust store, consulted for `auth.method:
    ///      "trusted_device_credential"`.
    ///  @param credentialThrottle Global failed device-credential attempt
    ///  throttle, separate from `tokenThrottle` so guessing one cannot block or
    ///      be blocked by the other; its reservation is held through validation
    ///      and session admission.
    ///  @param sessionManager Session registry.
    ///  @param activePlayContext Source of the play context active at connect
    ///  time, stamped onto the response's `playContextId`.
    ///  @param bridgeInstanceId This bridge process's identity, stamped onto
    ///  the response; no value if generation failed at startup.
    ///  @param bridgeVersion The DovahLink Bridge/mod release version exposed
    ///  to the client in `hello_ack.bridgeVersion` for its own compatibility
    ///      check (`ai/context/protocol/compatibility.md`).
    HandshakeHandler(security::ITokenStore& tokenStore,
                     security::IFailedTokenThrottle& tokenThrottle,
                     security::ITrustStore& trustStore,
                     security::IFailedTokenThrottle& credentialThrottle,
                     ISessionManager& sessionManager,
                     const IActivePlayContextReader& activePlayContext,
                     std::optional<std::string> bridgeInstanceId,
                     std::string bridgeVersion);

    ///  @copydoc IHandshakeHandler::Handle
    [[nodiscard]] HandshakeResult
    Handle(const protocol::Envelope& helloEnvelope, ConnectionId connection,
           ConnectionTimeoutTracker& timeoutTracker,
           std::chrono::steady_clock::time_point now) override;

  private:
    ///  One-time token store, consulted for `auth.method:
    ///  "one_time_local_token"`.
    security::ITokenStore& tokenStore_;

    ///  Global failed one-time-token attempt throttle.
    security::IFailedTokenThrottle& tokenThrottle_;

    ///  Persistent trust store, consulted for `auth.method:
    ///  "trusted_device_credential"`.
    security::ITrustStore& trustStore_;

    ///  Global failed device-credential attempt throttle.
    security::IFailedTokenThrottle& credentialThrottle_;

    ///  Session registry.
    ISessionManager& sessionManager_;

    ///  Source of the play context active at connect time.
    const IActivePlayContextReader& activePlayContext_;

    ///  This bridge process's identity, stamped onto every response.
    std::optional<std::string> bridgeInstanceId_;

    ///  The DovahLink Bridge/mod release version exposed to clients.
    std::string bridgeVersion_;
};

///  Reports why `clientId`'s trust, re-read from `trustStore` after session
///  admission, no longer permits the just-admitted `authMethod` -- or that it
///  still does. `HandshakeHandler::Handle` calls this immediately after
///  `SessionManager::TryCreateSession` succeeds, before declaring the handshake
///  successful -- not only via the earlier pre-admission trust checks -- because
///  a revoke or block that lands after those earlier checks but before admission
///  is otherwise invisible to
///  `ActiveSessionDisconnector::DisconnectIfClientActive`: that call is a
///  one-shot, and if it finds no session yet (because admission had not
///  happened), it is never retried. This is the last point that can still catch
///  it; the true concurrent interleaving is not exercised by a runtime test
///  here, since it is unreachable through this synchronous, single-threaded
///  function without real thread timing or a test-only hook -- closure instead
///  rests on `TrustStore`'s own mutex serializing every
///  `Revoke`/`Block`/`IsRevoked`/`IsBlocked` call against this one, combined
///  with `SessionManager` publishing the new session before this function
///  returns. Returns the wire reason directly, rather than a bare bool the
///  caller would have to re-derive by querying `trustStore` a second time -- a
///  second, separately-locked read could itself observe a different answer than
///  this one already decided on (`ai/context/common.md`'s "Domain modeling": one
///  decision needs one coherent read, not several independently synchronized
///  ones combined by the caller).
///  @param trustStore Persistent trust store, re-queried for its current state.
///  @param clientId The client identity just admitted.
///  @param authMethod How the session just admitted authenticated at `hello`.
///  @return `"blocked"` or `"revoked"` (`kTrustedDeviceCredential` only) when
///  `clientId` no longer
///      qualifies; `std::nullopt` when it still does. Always `std::nullopt` for
///      `kDeveloperToken` -- developer-token sessions are never Known Devices
///      (`ai/context/protocol/security.md`'s "Developer authentication").
[[nodiscard]] std::optional<std::string_view>
TrustLossAfterAdmission(security::ITrustStore& trustStore,
                        const std::string& clientId,
                        SessionAuthMethod authMethod);

} //  namespace dovahlink::application
