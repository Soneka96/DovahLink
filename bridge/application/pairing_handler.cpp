#include "application/pairing_handler.hpp"

#include "protocol/messages.hpp"
#include "security/csprng.hpp"
#include "security/hex.hpp"

#include <cstdint>
#include <utility>

namespace dovahlink::application {

namespace {

///  Size of a freshly generated pairing credential, matching
///  `security::GenerateOpaqueId`'s own 128-bit opaque-identifier size (this
///  handler generates the credential itself rather than reusing that function,
///  since the raw bytes -- not its hex encoding -- are what `PairingSession` and
///  `TrustStore` store and compare).
constexpr std::size_t kCredentialBytes = 16;

///  Builds a `pairing_outcome` envelope, falling back to a generic
///  `internal_error` envelope if envelope construction itself fails (the same
///  unreachable-in-practice CSPRNG fallback every envelope-building path in this
///  codebase shares).
protocol::Envelope
BuildPairingOutcome(const std::string& sessionId,
                    std::optional<std::string> correlationId,
                    const protocol::PairingOutcomePayload& payload) {
    auto envelope = protocol::BuildEnvelope(
        std::string(protocol::message_type::kPairingOutcome), sessionId,
        correlationId, protocol::EncodePairingOutcomePayload(payload));
    if (envelope.has_value()) {
        return std::move(*envelope);
    }
    return protocol::BuildErrorEnvelope(std::move(correlationId), sessionId,
                                        "internal_error",
                                        "Unable to build response", false);
}

///  Converts a `PairingSession` duration to the wire's plain-integer seconds
///  shape.
std::optional<std::int64_t>
ToWireSeconds(std::optional<std::chrono::seconds> duration) {
    if (!duration.has_value()) {
        return std::nullopt;
    }
    return static_cast<std::int64_t>(duration->count());
}

} //  namespace

protocol::Envelope
HandlePairingRequest(const protocol::Envelope& pairingRequestEnvelope,
                     const std::string& sessionId, const std::string& clientId,
                     security::PairingSession& pairingSession,
                     PairingNotificationSink& notificationSink,
                     std::chrono::steady_clock::time_point now) {
    auto result = pairingSession.TryStartChallenge(clientId, now);
    std::string state;
    std::optional<std::int64_t> expiresInSeconds;
    switch (result.outcome) {
    case security::StartChallengeOutcome::kStarted:
        notificationSink.NotifyPairingCodeAvailable(*result.code);
        state = "available";
        expiresInSeconds = ToWireSeconds(pairingSession.RemainingSeconds(now));
        break;
    case security::StartChallengeOutcome::kResumed:
        //  clientId already owns the active challenge or pending credential. Never a
        //  second code, never a second notification.
        state = "in_progress";
        expiresInSeconds = ToWireSeconds(pairingSession.RemainingSeconds(now));
        break;
    case security::StartChallengeOutcome::kOtherDeviceActive:
        //  A different clientId owns it -- reveal nothing else, including remaining
        //  time.
        state = "other_device_pairing";
        break;
    case security::StartChallengeOutcome::kGeneratorFailed:
        state = "unavailable";
        break;
    }

    auto envelope = protocol::BuildEnvelope(
        std::string(protocol::message_type::kPairingStatus), sessionId,
        pairingRequestEnvelope.messageId,
        protocol::EncodePairingStatusPayload(protocol::PairingStatusPayload{
            .state = std::move(state), .expiresInSeconds = expiresInSeconds}));
    if (envelope.has_value()) {
        return std::move(*envelope);
    }
    return protocol::BuildErrorEnvelope(pairingRequestEnvelope.messageId,
                                        sessionId, "internal_error",
                                        "Unable to build response", false);
}

protocol::Envelope
HandlePairingConfirm(const protocol::Envelope& pairingConfirmEnvelope,
                     const std::string& sessionId, const std::string& clientId,
                     security::PairingSession& pairingSession,
                     PairingNotificationSink& notificationSink,
                     std::chrono::steady_clock::time_point now) {
    auto confirm =
        protocol::DecodePairingConfirmPayload(pairingConfirmEnvelope.payload);
    if (!confirm.has_value()) {
        return protocol::BuildErrorEnvelope(
            pairingConfirmEnvelope.messageId, sessionId, "malformed_message",
            "Malformed pairing_confirm payload", false);
    }

    //  Generated unconditionally, before knowing whether the code is even valid:
    //  TryConfirmCode needs the credential ready to hold pending atomically with
    //  validating the code, and this handler has no earlier point to generate it
    //  from.
    auto credentialBytes = security::GenerateRandomBytes(kCredentialBytes);
    if (!credentialBytes.has_value()) {
        return protocol::BuildErrorEnvelope(
            pairingConfirmEnvelope.messageId, sessionId, "internal_error",
            "Unable to generate a credential", false);
    }

    auto result = pairingSession.TryConfirmCode(
        confirm->code, now, clientId, *credentialBytes, confirm->displayName);

    if (result.outcome == security::ConfirmResult::kConfirmed) {
        return BuildPairingOutcome(
            sessionId, pairingConfirmEnvelope.messageId,
            protocol::PairingOutcomePayload{
                .outcome = "credential_issued",
                .credential = security::EncodeHex(*credentialBytes),
                .shortId = std::nullopt,
                .displayName = confirm->displayName,
            });
    }
    if (result.outcome == security::ConfirmResult::kPacingLimited) {
        return BuildPairingOutcome(
            sessionId, pairingConfirmEnvelope.messageId,
            protocol::PairingOutcomePayload{
                .outcome = "pacing_limited",
                .retryAfterSeconds = ToWireSeconds(result.retryAfterSeconds)});
    }
    if (result.outcome == security::ConfirmResult::kExpired) {
        return BuildPairingOutcome(
            sessionId, pairingConfirmEnvelope.messageId,
            protocol::PairingOutcomePayload{.outcome = "expired"});
    }
    if (result.outcome == security::ConfirmResult::kHardLimitReached) {
        notificationSink.NotifyPairingAttemptsExhausted();
        return BuildPairingOutcome(
            sessionId, pairingConfirmEnvelope.messageId,
            protocol::PairingOutcomePayload{.outcome = "hard_limit_reached"});
    }
    //  Only kInvalid remains.
    if (result.shouldAutoRenotify) {
        auto currentCode = pairingSession.CurrentCode(now);
        if (currentCode.has_value()) {
            notificationSink.NotifyPairingCodeIncorrect(*currentCode);
        }
    }
    return BuildPairingOutcome(
        sessionId, pairingConfirmEnvelope.messageId,
        protocol::PairingOutcomePayload{.outcome = "invalid"});
}

protocol::Envelope HandlePairingAck(
    const protocol::Envelope& pairingAckEnvelope, const std::string& sessionId,
    const std::string& clientId, ConnectionId connection,
    security::PairingSession& pairingSession, security::TrustStore& trustStore,
    SessionManager& sessionManager, std::chrono::steady_clock::time_point now) {
    auto ack = protocol::DecodePairingAckPayload(pairingAckEnvelope.payload);
    if (!ack.has_value()) {
        return protocol::BuildErrorEnvelope(pairingAckEnvelope.messageId, sessionId,
                                            "malformed_message",
                                            "Malformed pairing_ack payload", false);
    }
    auto credentialBytes = security::DecodeHex(ack->credential);
    if (!credentialBytes.has_value()) {
        //  Cannot possibly match a pending record's raw bytes; the same "doesn't
        //  match anything" outcome a genuinely unrecognized credential gets,
        //  matching how handshake_handler.cpp treats a non-hex hello credential as
        //  simply not matching rather than a distinct malformed-payload case.
        return BuildPairingOutcome(
            sessionId, pairingAckEnvelope.messageId,
            protocol::PairingOutcomePayload{.outcome = "pending_not_found"});
    }

    //  Idempotent retry: a credential that already matches a committed trust
    //  record means an earlier pairing_ack on this same flow already succeeded and
    //  only its response was lost -- report success again rather than trying (and
    //  failing) to re-consume an already-spent pending record. Matched by
    //  credential (constant-time), not just clientId, so a claimed clientId that
    //  merely collides with someone else's already-trusted identity cannot be
    //  upgraded to full trust without actually presenting that identity's real
    //  credential.
    if (trustStore.Authenticate(clientId, *credentialBytes)) {
        auto existing = trustStore.Query(clientId);
        if (existing.has_value()) {
            sessionManager.UpgradeToFullTrust(connection, sessionId);
            return BuildPairingOutcome(
                sessionId, pairingAckEnvelope.messageId,
                protocol::PairingOutcomePayload{
                    .outcome = "already_trusted",
                    .credential = security::EncodeHex(existing->credential),
                    .shortId = existing->shortId,
                    .displayName = existing->displayName,
                });
        }
    }

    auto pending = pairingSession.PeekPending(clientId, *credentialBytes, now);
    if (!pending.has_value()) {
        return BuildPairingOutcome(
            sessionId, pairingAckEnvelope.messageId,
            protocol::PairingOutcomePayload{.outcome = "pending_not_found"});
    }

    //  Consumes the pending credential before persisting it, not after:
    //  CommitPending is the only operation that atomically checks-and-clears
    //  PairingSession's pending state, and PairingSession shares its one mutex
    //  with CancelAll (TrustAdminService::ResetTrust/ConfirmFactoryReset). Doing
    //  this first makes CommitPending and a concurrent CancelAll mutually
    //  exclusive -- whichever runs first wins deterministically -- so an admin
    //  operation cancelling every pending pairing can no longer be defeated by a
    //  pairing_ack that peeked the pending record before the cancel but committed
    //  it to TrustStore after. Committing after Persist (the previous order) let
    //  Persist durably create a Trusted device from a pending credential CancelAll
    //  had already cleared, since nothing checked PairingSession's state again
    //  before that write landed.
    if (!pairingSession.CommitPending(clientId, *credentialBytes, now)) {
        return BuildPairingOutcome(
            sessionId, pairingAckEnvelope.messageId,
            protocol::PairingOutcomePayload{.outcome = "pending_not_found"});
    }

    auto persisted = trustStore.Persist(pending->clientId, pending->credential,
                                        pending->displayName);
    if (!persisted.has_value()) {
        //  Unlike the previous order, the pending credential is already consumed at
        //  this point: a retried pairing_ack after this transient failure gets
        //  pending_not_found, not another shot at the same pending record -- the
        //  client must restart pairing from pairing_confirm.
        return protocol::BuildErrorEnvelope(pairingAckEnvelope.messageId, sessionId,
                                            "internal_error",
                                            "Unable to commit trust", false);
    }

    sessionManager.UpgradeToFullTrust(connection, sessionId);

    return BuildPairingOutcome(
        sessionId, pairingAckEnvelope.messageId,
        protocol::PairingOutcomePayload{
            .outcome = "trusted",
            .credential = security::EncodeHex(persisted->credential),
            .shortId = persisted->shortId,
            .displayName = persisted->displayName,
        });
}

protocol::Envelope
HandlePairingRenotify(const protocol::Envelope& pairingRenotifyEnvelope,
                      const std::string& sessionId, const std::string& clientId,
                      security::PairingSession& pairingSession,
                      PairingNotificationSink& notificationSink,
                      std::chrono::steady_clock::time_point now) {
    auto result = pairingSession.TryRenotify(clientId, now);
    switch (result.outcome) {
    case security::RenotifyOutcome::kRenotified: {
        auto code = pairingSession.CurrentCode(now);
        if (code.has_value()) {
            notificationSink.NotifyPairingCodeAvailable(*code);
        }
        return BuildPairingOutcome(
            sessionId, pairingRenotifyEnvelope.messageId,
            protocol::PairingOutcomePayload{.outcome = "renotified"});
    }
    case security::RenotifyOutcome::kCooldown:
        return BuildPairingOutcome(
            sessionId, pairingRenotifyEnvelope.messageId,
            protocol::PairingOutcomePayload{
                .outcome = "renotify_cooldown",
                .retryAfterSeconds = ToWireSeconds(result.retryAfterSeconds)});
    case security::RenotifyOutcome::kNotActive:
        return BuildPairingOutcome(
            sessionId, pairingRenotifyEnvelope.messageId,
            protocol::PairingOutcomePayload{.outcome = "already_idle"});
    }
    //  Unreachable: every enumerator is handled above.
    return BuildPairingOutcome(
        sessionId, pairingRenotifyEnvelope.messageId,
        protocol::PairingOutcomePayload{.outcome = "already_idle"});
}

protocol::Envelope
HandlePairingCancel(const protocol::Envelope& pairingCancelEnvelope,
                    const std::string& sessionId, const std::string& clientId,
                    security::PairingSession& pairingSession,
                    std::chrono::steady_clock::time_point now) {
    auto outcome = pairingSession.TryCancel(clientId, now);
    switch (outcome) {
    case security::CancelOutcome::kCancelled:
        return BuildPairingOutcome(
            sessionId, pairingCancelEnvelope.messageId,
            protocol::PairingOutcomePayload{.outcome = "cancelled"});
    case security::CancelOutcome::kAlreadyIdle:
        return BuildPairingOutcome(
            sessionId, pairingCancelEnvelope.messageId,
            protocol::PairingOutcomePayload{.outcome = "already_idle"});
    }
    //  Unreachable: every enumerator is handled above.
    return BuildPairingOutcome(
        sessionId, pairingCancelEnvelope.messageId,
        protocol::PairingOutcomePayload{.outcome = "already_idle"});
}

} //  namespace dovahlink::application
