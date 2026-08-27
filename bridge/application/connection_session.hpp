#pragma once

#include "application/active_play_context_reader.hpp"
#include "application/handshake_handler.hpp"
#include "application/pairing_notification_sink.hpp"
#include "application/session.hpp"
#include "application/subscription_handler.hpp"
#include "application/trust_mutation_coordinator.hpp"
#include "security/pairing_session.hpp"
#include "security/trust_store.hpp"
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
    ///  @param trustStore Plugin-lifetime persistent trust store.
    ///  @param sessionManager Session registry for the connection.
    ///  @param activePlayContext Source of the current play-context identity
    ///  this connection reports.
    ///  @param pairingSession Plugin-lifetime pairing challenge/pending-
    ///  credential state machine.
    ///  @param mutationCoordinator Serializes pairing finalization and
    ///  administrative trust mutations.
    ///  @param pairingNotificationSink Displays a freshly generated pairing
    ///  code to the user.
    ///  @param bridgeInstanceId This bridge process's identity, stamped onto
    ///  every response envelope; no value if generation failed at startup.
    ///  @param bridgeVersion The DovahLink Bridge/mod release version exposed
    ///  to the client in `hello_ack.bridgeVersion`
    ///      (`ai/context/protocol/compatibility.md`).
    ConnectionSession(IHandshakeHandler& handshakeHandler,
                      security::ITrustStore& trustStore,
                      ISessionManager& sessionManager,
                      const IActivePlayContextReader& activePlayContext,
                      security::IPairingSession& pairingSession,
                      ITrustMutationCoordinator& mutationCoordinator,
                      PairingNotificationSink& pairingNotificationSink,
                      std::optional<std::string> bridgeInstanceId,
                      std::string bridgeVersion);

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

    ///  Plugin-lifetime persistent trust store.
    security::ITrustStore& trustStore_;

    ///  Session registry for the connection.
    ISessionManager& sessionManager_;

    ///  Source of the current play-context identity this connection reports.
    const IActivePlayContextReader& activePlayContext_;

    ///  Plugin-lifetime pairing challenge/pending-credential state machine.
    security::IPairingSession& pairingSession_;

    ///  Serializes pairing finalization and administrative trust mutations.
    ITrustMutationCoordinator& mutationCoordinator_;

    ///  Displays a freshly generated pairing code to the user.
    PairingNotificationSink& pairingNotificationSink_;

    ///  This bridge process's identity, stamped onto every response envelope.
    std::optional<std::string> bridgeInstanceId_;

    ///  The DovahLink Bridge/mod release version exposed to clients.
    std::string bridgeVersion_;
};

} //  namespace dovahlink::application
