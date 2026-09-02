#pragma once

#include "application/active_play_context_reader.hpp"
#include "application/handshake_handler.hpp"
#include "application/message_dispatcher.hpp"
#include "application/session_manager.hpp"
#include "application/session_release_notification_sink.hpp"
#include "security/pairing_session.hpp"
#include "transport/websocket_session.hpp"

#include <chrono>
#include <functional>
#include <optional>
#include <string>

namespace dovahlink::application {

///  Supplies monotonic timestamps to one connection session.
using SteadyNowProvider =
    std::function<std::chrono::steady_clock::time_point()>;

///  Runs one accepted connection through authentication, message handling, and
///  cleanup.
class IConnectionSession {
  public:
    ///  Releases the interface without performing work.
    virtual ~IConnectionSession() = default;

    ///  Runs `ws` through authentication, message handling, and cleanup.
    ///  Session ownership is scope-bound so every exit after authentication
    ///  invalidates it. Notifies the bound pairing session of this
    ///  connection's own `clientId` reconnecting (right after a successful
    ///  hello) and disconnecting (at the same scope-bound teardown point), so
    ///  a challenge or pending credential it owns can apply the
    ///  reconnect-grace lazy-expiry rules in
    ///  `ai/context/protocol/security.md`'s Phase 3.1 ownership model. A
    ///  harmless no-op when `clientId` owns nothing.
    ///  @param ws Accepted WebSocket session, consumed through its
    ///  `IWebSocketSession` contract.
    ///  @param connection Transport connection identifier.
    ///  @param steadyNow Supplies current monotonic time; injectable for
    ///  deterministic timeout tests.
    virtual void Run(transport::IWebSocketSession& ws, ConnectionId connection, SteadyNowProvider steadyNow = [] { return std::chrono::steady_clock::now(); }) = 0;
};

///  Binds one accepted connection's full session lifecycle to its
///  plugin-lifetime collaborators, per `ai/context/skse/cpp-style.md`'s rule
///  against a free function mixing lifetime collaborators with per-call data.
class ConnectionSession final : public IConnectionSession {
  public:
    ///  Binds every collaborator `Run` needs.
    ///  @param handshakeHandler Validates and admits the connection's hello.
    ///  @param messageDispatcher Processes each inbound message after
    ///  authentication.
    ///  @param activePlayContext Source of the current play-context identity
    ///  this connection reports.
    ///  @param pairingSession Plugin-lifetime pairing challenge/pending-
    ///  credential state machine, notified of this connection's own
    ///  reconnect/disconnect.
    ///  @param sessionManager Queried at teardown to tell a genuine session
    ///  release apart from one an administrative caller (revoke/block/trust
    ///  reset/factory reset, via `ActiveSessionController`) already performed
    ///  on another thread before forcing this connection closed -- see
    ///  `sessionReleaseNotificationSink`.
    ///  @param sessionReleaseNotificationSink Notified with this connection's
    ///  own `clientId` immediately after its session slot is actually
    ///  released by this teardown, so an observer (a reconnecting client, a
    ///  test driver) can learn the slot is free without racing the release.
    ///  Not notified when an administrative caller already released the slot
    ///  before this teardown ran: that caller's own response line is the
    ///  authoritative signal for that case, and firing both would race one
    ///  another on the same output stream with no defined order.
    ///  @param bridgeInstanceId This bridge process's identity, stamped onto
    ///  every response envelope; no value if generation failed at startup.
    ConnectionSession(IHandshakeHandler& handshakeHandler,
                      IMessageDispatcher& messageDispatcher,
                      const IActivePlayContextReader& activePlayContext,
                      security::IPairingSession& pairingSession,
                      ISessionManager& sessionManager,
                      ISessionReleaseNotificationSink& sessionReleaseNotificationSink,
                      std::optional<std::string> bridgeInstanceId);

    ///  @copydoc IConnectionSession::Run
    ///  Repeats the base interface's default so existing callers that hold a
    ///  concrete `ConnectionSession&` (rather than an `IConnectionSession&`)
    ///  can still omit `steadyNow`; default arguments resolve by the static
    ///  type of the call expression, not the dynamic type, so the interface's
    ///  own default does not apply through a concrete-typed reference.
    void Run(transport::IWebSocketSession& ws, ConnectionId connection, SteadyNowProvider steadyNow = [] { return std::chrono::steady_clock::now(); }) override;

  private:
    ///  Validates and admits the connection's hello.
    IHandshakeHandler& handshakeHandler_;

    ///  Processes each inbound message after authentication.
    IMessageDispatcher& messageDispatcher_;

    ///  Source of the current play-context identity this connection reports.
    const IActivePlayContextReader& activePlayContext_;

    ///  Plugin-lifetime pairing challenge/pending-credential state machine.
    security::IPairingSession& pairingSession_;

    ///  Queried at teardown to tell a genuine session release apart from one
    ///  an administrative caller already performed on another thread.
    ISessionManager& sessionManager_;

    ///  Notified with this connection's own `clientId` when this teardown is
    ///  the one that actually released its session slot.
    ISessionReleaseNotificationSink& sessionReleaseNotificationSink_;

    ///  This bridge process's identity, stamped onto every response envelope.
    std::optional<std::string> bridgeInstanceId_;
};

} //  namespace dovahlink::application
