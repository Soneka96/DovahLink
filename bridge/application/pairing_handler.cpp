#include "application/pairing_handler.hpp"

#include "protocol/messages.hpp"
#include "security/csprng.hpp"
#include "security/hex.hpp"

#include <utility>

namespace dovahlink::application {

namespace {

/// Size of a freshly generated pairing credential, matching `security::GenerateOpaqueId`'s own
/// 128-bit opaque-identifier size (this handler generates the credential itself rather than
/// reusing that function, since the raw bytes -- not its hex encoding -- are what `PairingSession`
/// and `TrustStore` store and compare).
constexpr std::size_t kCredentialBytes = 16;

/// Builds a `pairing_outcome` envelope, falling back to a generic `internal_error` envelope if
/// envelope construction itself fails (the same unreachable-in-practice CSPRNG fallback every
/// envelope-building path in this codebase shares).
protocol::Envelope BuildPairingOutcome(const std::string& sessionId, std::optional<std::string> correlationId,
                                        const protocol::PairingOutcomePayload& payload) {
    auto envelope = protocol::BuildEnvelope(std::string(protocol::message_type::kPairingOutcome), sessionId,
                                             correlationId, protocol::EncodePairingOutcomePayload(payload));
    if (envelope.has_value()) {
        return std::move(*envelope);
    }
    return protocol::BuildErrorEnvelope(std::move(correlationId), sessionId, "internal_error",
                                         "Unable to build response", false);
}

}  // namespace

protocol::Envelope HandlePairingRequest(const protocol::Envelope& pairingRequestEnvelope,
                                          const std::string& sessionId, security::PairingSession& pairingSession,
                                          PairingNotificationSink& notificationSink) {
    auto code = pairingSession.TryStartChallenge();
    std::string state;
    if (code.has_value()) {
        notificationSink.NotifyPairingCodeAvailable(*code);
        state = "available";
    } else {
        // Already in progress: a challenge or a pending credential is active. Never a second code,
        // never a second notification.
        state = "in_progress";
    }

    auto envelope = protocol::BuildEnvelope(std::string(protocol::message_type::kPairingStatus), sessionId,
                                             pairingRequestEnvelope.messageId,
                                             protocol::EncodePairingStatusPayload(
                                                 protocol::PairingStatusPayload{.state = std::move(state)}));
    if (envelope.has_value()) {
        return std::move(*envelope);
    }
    return protocol::BuildErrorEnvelope(pairingRequestEnvelope.messageId, sessionId, "internal_error",
                                         "Unable to build response", false);
}

protocol::Envelope HandlePairingConfirm(const protocol::Envelope& pairingConfirmEnvelope,
                                          const std::string& sessionId, const std::string& clientId,
                                          security::PairingSession& pairingSession,
                                          std::chrono::steady_clock::time_point now) {
    auto confirm = protocol::DecodePairingConfirmPayload(pairingConfirmEnvelope.payload);
    if (!confirm.has_value()) {
        return protocol::BuildErrorEnvelope(pairingConfirmEnvelope.messageId, sessionId, "malformed_message",
                                             "Malformed pairing_confirm payload", false);
    }

    // Generated unconditionally, before knowing whether the code is even valid: TryConfirmCode
    // needs the credential ready to hold pending atomically with validating the code, and this
    // handler has no earlier point to generate it from.
    auto credentialBytes = security::GenerateRandomBytes(kCredentialBytes);
    if (!credentialBytes.has_value()) {
        return protocol::BuildErrorEnvelope(pairingConfirmEnvelope.messageId, sessionId, "internal_error",
                                             "Unable to generate a credential", false);
    }

    auto result =
        pairingSession.TryConfirmCode(confirm->code, now, clientId, *credentialBytes, confirm->displayName);

    if (result == security::PairingSession::ConfirmResult::kConfirmed) {
        return BuildPairingOutcome(sessionId, pairingConfirmEnvelope.messageId,
                                    protocol::PairingOutcomePayload{
                                        .outcome = "credential_issued",
                                        .credential = security::EncodeHex(*credentialBytes),
                                        .shortId = std::nullopt,
                                        .displayName = confirm->displayName,
                                    });
    }
    if (result == security::PairingSession::ConfirmResult::kRateLimited) {
        return BuildPairingOutcome(sessionId, pairingConfirmEnvelope.messageId,
                                    protocol::PairingOutcomePayload{.outcome = "rate_limited"});
    }
    if (result == security::PairingSession::ConfirmResult::kExpired) {
        return BuildPairingOutcome(sessionId, pairingConfirmEnvelope.messageId,
                                    protocol::PairingOutcomePayload{.outcome = "expired"});
    }
    // Only kInvalid remains.
    return BuildPairingOutcome(sessionId, pairingConfirmEnvelope.messageId,
                                protocol::PairingOutcomePayload{.outcome = "invalid"});
}

protocol::Envelope HandlePairingAck(const protocol::Envelope& pairingAckEnvelope, const std::string& sessionId,
                                      const std::string& clientId, ConnectionId connection,
                                      security::PairingSession& pairingSession, security::TrustStore& trustStore,
                                      SessionManager& sessionManager) {
    auto ack = protocol::DecodePairingAckPayload(pairingAckEnvelope.payload);
    if (!ack.has_value()) {
        return protocol::BuildErrorEnvelope(pairingAckEnvelope.messageId, sessionId, "malformed_message",
                                             "Malformed pairing_ack payload", false);
    }
    auto credentialBytes = security::DecodeHex(ack->credential);
    if (!credentialBytes.has_value()) {
        // Cannot possibly match a pending record's raw bytes; the same "doesn't match anything"
        // outcome a genuinely unrecognized credential gets, matching how handshake_handler.cpp
        // treats a non-hex hello credential as simply not matching rather than a distinct
        // malformed-payload case.
        return BuildPairingOutcome(sessionId, pairingAckEnvelope.messageId,
                                    protocol::PairingOutcomePayload{.outcome = "pending_not_found"});
    }

    // Idempotent retry: a credential that already matches a committed trust record means an
    // earlier pairing_ack on this same flow already succeeded and only its response was lost --
    // report success again rather than trying (and failing) to re-consume an already-spent
    // pending record. Matched by credential (constant-time), not just clientId, so a claimed
    // clientId that merely collides with someone else's already-trusted identity cannot be
    // upgraded to full trust without actually presenting that identity's real credential.
    if (trustStore.Authenticate(clientId, *credentialBytes)) {
        auto existing = trustStore.Query(clientId);
        if (existing.has_value()) {
            sessionManager.UpgradeToFullTrust(connection, sessionId);
            return BuildPairingOutcome(sessionId, pairingAckEnvelope.messageId,
                                        protocol::PairingOutcomePayload{
                                            .outcome = "already_trusted",
                                            .credential = security::EncodeHex(existing->credential),
                                            .shortId = existing->shortId,
                                            .displayName = existing->displayName,
                                        });
        }
    }

    auto pending = pairingSession.TryFinalize(clientId, *credentialBytes);
    if (!pending.has_value()) {
        return BuildPairingOutcome(sessionId, pairingAckEnvelope.messageId,
                                    protocol::PairingOutcomePayload{.outcome = "pending_not_found"});
    }

    auto persisted = trustStore.Persist(pending->clientId, pending->credential, pending->displayName);
    if (!persisted.has_value()) {
        return protocol::BuildErrorEnvelope(pairingAckEnvelope.messageId, sessionId, "internal_error",
                                             "Unable to commit trust", false);
    }

    sessionManager.UpgradeToFullTrust(connection, sessionId);

    return BuildPairingOutcome(sessionId, pairingAckEnvelope.messageId,
                                protocol::PairingOutcomePayload{
                                    .outcome = "trusted",
                                    .credential = security::EncodeHex(persisted->credential),
                                    .shortId = persisted->shortId,
                                    .displayName = persisted->displayName,
                                });
}

}  // namespace dovahlink::application
