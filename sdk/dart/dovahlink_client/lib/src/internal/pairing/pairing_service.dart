import 'package:dovahlink_client_sdk/src/dovahlink_pairing_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/protocol_payload_decoder.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/request_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_trust_service.dart';
import 'package:dovahlink_client_sdk/src/pairing_cancel_outcome.dart';
import 'package:dovahlink_client_sdk/src/pairing_challenge_status.dart';
import 'package:dovahlink_client_sdk/src/pairing_renotify_result.dart';
import 'package:dovahlink_client_sdk/src/persistence/client_storage.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/pairing_ack_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/pairing_confirm_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/pairing_outcome_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/pairing_status_payload.dart';
import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Owns pairing operations, per `ai/context/sdk/architecture.md`'s "Internal composition":
/// starting/querying a pairing challenge, redisplaying or cancelling it, confirming a code,
/// acknowledging the issued credential, and resuming an interrupted confirmation after a crash or
/// relaunch.
abstract interface class IPairingService {
  /// Starts, or queries the status of, a pairing challenge. Valid only on an `unpaired` session.
  /// [PairingChallengeStatus.availability] being [PairingAvailability.otherDevicePairing] means a
  /// different clientId currently owns the active challenge or pending credential.
  Future<PairingChallengeStatus> requestPairing();

  /// Requests redisplay of the active pairing challenge's code the caller owns. Never generates a
  /// new code and never sends the code itself over the wire -- redisplay occurs through the
  /// in-game notification, not the connection. Valid only on an `unpaired` session.
  Future<PairingRenotifyResult> requestPairingRenotify();

  /// Gives up an owned active challenge or pending credential, freeing the slot for a fresh
  /// [requestPairing]. Never touches persisted trust or an already-committed credential. Valid
  /// only on an `unpaired` session.
  Future<PairingCancelOutcome> cancelPairing();

  /// Submits the six-digit code the user read from Skyrim. Durably persists the issued credential
  /// and a `CONFIRMING` recovery state before returning it, per
  /// `ai/context/protocol/security.md`'s "client durably persists its issued credential and its
  /// `CONFIRMING` recovery state before sending final confirmation."
  /// @return The issued credential, already persisted.
  /// @throws [DovahLinkPairingException] if the code was expired, invalid, paced too soon, hit
  ///     the hard wrong-attempt limit, or an administrative mutation invalidated the challenge
  ///     after it began but before this call was evaluated.
  Future<String> confirmPairingCode({
    required String code,
    String? displayName,
  });

  /// Echoes back a [credential] durably saved from [confirmPairingCode], completing pairing.
  /// The session's trust state becomes trusted on success, and the persisted recovery state
  /// clears back to `PairingRecoveryState.none` while keeping the credential.
  /// @throws [DovahLinkPairingException] if the bridge has no matching pending confirmation or
  ///     an administrative mutation invalidated the pending credential.
  Future<void> acknowledgeTrustedCredential(String credential);

  /// Resumes an interrupted pairing confirmation after a crash or relaunch, per
  /// `ai/context/protocol/security.md`'s "a client that saves the credential but crashes before
  /// confirming retries confirmation on restart." Call after `hello` admits an `unpaired` session.
  ///
  /// A no-op returning [DovahLinkTrustState.unpaired] when no confirmation is outstanding. When
  /// one is, retries [acknowledgeTrustedCredential] with the stored credential: a
  /// `pending_not_found` outcome (the bridge restarted and lost the pending credential) or
  /// `pairing_invalidated` outcome (an administrative mutation rejected the pending credential)
  /// discards the local credential and resets to unpaired rather than treating that as a fatal
  /// error; any other failure leaves the `CONFIRMING` state untouched so a later relaunch can retry
  /// again.
  Future<DovahLinkTrustState> recoverPendingPairing();
}

/// Implements [IPairingService], per `ai/context/sdk/architecture.md`'s "Internal composition".
/// Every collaborator ([ISessionTrustService], [IRequestService], [IClientStorage]) is supplied by
/// the caller per `ai/context/sdk/architecture.md`'s "Dependency injection" -- this class never
/// constructs one of its own dependencies.
class PairingService implements IPairingService {
  /// Upgrades trust standing once a pairing acknowledgement succeeds -- the only class permitted
  /// to.
  final ISessionTrustService _sessionTrustService;

  /// Sends a pairing message and awaits its correlated reply.
  final IRequestService _requestService;

  /// The SDK-owned persistence boundary for this client's credential and pairing recovery state.
  final IClientStorage _storage;

  /// Creates a pairing service over [sessionTrustService], [requestService], and [storage].
  PairingService({
    required ISessionTrustService sessionTrustService,
    required IRequestService requestService,
    required IClientStorage storage,
  }) : _sessionTrustService = sessionTrustService,
       _requestService = requestService,
       _storage = storage;

  /// Implements [IPairingService.requestPairing].
  @override
  Future<PairingChallengeStatus> requestPairing() async {
    final Envelope response = await _requestService.sendAndAwait(
      messageType: ProtocolMessageType.pairingRequest,
      payload: const <String, dynamic>{},
      expectedType: ProtocolMessageType.pairingStatus,
      policy: const RequestPolicy(
        retrySafe: true,
        requiredTrustState: DovahLinkTrustState.unpaired,
        timeoutClass: TimeoutClass.short,
      ),
    );
    final PairingStatusPayload status = ProtocolPayloadDecoder.decode(
      PairingStatusPayload.fromJson,
      response.payload,
    );
    return PairingChallengeStatus(
      availability: status.state,
      expiresInSeconds: status.expiresInSeconds,
    );
  }

  /// Implements [IPairingService.requestPairingRenotify].
  @override
  Future<PairingRenotifyResult> requestPairingRenotify() async {
    final Envelope response = await _requestService.sendAndAwait(
      messageType: ProtocolMessageType.pairingRenotify,
      payload: const <String, dynamic>{},
      expectedType: ProtocolMessageType.pairingOutcome,
      policy: const RequestPolicy(
        retrySafe: true,
        requiredTrustState: DovahLinkTrustState.unpaired,
        timeoutClass: TimeoutClass.short,
      ),
    );
    final PairingOutcomePayload outcome = ProtocolPayloadDecoder.decode(
      PairingOutcomePayload.fromJson,
      response.payload,
    );
    final PairingRenotifyStatus? status = PairingRenotifyStatus.fromOutcome(
      outcome.outcome,
    );
    if (status == null) {
      throw DovahLinkProtocolException(
        code: ProtocolErrorCode.malformedMessage,
        message: 'Unexpected pairing_renotify outcome: ${outcome.outcome}',
        retryable: false,
      );
    }
    return PairingRenotifyResult(
      status: status,
      retryAfterSeconds: outcome.retryAfterSeconds,
    );
  }

  /// Implements [IPairingService.cancelPairing].
  @override
  Future<PairingCancelOutcome> cancelPairing() async {
    final Envelope response = await _requestService.sendAndAwait(
      messageType: ProtocolMessageType.pairingCancel,
      payload: const <String, dynamic>{},
      expectedType: ProtocolMessageType.pairingOutcome,
      policy: const RequestPolicy(
        retrySafe: true,
        requiredTrustState: DovahLinkTrustState.unpaired,
        timeoutClass: TimeoutClass.short,
      ),
    );
    final PairingOutcomePayload outcome = ProtocolPayloadDecoder.decode(
      PairingOutcomePayload.fromJson,
      response.payload,
    );
    final PairingCancelStatus? status = PairingCancelStatus.fromOutcome(
      outcome.outcome,
    );
    if (status == null) {
      throw DovahLinkProtocolException(
        code: ProtocolErrorCode.malformedMessage,
        message: 'Unexpected pairing_cancel outcome: ${outcome.outcome}',
        retryable: false,
      );
    }
    return PairingCancelOutcome(status: status);
  }

  /// Implements [IPairingService.confirmPairingCode].
  @override
  Future<String> confirmPairingCode({
    required String code,
    String? displayName,
  }) async {
    final PairingConfirmPayload payload = PairingConfirmPayload(
      code: code,
      displayName: displayName,
    );
    final Envelope response = await _requestService.sendAndAwait(
      messageType: ProtocolMessageType.pairingConfirm,
      payload: payload.toJson(),
      expectedType: ProtocolMessageType.pairingOutcome,
      policy: const RequestPolicy(
        retrySafe: false,
        requiredTrustState: DovahLinkTrustState.unpaired,
        timeoutClass: TimeoutClass.normal,
      ),
    );
    final PairingOutcomePayload outcome = ProtocolPayloadDecoder.decode(
      PairingOutcomePayload.fromJson,
      response.payload,
    );
    final bool isConfirmOutcome = switch (outcome.outcome) {
      PairingOutcome.credentialIssued ||
      PairingOutcome.expired ||
      PairingOutcome.invalid ||
      PairingOutcome.pacingLimited ||
      PairingOutcome.hardLimitReached ||
      PairingOutcome.pairingInvalidated => true,
      _ => false,
    };
    if (!isConfirmOutcome) {
      throw DovahLinkProtocolException(
        code: ProtocolErrorCode.malformedMessage,
        message: 'Unexpected pairing_confirm outcome: ${outcome.outcome}',
        retryable: false,
      );
    }
    if (outcome.outcome != PairingOutcome.credentialIssued) {
      throw DovahLinkPairingException(
        outcome.outcome,
        retryAfterSeconds: outcome.retryAfterSeconds,
      );
    }
    final String? credential = outcome.credential;
    if (credential == null) {
      throw const DovahLinkProtocolException(
        code: ProtocolErrorCode.malformedMessage,
        message: 'The bridge reported credential_issued with no credential.',
        retryable: false,
      );
    }

    final PersistedClientState state = await _storage.load();
    await _storage.save(
      state.copyWith(
        credential: credential,
        recoveryState: PairingRecoveryState.confirming,
      ),
    );
    return credential;
  }

  /// Implements [IPairingService.acknowledgeTrustedCredential].
  @override
  Future<void> acknowledgeTrustedCredential(String credential) async {
    final PairingAckPayload payload = PairingAckPayload(credential: credential);
    final Envelope response = await _requestService.sendAndAwait(
      messageType: ProtocolMessageType.pairingAck,
      payload: payload.toJson(),
      expectedType: ProtocolMessageType.pairingOutcome,
      policy: const RequestPolicy(
        retrySafe: true,
        requiredTrustState: DovahLinkTrustState.unpaired,
        timeoutClass: TimeoutClass.short,
      ),
    );
    final PairingOutcomePayload outcome = ProtocolPayloadDecoder.decode(
      PairingOutcomePayload.fromJson,
      response.payload,
    );
    final bool isAckOutcome = switch (outcome.outcome) {
      PairingOutcome.trusted ||
      PairingOutcome.alreadyTrusted ||
      PairingOutcome.pendingNotFound ||
      PairingOutcome.pairingInvalidated => true,
      _ => false,
    };
    if (!isAckOutcome) {
      throw DovahLinkProtocolException(
        code: ProtocolErrorCode.malformedMessage,
        message: 'Unexpected pairing_ack outcome: ${outcome.outcome}',
        retryable: false,
      );
    }
    if (outcome.outcome == PairingOutcome.pendingNotFound ||
        outcome.outcome == PairingOutcome.pairingInvalidated) {
      throw DovahLinkPairingException(
        outcome.outcome,
        retryAfterSeconds: outcome.retryAfterSeconds,
      );
    }
    _sessionTrustService.markTrusted();

    final PersistedClientState state = await _storage.load();
    await _storage.save(
      state.copyWith(recoveryState: PairingRecoveryState.none),
    );
  }

  /// Implements [IPairingService.recoverPendingPairing].
  @override
  Future<DovahLinkTrustState> recoverPendingPairing() async {
    final PersistedClientState state = await _storage.load();
    if (state.recoveryState != PairingRecoveryState.confirming ||
        state.credential == null) {
      return DovahLinkTrustState.unpaired;
    }

    try {
      await acknowledgeTrustedCredential(state.credential!);
      return DovahLinkTrustState.trusted;
    } on DovahLinkPairingException catch (error) {
      if (error.outcome == PairingOutcome.pendingNotFound ||
          error.outcome == PairingOutcome.pairingInvalidated) {
        await _storage.save(PersistedClientState(clientId: state.clientId));
        return DovahLinkTrustState.unpaired;
      }
      rethrow;
    }
  }
}
