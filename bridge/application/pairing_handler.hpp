#pragma once

#include "application/pairing_notification_sink.hpp"
#include "application/session.hpp"
#include "application/trust_mutation_coordinator.hpp"
#include "protocol/envelope.hpp"
#include "security/pairing_session.hpp"
#include "security/trust_store.hpp"

#include <chrono>
#include <string>

namespace dovahlink::application {

///  Binds the three pairing message handlers that mix multiple plugin-lifetime
///  collaborators with per-call data (`ai/context/skse/cpp-style.md`'s rule
///  against a free function doing so) to those collaborators. `HandlePairingAck`
///  and `HandlePairingCancel` each depend on exactly one collaborator and remain
///  free functions below, exempt from that rule.
class IPairingHandler {
  public:
    ///  Releases the interface without performing work.
    virtual ~IPairingHandler() = default;

    ///  Handles a `pairing_request`: starts a fresh challenge and displays its
    ///  code via the bound notification sink, resumes `clientId`'s own
    ///  already-active challenge or pending credential without generating or
    ///  displaying a second code, or reports that a different `clientId` owns
    ///  it without revealing anything about that device or its code.
    ///  @param pairingRequestEnvelope Decoded client `pairing_request`.
    ///  @param sessionId Authenticated session identifier.
    ///  @param clientId The client identity bound to this connection's
    ///  session, presented at `hello` (session-owned state).
    ///  @param now Current monotonic time, for the lazy-expiry checks and
    ///  `expiresInSeconds`.
    ///  @return `pairing_status` envelope reporting
    ///  `"available"`/`"in_progress"` (both carrying `expiresInSeconds`),
    ///      `"other_device_pairing"`, or `"unavailable"` when the underlying
    ///      code generator fails.
    [[nodiscard]] virtual protocol::Envelope
    HandleRequest(const protocol::Envelope& pairingRequestEnvelope,
                  const std::string& sessionId, const std::string& clientId,
                  std::chrono::steady_clock::time_point now) = 0;

    ///  Handles a `pairing_confirm`: validates the submitted code against the
    ///  active challenge and, on success, generates a credential and holds it
    ///  pending finalization. A wrong evaluated attempt redisplays the code
    ///  (distinct wording) when its own auto-renotify cooldown allows it; the
    ///  attempt that reaches the wrong-attempt hard limit instead displays a
    ///  distinct too-many-attempts notification and cancels the challenge.
    ///  @param pairingConfirmEnvelope Decoded client `pairing_confirm`.
    ///  @param sessionId Authenticated session identifier.
    ///  @param clientId The client identity bound to this connection's
    ///  session, presented at `hello` (session-owned state).
    ///  @param now Current monotonic time, for the lazy-expiry checks and
    ///  pacing/attempt accounting.
    ///  @return `pairing_outcome` envelope: `"credential_issued"` on success,
    ///  or
    ///      `"expired"`/`"invalid"`/`"pacing_limited"`/`"hard_limit_reached"`;
    ///      a generic `error` envelope for a malformed payload or an internal
    ///      failure building the response.
    [[nodiscard]] virtual protocol::Envelope
    HandleConfirm(const protocol::Envelope& pairingConfirmEnvelope,
                  const std::string& sessionId, const std::string& clientId,
                  std::chrono::steady_clock::time_point now) = 0;

    ///  Handles a `pairing_renotify`: "show my code again". Redisplays
    ///  `clientId`'s owned active challenge's existing code when its manual
    ///  renotify cooldown allows it; never generates a new code and never
    ///  sends the code itself over the wire.
    ///  @param pairingRenotifyEnvelope Decoded client `pairing_renotify` (no
    ///  payload beyond the standard envelope).
    ///  @param sessionId Authenticated session identifier.
    ///  @param clientId The client identity bound to this connection's
    ///  session, presented at `hello` (session-owned state).
    ///  @param now Current monotonic time, for the lazy-expiry checks and
    ///  cooldown accounting.
    ///  @return `pairing_outcome` envelope: `"renotified"` on success,
    ///  `"renotify_cooldown"` (carrying
    ///      `retryAfterSeconds`) while the cooldown is still active, or
    ///      `"already_idle"` when `clientId` owns no active challenge or
    ///      pending credential.
    [[nodiscard]] virtual protocol::Envelope
    HandleRenotify(const protocol::Envelope& pairingRenotifyEnvelope,
                   const std::string& sessionId, const std::string& clientId,
                   std::chrono::steady_clock::time_point now) = 0;
};

///  Binds the pairing-request/confirm/renotify handlers to their
///  plugin-lifetime collaborators, per `ai/context/skse/cpp-style.md`'s rule
///  against a free function mixing lifetime collaborators with per-call data.
class PairingHandler final : public IPairingHandler {
  public:
    ///  Binds every collaborator `HandleRequest`/`HandleConfirm`/
    ///  `HandleRenotify` need.
    ///  @param pairingSession Bridge-lifetime pairing challenge/pending-
    ///  credential state machine.
    ///  @param mutationCoordinator Captures the trust mutation fence and
    ///  creates the pending credential under the same coordination boundary
    ///      as admin mutations.
    ///  @param notificationSink Displays a freshly generated pairing code, or
    ///  redisplays one after a wrong attempt or reports attempts exhausted.
    PairingHandler(security::IPairingSession& pairingSession,
                   ITrustMutationCoordinator& mutationCoordinator,
                   PairingNotificationSink& notificationSink);

    ///  @copydoc IPairingHandler::HandleRequest
    [[nodiscard]] protocol::Envelope
    HandleRequest(const protocol::Envelope& pairingRequestEnvelope,
                  const std::string& sessionId, const std::string& clientId,
                  std::chrono::steady_clock::time_point now) override;

    ///  @copydoc IPairingHandler::HandleConfirm
    [[nodiscard]] protocol::Envelope
    HandleConfirm(const protocol::Envelope& pairingConfirmEnvelope,
                  const std::string& sessionId, const std::string& clientId,
                  std::chrono::steady_clock::time_point now) override;

    ///  @copydoc IPairingHandler::HandleRenotify
    [[nodiscard]] protocol::Envelope
    HandleRenotify(const protocol::Envelope& pairingRenotifyEnvelope,
                   const std::string& sessionId, const std::string& clientId,
                   std::chrono::steady_clock::time_point now) override;

  private:
    ///  Bridge-lifetime pairing challenge/pending-credential state machine.
    security::IPairingSession& pairingSession_;

    ///  Serializes pairing finalization and administrative trust mutations.
    ITrustMutationCoordinator& mutationCoordinator_;

    ///  Displays a freshly generated pairing code to the user.
    PairingNotificationSink& notificationSink_;
};

///  Handles a `pairing_ack`: idempotently checks whether `clientId` is already
///  trusted before touching `pairingSession` at all (a lost-response retry
///  resolves to `"already_trusted"`, not an error); otherwise matches the echoed
///  credential against the pending record and, on success, commits it to
///  `trustStore` and upgrades the session to full trust through the coordinator.
///  @param pairingAckEnvelope Decoded client `pairing_ack`.
///  @param sessionId Authenticated session identifier.
///  @param clientId The client identity bound to this connection's session,
///  presented at `hello`.
///  @param connection Transport connection identifier, for the trust-tier
///  upgrade.
///  @param mutationCoordinator Serializes pending-pairing finalization with
///  administrative trust mutations.
///  @param now Current monotonic time, for the pending-credential lazy-expiry
///  check.
///  @return `pairing_outcome` envelope: `"trusted"`, `"already_trusted"`,
///      `"pending_not_found"`, or `"pairing_invalidated"`; a generic `error`
///      envelope for a malformed
///      payload or an internal failure committing the credential or building the
///      response.
[[nodiscard]] protocol::Envelope HandlePairingAck(
    const protocol::Envelope& pairingAckEnvelope, const std::string& sessionId,
    const std::string& clientId, ConnectionId connection,
    ITrustMutationCoordinator& mutationCoordinator,
    std::chrono::steady_clock::time_point now);

///  Handles a `pairing_cancel`: `clientId` gives up its owned active challenge
///  or pending credential, whichever is currently held. Never touches
///  `TrustStore` or any already-committed trust; only ever clears in-memory
///  challenge/pending state, freeing the slot for a fresh `pairing_request`
///  immediately.
///  @param pairingCancelEnvelope Decoded client `pairing_cancel` (no payload
///  beyond the standard
///      envelope).
///  @param sessionId Authenticated session identifier.
///  @param clientId The client identity bound to this connection's session,
///  presented at `hello`
///      (session-owned state).
///  @param mutationCoordinator Serializes cancellation with pending-pairing
///  finalization and administrative trust mutations.
///  @param now Current monotonic time, for the lazy-expiry checks.
///  @return `pairing_outcome` envelope: `"cancelled"` when `clientId` owned
///  something and it was
///      cleared, or `"already_idle"` when it owned no active challenge or
///      pending credential.
[[nodiscard]] protocol::Envelope
HandlePairingCancel(const protocol::Envelope& pairingCancelEnvelope,
                    const std::string& sessionId, const std::string& clientId,
                    ITrustMutationCoordinator& mutationCoordinator,
                    std::chrono::steady_clock::time_point now);

} //  namespace dovahlink::application
