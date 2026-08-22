import '../dovahlink_client_exception.dart';
import '../pairing_cancel_outcome.dart';
import '../pairing_challenge_status.dart';
import '../pairing_renotify_result.dart';
import '../persistence/client_storage.dart';
import '../persistence/persisted_client_state.dart';
import '../protocol/envelope.dart';
import '../protocol/pairing_ack_payload.dart';
import '../protocol/pairing_confirm_payload.dart';
import '../protocol/pairing_outcome_payload.dart';
import '../protocol/pairing_status_payload.dart';
import '../protocol/protocol_format_exception.dart';
import '../request_policy.dart';
import '../shared/enums.dart';
import 'message_receiver.dart';
import 'request_manager.dart';
import 'session_trust_writer.dart';

/// Owns pairing operations, per `ai/context/sdk/architecture.md`'s "Internal composition":
/// starting/querying a pairing challenge, redisplaying or cancelling it, confirming a code,
/// acknowledging the issued credential, and resuming an interrupted confirmation after a crash or
/// relaunch.
class PairingService {
  /// Creates a pairing service sending through [requestManager], persisting credential and
  /// pairing recovery state through [storage], upgrading trust through [sessionTrustWriter], and
  /// ensuring the connection is receiving through [messageReceiver].
  PairingService({
    required RequestManager requestManager,
    required ClientStorage storage,
    required SessionTrustWriter sessionTrustWriter,
    required MessageReceiver messageReceiver,
  }) : _requestManager = requestManager,
       _storage = storage,
       _sessionTrustWriter = sessionTrustWriter,
       _messageReceiver = messageReceiver;

  /// Sends a pairing message and awaits its correlated reply.
  final RequestManager _requestManager;

  /// The SDK-owned persistence boundary for this client's credential and pairing recovery state.
  final ClientStorage _storage;

  /// Upgrades trust standing once a pairing acknowledgement succeeds.
  final SessionTrustWriter _sessionTrustWriter;

  /// Ensures the transport's inbound message stream is being read before a pairing message sends.
  final MessageReceiver _messageReceiver;

  /// Starts, or queries the status of, a pairing challenge. Valid only on an `unpaired` session.
  /// [PairingChallengeStatus.availability] being [PairingAvailability.otherDevicePairing] means a
  /// different clientId currently owns the active challenge or pending credential.
  Future<PairingChallengeStatus> requestPairing() async {
    _messageReceiver.ensureReceiving();
    final Envelope response = await _requestManager.sendAndAwait(
      messageType: 'pairing_request',
      payload: const <String, dynamic>{},
      expectedType: 'pairing_status',
      policy: const RequestPolicy(
        retrySafe: true,
        requiredTrustState: DovahLinkTrustState.unpaired,
        timeoutClass: TimeoutClass.short,
      ),
    );
    final PairingStatusPayload status;
    try {
      status = PairingStatusPayload.fromJson(response.payload);
    } on ProtocolFormatException catch (error) {
      _throwMalformedMessage(error);
    }
    return PairingChallengeStatus(
      availability: status.state,
      expiresInSeconds: status.expiresInSeconds,
    );
  }

  /// Requests redisplay of the active pairing challenge's code the caller owns. Never generates a
  /// new code and never sends the code itself over the wire -- redisplay occurs through the
  /// in-game notification, not the connection. Valid only on an `unpaired` session.
  Future<PairingRenotifyResult> requestPairingRenotify() async {
    _messageReceiver.ensureReceiving();
    final Envelope response = await _requestManager.sendAndAwait(
      messageType: 'pairing_renotify',
      payload: const <String, dynamic>{},
      expectedType: 'pairing_outcome',
      policy: const RequestPolicy(
        retrySafe: true,
        requiredTrustState: DovahLinkTrustState.unpaired,
        timeoutClass: TimeoutClass.short,
      ),
    );
    final PairingOutcomePayload outcome;
    try {
      outcome = PairingOutcomePayload.fromJson(response.payload);
    } on ProtocolFormatException catch (error) {
      _throwMalformedMessage(error);
    }
    return PairingRenotifyResult(
      status: _parsePairingRenotifyStatus(outcome.outcome),
      retryAfterSeconds: outcome.retryAfterSeconds,
    );
  }

  /// Gives up an owned active challenge or pending credential, freeing the slot for a fresh
  /// [requestPairing]. Never touches persisted trust or an already-committed credential. Valid
  /// only on an `unpaired` session.
  Future<PairingCancelOutcome> cancelPairing() async {
    _messageReceiver.ensureReceiving();
    final Envelope response = await _requestManager.sendAndAwait(
      messageType: 'pairing_cancel',
      payload: const <String, dynamic>{},
      expectedType: 'pairing_outcome',
      policy: const RequestPolicy(
        retrySafe: true,
        requiredTrustState: DovahLinkTrustState.unpaired,
        timeoutClass: TimeoutClass.short,
      ),
    );
    final PairingOutcomePayload outcome;
    try {
      outcome = PairingOutcomePayload.fromJson(response.payload);
    } on ProtocolFormatException catch (error) {
      _throwMalformedMessage(error);
    }
    return PairingCancelOutcome(
      status: _parsePairingCancelStatus(outcome.outcome),
    );
  }

  /// Submits the six-digit code the user read from Skyrim. Durably persists the issued credential
  /// and a `CONFIRMING` recovery state before returning it, per
  /// `ai/context/protocol/security.md`'s "client durably persists its issued credential and its
  /// `CONFIRMING` recovery state before sending final confirmation."
  /// @return The issued credential, already persisted.
  /// @throws DovahLinkPairingException if the code was expired, invalid, paced too soon, or
  ///     hit the hard wrong-attempt limit.
  Future<String> confirmPairingCode({
    required String code,
    String? displayName,
  }) async {
    final PairingConfirmPayload payload = PairingConfirmPayload(
      code: code,
      displayName: displayName,
    );
    _messageReceiver.ensureReceiving();
    final Envelope response = await _requestManager.sendAndAwait(
      messageType: 'pairing_confirm',
      payload: payload.toJson(),
      expectedType: 'pairing_outcome',
      policy: const RequestPolicy(
        retrySafe: false,
        requiredTrustState: DovahLinkTrustState.unpaired,
        timeoutClass: TimeoutClass.normal,
      ),
    );
    final PairingOutcomePayload outcome;
    try {
      outcome = PairingOutcomePayload.fromJson(response.payload);
    } on ProtocolFormatException catch (error) {
      _throwMalformedMessage(error);
    }
    if (outcome.outcome != PairingOutcome.credentialIssued) {
      throw DovahLinkPairingException(outcome.outcome);
    }
    final String? credential = outcome.credential;
    if (credential == null) {
      throw const DovahLinkProtocolException(
        code: 'malformed_message',
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

  /// Echoes back a [credential] durably saved from [confirmPairingCode], completing pairing.
  /// The session's trust state becomes [DovahLinkTrustState.trusted] on success, and the
  /// persisted recovery state clears back to [PairingRecoveryState.none] while keeping the
  /// credential.
  /// @throws DovahLinkPairingException if the bridge has no matching pending confirmation.
  Future<void> acknowledgeTrustedCredential(String credential) async {
    final PairingAckPayload payload = PairingAckPayload(credential: credential);
    _messageReceiver.ensureReceiving();
    final Envelope response = await _requestManager.sendAndAwait(
      messageType: 'pairing_ack',
      payload: payload.toJson(),
      expectedType: 'pairing_outcome',
      policy: const RequestPolicy(
        retrySafe: true,
        requiredTrustState: DovahLinkTrustState.unpaired,
        timeoutClass: TimeoutClass.short,
      ),
    );
    final PairingOutcomePayload outcome;
    try {
      outcome = PairingOutcomePayload.fromJson(response.payload);
    } on ProtocolFormatException catch (error) {
      _throwMalformedMessage(error);
    }
    if (outcome.outcome != PairingOutcome.trusted &&
        outcome.outcome != PairingOutcome.alreadyTrusted) {
      throw DovahLinkPairingException(outcome.outcome);
    }
    _sessionTrustWriter.markTrusted();

    final PersistedClientState state = await _storage.load();
    await _storage.save(
      state.copyWith(recoveryState: PairingRecoveryState.none),
    );
  }

  /// Resumes an interrupted pairing confirmation after a crash or relaunch, per
  /// `ai/context/protocol/security.md`'s "a client that saves the credential but crashes before
  /// confirming retries confirmation on restart." Call after `hello` admits an `unpaired` session.
  ///
  /// A no-op returning [DovahLinkTrustState.unpaired] when no confirmation is outstanding. When
  /// one is, retries [acknowledgeTrustedCredential] with the stored credential: a
  /// `pending_not_found` outcome (the bridge restarted and lost the pending credential) discards
  /// the local credential and resets to unpaired rather than treating that as a fatal error; any
  /// other failure leaves the `CONFIRMING` state untouched so a later relaunch can retry again.
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
      if (error.outcome == PairingOutcome.pendingNotFound) {
        await _storage.save(PersistedClientState(clientId: state.clientId));
        return DovahLinkTrustState.unpaired;
      }
      rethrow;
    }
  }

  /// Interprets a [PairingOutcome] returned in reply to `pairing_renotify`. Every [PairingOutcome]
  /// value is a recognized wire value -- decoded and validated as a closed enum by
  /// `PairingOutcomePayload.fromJson` -- but only a subset is a valid reply to this specific
  /// exchange; a value valid elsewhere (for example [PairingOutcome.credentialIssued]) is still a
  /// protocol violation here.
  PairingRenotifyStatus _parsePairingRenotifyStatus(PairingOutcome outcome) =>
      switch (outcome) {
        PairingOutcome.renotified => PairingRenotifyStatus.renotified,
        PairingOutcome.renotifyCooldown => PairingRenotifyStatus.cooldown,
        PairingOutcome.alreadyIdle => PairingRenotifyStatus.alreadyIdle,
        _ => throw DovahLinkProtocolException(
          code: 'malformed_message',
          message: 'Unexpected pairing_renotify outcome: $outcome',
          retryable: false,
        ),
      };

  /// Interprets a [PairingOutcome] returned in reply to `pairing_cancel`; see
  /// [_parsePairingRenotifyStatus] for why an otherwise-valid [PairingOutcome] can still be
  /// rejected here.
  PairingCancelStatus _parsePairingCancelStatus(PairingOutcome outcome) =>
      switch (outcome) {
        PairingOutcome.cancelled => PairingCancelStatus.cancelled,
        PairingOutcome.alreadyIdle => PairingCancelStatus.alreadyIdle,
        _ => throw DovahLinkProtocolException(
          code: 'malformed_message',
          message: 'Unexpected pairing_cancel outcome: $outcome',
          retryable: false,
        ),
      };

  /// Throws the SDK's public `DovahLinkProtocolException(code: 'malformed_message', retryable:
  /// false)` translated from a DTO decode boundary failure, per
  /// `ai/context/sdk/api-design.md`'s "Protocol DTO decoding" boundary translation.
  Never _throwMalformedMessage(ProtocolFormatException error) =>
      throw DovahLinkProtocolException(
        code: 'malformed_message',
        message: error.message,
        retryable: false,
      );
}
