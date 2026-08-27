#pragma once

#include "application/active_play_context_reader.hpp"
#include "application/connection_timeout_tracker.hpp"
#include "application/dispatch_result.hpp"
#include "application/pairing_handler.hpp"
#include "application/replay_guard.hpp"
#include "application/session.hpp"
#include "application/subscription_handler.hpp"
#include "application/trust_mutation_coordinator.hpp"
#include "security/inbound_message_rate_limiter.hpp"
#include "security/trust_store.hpp"
#include "security/violation_tracker.hpp"

#include <chrono>
#include <cstddef>
#include <optional>
#include <string>

namespace dovahlink::application {

///  Counts, validates, rate-limits, and dispatches inbound messages for one
///  connection, per `ai/context/skse/cpp-style.md`'s rule against a free
///  function mixing lifetime collaborators with per-call data.
class IMessageDispatcher {
  public:
    ///  Releases the interface without performing work.
    virtual ~IMessageDispatcher() = default;

    ///  Processes one inbound message. Oversized frames and exhausted session
    ///  limits request immediate closure without a response.
    ///  @param rawMessage Encoded inbound WebSocket text.
    ///  @param receivedMessageCount Number of completed messages read in this
    ///  session; incremented before decoding.
    ///  @param sessionId Authenticated session identifier.
    ///  @param connection Transport connection identifier.
    ///  @param replayGuard Per-session message-ID guard.
    ///  @param violations Per-connection protocol-violation tracker.
    ///  @param rateLimiter Per-connection inbound rate limiter.
    ///  @param timeoutTracker Connection timeout tracker.
    ///  @param steadyNow Current monotonic time.
    ///  @return Responses and the connection-close decision.
    [[nodiscard]] virtual DispatchResult
    Process(const std::string& rawMessage, std::size_t& receivedMessageCount,
            const std::string& sessionId, ConnectionId connection,
            ReplayGuard& replayGuard, security::IViolationTracker& violations,
            security::IInboundMessageRateLimiter& rateLimiter,
            IConnectionTimeoutTracker& timeoutTracker,
            std::chrono::steady_clock::time_point steadyNow) = 0;
};

///  Binds inbound-message dispatch to its plugin-lifetime collaborators, per
///  `ai/context/skse/cpp-style.md`'s rule against a free function mixing
///  lifetime collaborators with per-call data.
class MessageDispatcher final : public IMessageDispatcher {
  public:
    ///  Binds every collaborator `Process` needs.
    ///  @param sessionManager Session registry. Also determines a call's
    ///  allowed message types: a
    ///      `Restricted` session may only exchange `ping`/`capabilities`/the
    ///      pairing message types; a `Full` session may exchange
    ///      `ping`/`capabilities`/`subscribe`/`snapshot_request`/
    ///      `rename_request` (pairing messages are pointless once already
    ///      trusted).
    ///  @param trustStore Persistent trust store, for full-session trust-store
    ///  operations such as `rename_request`.
    ///  @param mutationCoordinator Serializes pairing finalization and
    ///  administrative trust mutations, for `pairing_ack`/`pairing_cancel`.
    ///  @param pairingHandler Handles `pairing_request`/`pairing_confirm`/
    ///  `pairing_renotify`.
    ///  @param activePlayContext Source of the current play-context identity,
    ///  stamped onto every response as `playContextId`.
    ///  @param bridgeInstanceId This bridge process's identity, stamped onto
    ///  every response a call
    ///      produces, including an early connection-hygiene rejection. The
    ///      client identity bound to a session is never stamped on any
    ///      response here: once a session exists, it is owned state
    ///      (@ref SessionManager::ClientIdForConnection), not a value repeated
    ///      on the wire.
    MessageDispatcher(ISessionManager& sessionManager,
                      security::ITrustStore& trustStore,
                      ITrustMutationCoordinator& mutationCoordinator,
                      IPairingHandler& pairingHandler,
                      const IActivePlayContextReader& activePlayContext,
                      std::optional<std::string> bridgeInstanceId);

    ///  @copydoc IMessageDispatcher::Process
    [[nodiscard]] DispatchResult
    Process(const std::string& rawMessage, std::size_t& receivedMessageCount,
            const std::string& sessionId, ConnectionId connection,
            ReplayGuard& replayGuard, security::IViolationTracker& violations,
            security::IInboundMessageRateLimiter& rateLimiter,
            IConnectionTimeoutTracker& timeoutTracker,
            std::chrono::steady_clock::time_point steadyNow) override;

  private:
    ///  Session registry.
    ISessionManager& sessionManager_;

    ///  Persistent trust store.
    security::ITrustStore& trustStore_;

    ///  Serializes pairing finalization and administrative trust mutations.
    ITrustMutationCoordinator& mutationCoordinator_;

    ///  Handles `pairing_request`/`pairing_confirm`/`pairing_renotify`.
    IPairingHandler& pairingHandler_;

    ///  Source of the current play-context identity.
    const IActivePlayContextReader& activePlayContext_;

    ///  This bridge process's identity, stamped onto every response.
    std::optional<std::string> bridgeInstanceId_;
};

} //  namespace dovahlink::application
