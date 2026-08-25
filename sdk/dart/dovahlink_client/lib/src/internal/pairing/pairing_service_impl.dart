import 'package:dovahlink_client_sdk/src/dovahlink_pairing_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/pairing/pairing_service.dart';
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

/// Implements [PairingService], per `ai/context/sdk/architecture.md`'s "Internal composition".
/// Every collaborator ([SessionTrustService], [RequestService], [ClientStorage]) is supplied by
/// the caller per `ai/context/sdk/architecture.md`'s "Dependency injection" -- this class never
/// constructs one of its own dependencies.
class PairingServiceImpl implements PairingService {
  /// Upgrades trust standing once a pairing acknowledgement succeeds -- the only class permitted
  /// to.
  final SessionTrustService _sessionTrustService;

  /// Sends a pairing message and awaits its correlated reply.
  final RequestService _requestService;

  /// The SDK-owned persistence boundary for this client's credential and pairing recovery state.
  final ClientStorage _storage;

  /// Creates a pairing service over [sessionTrustService], [requestService], and [storage].
  PairingServiceImpl({
    required SessionTrustService sessionTrustService,
    required RequestService requestService,
    required ClientStorage storage,
  }) : _sessionTrustService = sessionTrustService,
       _requestService = requestService,
       _storage = storage;

  /// Implements [PairingService.requestPairing].
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

  /// Implements [PairingService.requestPairingRenotify].
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

  /// Implements [PairingService.cancelPairing].
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

  /// Implements [PairingService.confirmPairingCode].
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
      PairingOutcome.hardLimitReached => true,
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

  /// Implements [PairingService.acknowledgeTrustedCredential].
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

  /// Implements [PairingService.recoverPendingPairing].
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
