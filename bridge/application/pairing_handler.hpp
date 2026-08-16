#pragma once

#include "application/pairing_notification_sink.hpp"
#include "application/session.hpp"
#include "protocol/envelope.hpp"
#include "security/pairing_session.hpp"
#include "security/trust_store.hpp"

#include <chrono>
#include <string>

namespace dovahlink::application {

/// Handles a `pairing_request`: starts a challenge if none is active and displays its code via
/// `notificationSink`, or reports that pairing is already in progress without generating or
/// displaying a second code.
/// @param pairingRequestEnvelope Decoded client `pairing_request`.
/// @param sessionId Authenticated session identifier.
/// @param pairingSession Bridge-lifetime pairing challenge/pending-credential state machine.
/// @param notificationSink Displays a freshly generated code to the user.
/// @return `pairing_status` envelope reporting `"available"` or `"in_progress"`.
[[nodiscard]] protocol::Envelope HandlePairingRequest(const protocol::Envelope& pairingRequestEnvelope,
                                                        const std::string& sessionId,
                                                        security::PairingSession& pairingSession,
                                                        PairingNotificationSink& notificationSink);

/// Handles a `pairing_confirm`: validates the submitted code against the active challenge and, on
/// success, generates a credential and holds it pending finalization via `pairingSession`.
/// @param pairingConfirmEnvelope Decoded client `pairing_confirm`.
/// @param sessionId Authenticated session identifier.
/// @param clientId The connection's authenticated client identity (session-owned state).
/// @param pairingSession Bridge-lifetime pairing challenge/pending-credential state machine.
/// @param now Current monotonic time, for the code-attempt throttle.
/// @return `pairing_outcome` envelope: `"credential_issued"` on success, or
///     `"expired"`/`"invalid"`/`"rate_limited"`; a generic `error` envelope for a malformed
///     payload or an internal failure building the response.
[[nodiscard]] protocol::Envelope HandlePairingConfirm(const protocol::Envelope& pairingConfirmEnvelope,
                                                        const std::string& sessionId, const std::string& clientId,
                                                        security::PairingSession& pairingSession,
                                                        std::chrono::steady_clock::time_point now);

/// Handles a `pairing_ack`: idempotently checks whether `clientId` is already trusted before
/// touching `pairingSession` at all (a lost-response retry resolves to `"already_trusted"`, not an
/// error); otherwise matches the echoed credential against the pending record and, on success,
/// commits it to `trustStore` and upgrades the session to full trust.
/// @param pairingAckEnvelope Decoded client `pairing_ack`.
/// @param sessionId Authenticated session identifier.
/// @param clientId The connection's authenticated client identity.
/// @param connection Transport connection identifier, for the trust-tier upgrade.
/// @param pairingSession Bridge-lifetime pairing challenge/pending-credential state machine.
/// @param trustStore Persistent trust store, queried for idempotency and committed to on success.
/// @param sessionManager Session registry, upgraded to full trust on success.
/// @return `pairing_outcome` envelope: `"trusted"`, `"already_trusted"`, or
///     `"pending_not_found"`; a generic `error` envelope for a malformed payload or an internal
///     failure committing the credential or building the response.
[[nodiscard]] protocol::Envelope HandlePairingAck(const protocol::Envelope& pairingAckEnvelope,
                                                    const std::string& sessionId, const std::string& clientId,
                                                    ConnectionId connection, security::PairingSession& pairingSession,
                                                    security::TrustStore& trustStore,
                                                    SessionManager& sessionManager);

}  // namespace dovahlink::application
