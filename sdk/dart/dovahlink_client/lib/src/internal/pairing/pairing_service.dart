import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host_identity_mismatch_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_pairing_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/persistence/client_state_service.dart';
import 'package:dovahlink_client_sdk/src/internal/protocol_payload_decoder.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/request_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_trust_service.dart';
import 'package:dovahlink_client_sdk/src/pairing_cancel_outcome.dart';
import 'package:dovahlink_client_sdk/src/pairing_challenge_status.dart';
import 'package:dovahlink_client_sdk/src/pairing_renotify_result.dart';
import 'package:dovahlink_client_sdk/src/persistence/pending_pairing_recovery.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_known_host.dart';
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

  /// Submits the six-digit code the user read from Skyrim. Durably persists the issued credential,
  /// current Host relationship, and Host-owned `CONFIRMING` recovery record in one write, per
  /// `ai/context/protocol/security.md`'s "client durably persists its issued credential and its
  /// `CONFIRMING` recovery state before sending final confirmation."
  /// The credential remains inside its Host relationship in SDK-owned state.
  /// @param code The six-digit code shown by Skyrim.
  /// @param displayName The optional Client display name for Host pairing metadata.
  /// @throws [DovahLinkPairingException] if the code was expired, invalid, paced too soon, hit
  ///     the hard wrong-attempt limit, or an administrative mutation invalidated the challenge
  ///     after it began but before this call was evaluated.
  /// @throws [DovahLinkConnectionException] if the active session has no current Host context.
  Future<void> confirmPairingCode({required String code, String? displayName});

  /// Echoes back the pending Host-scoped credential internally, completing pairing. The session's
  /// trust state becomes trusted on success, and the recovery record clears while the relationship
  /// and credential remain persisted.
  /// @throws [DovahLinkPairingException] if the Host has no matching pending confirmation or
  ///     an administrative mutation invalidated the pending credential.
  Future<void> acknowledgeTrustedCredential();

  /// Resumes an interrupted pairing confirmation after a crash or relaunch, per
  /// `ai/context/protocol/security.md`'s "a client that saves the credential but crashes before
  /// confirming retries confirmation on restart." Call after `hello` admits an `unpaired` session.
  ///
  /// A no-op returning [DovahLinkTrustState.unpaired] when no confirmation is outstanding. When
  /// one is, retries [acknowledgeTrustedCredential] with its owning Host credential: a
  /// `pending_not_found` outcome (the Host restarted and lost the pending credential) or
  /// `pairing_invalidated` outcome (an administrative mutation rejected the pending credential)
  /// discards the owning Host's credential and recovery record rather than treating that as a fatal
  /// error; any other failure leaves the `CONFIRMING` recovery record untouched so a later relaunch
  /// can retry. Invalidated confirmation clears that Host's credential while preserving its
  /// Known Host metadata.
  Future<DovahLinkTrustState> recoverPendingPairing();
}

/// Implements [IPairingService], per `ai/context/sdk/architecture.md`'s "Internal composition".
/// Every collaborator is supplied by the caller per `ai/context/sdk/architecture.md`'s
/// "Dependency injection" -- this class never constructs one of its own dependencies.
class PairingService implements IPairingService {
  /// Reads the Host context owned by the active session.
  final ISessionService _sessionService;

  /// Upgrades trust standing once a pairing acknowledgement succeeds -- the only class permitted
  /// to.
  final ISessionTrustService _sessionTrustService;

  /// Sends a pairing message and awaits its correlated reply.
  final IRequestService _requestService;

  /// The owner of persisted credentials, recovery state, and Known Host metadata.
  final IClientStateService _clientStateService;

  /// Creates a pairing service over [sessionService], [sessionTrustService], [requestService], and
  /// [clientStateService].
  /// @param sessionService Reads the current Host context from the admitted session.
  /// @param sessionTrustService Upgrades the session after a successful pairing acknowledgement.
  /// @param requestService Sends pairing messages.
  /// @param clientStateService Atomically persists client credentials and Host association.
  PairingService({
    required ISessionService sessionService,
    required ISessionTrustService sessionTrustService,
    required IRequestService requestService,
    required IClientStateService clientStateService,
  }) : _sessionService = sessionService,
       _sessionTrustService = sessionTrustService,
       _requestService = requestService,
       _clientStateService = clientStateService;

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
  Future<void> confirmPairingCode({
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
        attemptsRemaining: outcome.attemptsRemaining,
      );
    }
    final String? credential = outcome.credential;
    if (credential == null) {
      throw const DovahLinkProtocolException(
        code: ProtocolErrorCode.malformedMessage,
        message: 'The host reported credential_issued with no credential.',
        retryable: false,
      );
    }

    final DovahLinkHost? currentHost = _sessionService.currentHost;
    if (currentHost == null) {
      throw const DovahLinkConnectionException(
        'The current Host context is unavailable.',
      );
    }
    await _clientStateService.updateState((PersistedClientState state) {
      final PendingPairingRecovery? pending = state.pendingPairingRecovery;
      if (pending != null && pending.hostId != currentHost.hostId) {
        throw DovahLinkHostIdentityMismatchException(
          knownHostId: pending.hostId,
          reportedHostId: currentHost.hostId,
        );
      }
      return state.copyWith(
        knownHosts: <String, PersistedKnownHost>{
          ...state.knownHosts,
          currentHost.hostId: PersistedKnownHost(
            host: currentHost,
            credential: credential,
          ),
        },
        pendingPairingRecovery: PendingPairingRecovery(
          hostId: currentHost.hostId,
          state: PairingRecoveryState.confirming,
        ),
      );
    });
  }

  /// Implements [IPairingService.acknowledgeTrustedCredential].
  @override
  Future<void> acknowledgeTrustedCredential() async {
    final DovahLinkHost? currentHost = _sessionService.currentHost;
    if (currentHost == null) {
      throw const DovahLinkConnectionException(
        'The current Host context is unavailable.',
      );
    }
    final PersistedClientState state = await _clientStateService.load();
    final PendingPairingRecovery? recovery = state.pendingPairingRecovery;
    if (recovery == null) {
      throw const DovahLinkPairingException(PairingOutcome.pendingNotFound);
    }
    if (recovery.hostId != currentHost.hostId) {
      throw DovahLinkHostIdentityMismatchException(
        knownHostId: recovery.hostId,
        reportedHostId: currentHost.hostId,
      );
    }
    final String? credential = state.knownHosts[currentHost.hostId]?.credential;
    if (credential == null) {
      throw const DovahLinkPairingException(PairingOutcome.pendingNotFound);
    }
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
    await _clientStateService.updateState((PersistedClientState state) {
      final PersistedKnownHost? relationship =
          state.knownHosts[currentHost.hostId];
      if (relationship == null) {
        throw const DovahLinkPairingException(PairingOutcome.pendingNotFound);
      }
      return state.copyWith(
        knownHosts: <String, PersistedKnownHost>{
          ...state.knownHosts,
          currentHost.hostId: PersistedKnownHost(
            host: relationship.host,
            credential: relationship.credential,
          ),
        },
        clearPendingPairingRecovery: true,
      );
    });
    _sessionTrustService.markTrusted();
  }

  /// Implements [IPairingService.recoverPendingPairing].
  @override
  Future<DovahLinkTrustState> recoverPendingPairing() async {
    final PersistedClientState state = await _clientStateService.load();
    final PendingPairingRecovery? recovery = state.pendingPairingRecovery;
    if (recovery == null) {
      return DovahLinkTrustState.unpaired;
    }
    final String? currentHostId = _sessionService.currentHost?.hostId;
    if (currentHostId == null) {
      throw const DovahLinkConnectionException(
        'The current Host context is unavailable.',
      );
    }
    if (currentHostId != recovery.hostId) {
      throw DovahLinkHostIdentityMismatchException(
        knownHostId: recovery.hostId,
        reportedHostId: currentHostId,
      );
    }

    try {
      await acknowledgeTrustedCredential();
      return DovahLinkTrustState.trusted;
    } on DovahLinkPairingException catch (error) {
      if (error.outcome == PairingOutcome.pendingNotFound ||
          error.outcome == PairingOutcome.pairingInvalidated) {
        await _clientStateService.updateState((PersistedClientState current) {
          final PersistedKnownHost relationship =
              current.knownHosts[recovery.hostId]!;
          return current.copyWith(
            knownHosts: <String, PersistedKnownHost>{
              ...current.knownHosts,
              recovery.hostId: PersistedKnownHost(host: relationship.host),
            },
            clearPendingPairingRecovery: true,
          );
        });
        return DovahLinkTrustState.unpaired;
      }
      rethrow;
    }
  }
}
