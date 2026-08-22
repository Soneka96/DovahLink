import 'dart:math';

import 'package:meta/meta.dart';

import 'dovahlink_client_exception.dart';
import 'hello_result.dart';
import 'internal/client_session.dart';
import 'internal/request_manager.dart';
import 'pairing_cancel_outcome.dart';
import 'pairing_challenge_status.dart';
import 'pairing_renotify_result.dart';
import 'persistence/client_storage.dart';
import 'persistence/persisted_client_state.dart';
import 'persistence/windows/dpapi_client_storage.dart';
import 'protocol/envelope.dart';
import 'protocol/hello_ack_payload.dart';
import 'protocol/hello_payload.dart';
import 'protocol/pairing_ack_payload.dart';
import 'protocol/pairing_confirm_payload.dart';
import 'protocol/pairing_outcome_payload.dart';
import 'protocol/pairing_status_payload.dart';
import 'protocol/protocol_format_exception.dart';
import 'request_policy.dart';
import 'shared/constants.dart';
import 'shared/enums.dart';
import 'transport/dovahlink_transport.dart';
import 'transport/websocket_transport.dart';

/// A real, Flutter/Redux-independent DovahLink protocol client: connect, authenticate, pair, and
/// disconnect. Owns its local `clientId`, pairing credential, and `CONFIRMING` recovery state
/// behind [ClientStorage] -- see `ai/context/sdk/persistence.md`'s ownership rule -- so a consumer
/// never threads identity or credential material through this API by hand.
///
/// Never exposes raw JSON or transport details: every method takes and returns typed values.
class DovahLinkClient {
  /// Creates a client. [transport] defaults to a real [WebSocketTransport]; inject a fake for
  /// deterministic tests. [storage] is required so every consumer makes its persistence choice
  /// explicit; see [DovahLinkClient.windows] for the real Windows-backed convenience factory.
  DovahLinkClient({
    DovahLinkTransport? transport,
    required ClientStorage storage,
  }) : _storage = storage {
    _session = ClientSession(
      transport: transport ?? WebSocketTransport(),
      timeoutDurations: kTimeoutClassDurations,
    );
    _requestManager = _session.requestManager;
  }

  /// Creates a client backed by real infrastructure: a [WebSocketTransport] and a
  /// [DpapiClientStorage] persisting to this Windows user's default per-user location.
  factory DovahLinkClient.windows() =>
      DovahLinkClient(storage: DpapiClientStorage());

  /// Creates a client with directly-injected [timeoutDurations], bypassing the centralized
  /// production defaults in `shared/constants.dart`. Test-only: production code must always use
  /// the unnamed constructor so every operation shares the same centrally tuned timeout policy.
  @visibleForTesting
  DovahLinkClient.withTimeoutDurations({
    required DovahLinkTransport transport,
    required ClientStorage storage,
    required Map<TimeoutClass, Duration> timeoutDurations,
  }) : _storage = storage {
    _session = ClientSession(
      transport: transport,
      timeoutDurations: timeoutDurations,
    );
    _requestManager = _session.requestManager;
  }

  /// The SDK-owned persistence boundary for this client's identity, credential, and pairing
  /// recovery state.
  final ClientStorage _storage;

  /// Owns transport lifecycle, connection state, and stream ownership -- the sole owner of every
  /// socket-scoped field this client has; see `ai/context/sdk/architecture.md`'s "Session-state
  /// ownership". This façade never assigns session state directly, only through [ClientSession]'s
  /// own commands ([ClientSession.admitSession], [ClientSession.markTrusted]).
  late final ClientSession _session;

  /// Owns pending requests, timeouts, and retry behavior. The same instance [_session] itself
  /// uses internally, exposed via [ClientSession.requestManager] so this façade (and, in a later
  /// extraction step, `AuthenticationService`/`PairingService`) can send requests directly without
  /// depending on the whole session.
  late final RequestManager _requestManager;

  /// Source of randomness for this installation's `clientId`, generated on first use.
  final Random _random = Random.secure();

  /// This installation's stable client ID, or `null` before [hello] has resolved it.
  String? _clientId;

  /// The DovahLink Bridge/mod release version reported by the last successful [hello], or `null`
  /// before [hello] succeeds. Cached so [authenticate] can report it again without re-sending
  /// `hello` on an already-admitted session.
  String? _bridgeVersion;

  /// The current connection lifecycle phase.
  DovahLinkConnectionState get connectionState => _session.connectionState;

  /// The current trust standing, or `null` before [hello] succeeds.
  DovahLinkTrustState? get trustState => _session.currentTrustState;

  /// The server-issued session identifier, or `null` before [hello] succeeds.
  String? get sessionId => _session.currentSessionId;

  /// This installation's stable client ID, or `null` before [hello] has resolved it.
  String? get clientId => _clientId;

  /// The reason [connectionState] is [DovahLinkConnectionState.administrativelyInvalidated], or
  /// `null` otherwise.
  AdministrativeInvalidationReason? get invalidationReason =>
      _session.invalidationReason;

  /// Establishes the transport connection to [uri]. Must be called before [hello].
  /// @throws DovahLinkConnectionException if the socket cannot be established.
  Future<void> connect(Uri uri) => _session.connect(uri);

  /// Sends `hello` and negotiates the session. Resolves and persists this installation's
  /// `clientId` on first use, and automatically presents a stored trusted credential as
  /// `trusted_device_credential` for an ordinary reconnect. Admits `unpaired` both when no
  /// credential is stored yet and when a `CONFIRMING` pairing is still outstanding -- the bridge
  /// has not yet committed that credential as trusted, so it must not be presented as one. Once
  /// the new session's trust state is known, retransmits any retry-safe operation an earlier
  /// ordinary transport loss orphaned, provided the new session still satisfies its required
  /// trust state; see [RequestPolicy.requiredTrustState] and [ClientSession.admitSession].
  /// @throws DovahLinkProtocolException if the bridge rejects authentication.
  Future<HelloResult> hello() async {
    final PersistedClientState state = await _storage.load();
    final String clientId = await _resolveClientId(state);
    final String? credential = state.recoveryState == PairingRecoveryState.none
        ? state.credential
        : null;
    _clientId = clientId;

    final HelloPayload payload = HelloPayload(
      clientId: clientId,
      authMethod: credential == null
          ? AuthMethod.unpaired
          : AuthMethod.trustedDeviceCredential,
      authToken: credential,
    );
    try {
      _session.ensureReceiving();
      final Envelope response = await _requestManager.sendAndAwait(
        messageType: 'hello',
        payload: payload.toJson(),
        expectedType: 'hello_ack',
        policy: const RequestPolicy(
          retrySafe: false,
          requiredTrustState: null,
          timeoutClass: TimeoutClass.normal,
        ),
      );
      final HelloAckPayload ack = HelloAckPayload.fromJson(response.payload);
      final DovahLinkTrustState trustState = switch (ack.clientIdentityKind) {
        ClientIdentityKind.unpaired => DovahLinkTrustState.unpaired,
        ClientIdentityKind.paired => DovahLinkTrustState.trusted,
      };

      final String? sessionId = response.sessionId;
      if (sessionId == null) {
        // hello_ack always carries a real sessionId per `protocol/schema/README.md`; a null one
        // is a malformed reply, not a state ClientSession.admitSession's typed contract accepts
        // silently the way the pre-extraction field assignment once did.
        throw const DovahLinkProtocolException(
          code: 'malformed_message',
          message: 'The bridge reported hello_ack with no sessionId.',
          retryable: false,
        );
      }
      // admitSession also retransmits any retry-safe operation an earlier ordinary transport
      // loss orphaned, now that this new session's trust state is known -- see
      // ClientSession.admitSession's documentation.
      _session.admitSession(sessionId: sessionId, trustState: trustState);
      _bridgeVersion = ack.bridgeVersion;

      // The bridge always sends an unprompted `capabilities` message right after `hello_ack`; it
      // arrives as an unsolicited (null-correlationId) message and is discarded by
      // MessageRouter -- exposing it is out of this client's current scope. hello() does not
      // wait for it.

      return HelloResult(
        bridgeVersion: ack.bridgeVersion,
        trustState: trustState,
      );
    } on ProtocolFormatException catch (error) {
      // A DTO decode boundary failure (for example an unrecognized clientIdentityKind) is a
      // protocol-level anomaly, not ordinary connectivity loss -- translated to the typed
      // exception this client's callers already handle, per `ai/context/sdk/api-design.md`'s
      // "Protocol DTO decoding" boundary translation, rather than leaking the DTO-layer type.
      await disconnect();
      _throwMalformedMessage(error);
    } on Object {
      // Every HandleHello failure path closes the connection (handshake_handler.cpp's Fail()
      // always sets closeConnection), and a genuine transport failure leaves the socket equally
      // unusable either way -- reset so the next connect() attempt does not find a stale socket
      // WebSocketTransport still considers open (its "Already connected" guard in connect()).
      // disconnect() itself never throws, so this cannot mask the error being rethrown below.
      await disconnect();
      rethrow;
    }
  }

  /// Connects to [uri] and authenticates, recovering from a rejected `trusted_device_credential`
  /// hello (`revoked` or an unrecognized credential) by discarding it and retrying once as
  /// `unpaired` -- the bridge always accepts that, so a recoverable rejection never surfaces as a
  /// thrown exception here. [HelloResult.recoveredFromRejectedCredential] reports whether that
  /// happened and why, so a caller can still explain it to the user. A transport failure, a
  /// non-recoverable protocol rejection, or the retry attempt's own failure still throws normally.
  ///
  /// A no-op that returns the cached result of the last [hello] when this client is already
  /// [DovahLinkConnectionState.connected] and [DovahLinkTrustState.trusted] -- the bridge's
  /// one-session-per-connection limit (`handshake_handler.cpp`'s `TryCreateSession`) rejects a
  /// second `hello` on a socket that already holds a session, so re-authenticating an
  /// already-trusted, still-open connection must not re-send one.
  /// @throws DovahLinkConnectionException if the socket cannot be established (initial or retry).
  /// @throws DovahLinkProtocolException if hello is rejected for a non-recoverable reason, or the
  ///     retry attempt is itself rejected.
  Future<HelloResult> authenticate(Uri uri) async {
    final String? cachedBridgeVersion = _bridgeVersion;
    if (_session.connectionState == DovahLinkConnectionState.connected &&
        _session.currentTrustState == DovahLinkTrustState.trusted &&
        cachedBridgeVersion != null) {
      return HelloResult(
        bridgeVersion: cachedBridgeVersion,
        trustState: DovahLinkTrustState.trusted,
      );
    }
    await connect(uri);
    try {
      return await hello();
    } on DovahLinkProtocolException catch (error) {
      final CredentialRejectionReason? reason = _credentialRejectionReason(
        error.code,
      );
      if (reason == null) {
        rethrow;
      }
      await forgetCredential();
      await connect(uri);
      final HelloResult result = await hello();
      return HelloResult(
        bridgeVersion: result.bridgeVersion,
        trustState: result.trustState,
        recoveredFromRejectedCredential: reason,
      );
    }
  }

  /// Starts, or queries the status of, a pairing challenge. Valid only on an `unpaired` session.
  /// [PairingChallengeStatus.availability] being [PairingAvailability.otherDevicePairing] means a
  /// different clientId currently owns the active challenge or pending credential.
  Future<PairingChallengeStatus> requestPairing() async {
    _session.ensureReceiving();
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
    _session.ensureReceiving();
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
    _session.ensureReceiving();
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
    _session.ensureReceiving();
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
  /// [trustState] becomes [DovahLinkTrustState.trusted] on success, and the persisted recovery
  /// state clears back to [PairingRecoveryState.none] while keeping the credential.
  /// @throws DovahLinkPairingException if the bridge has no matching pending confirmation.
  Future<void> acknowledgeTrustedCredential(String credential) async {
    final PairingAckPayload payload = PairingAckPayload(credential: credential);
    _session.ensureReceiving();
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
    _session.markTrusted();

    final PersistedClientState state = await _storage.load();
    await _storage.save(
      state.copyWith(recoveryState: PairingRecoveryState.none),
    );
  }

  /// Resumes an interrupted pairing confirmation after a crash or relaunch, per
  /// `ai/context/protocol/security.md`'s "a client that saves the credential but crashes before
  /// confirming retries confirmation on restart." Call after [hello] admits an `unpaired` session.
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

  /// Closes the connection and resets in-memory session state. Idempotent, and never throws: this
  /// is a best-effort cleanup operation, matching [DovahLinkTransport.close]'s own "Idempotent"
  /// contract. In-memory state resets even when the underlying transport cannot be closed
  /// cleanly -- a broken close must not leave [connectionState]/[trustState]/[sessionId] lying
  /// about a session that no longer exists. Persisted identity, credential, and recovery state are
  /// untouched -- trust survives a disconnect. Fails any operation still awaiting a reply, and any
  /// operation an earlier transport loss orphaned for retry, instead of leaving it to hang
  /// forever: unlike an unexpected transport loss, a deliberate disconnect never retries. A no-op
  /// when [connectionState] is already
  /// [DovahLinkConnectionState.disconnected]/[DovahLinkConnectionState.administrativelyInvalidated] --
  /// an administrative invalidation's typed reason is preserved, not reset to a generic
  /// disconnect.
  Future<void> disconnect() => _session.disconnect();

  /// Discards the persisted pairing credential and recovery state while preserving [clientId], so
  /// the next [hello] presents [AuthMethod.unpaired] instead of a credential the bridge has
  /// already rejected. Call after a `trusted_device_credential` hello is rejected
  /// (`unauthenticated`/`revoked`) and before retrying -- this installation's identity is not
  /// itself invalid, only its stored credential. Does not touch the transport or in-memory
  /// connection state; call [disconnect] separately if the connection also needs resetting.
  Future<void> forgetCredential() async {
    final PersistedClientState state = await _storage.load();
    await _storage.save(PersistedClientState(clientId: state.clientId));
  }

  /// Returns the persisted `clientId`, generating and persisting a fresh RFC 4122 version-4 UUID
  /// on first use.
  Future<String> _resolveClientId(PersistedClientState state) async {
    final String? existing = state.clientId;
    if (existing != null && existing.isNotEmpty) {
      return existing;
    }
    final String generated = _generateUuidV4();
    await _storage.save(state.copyWith(clientId: generated));
    return generated;
  }

  /// Generates a random RFC 4122 version-4 UUID string.
  String _generateUuidV4() {
    final List<int> bytes = List<int>.generate(16, (_) => _random.nextInt(256));
    bytes[6] = (bytes[6] & 0x0f) | 0x40;
    bytes[8] = (bytes[8] & 0x3f) | 0x80;

    String hexRange(int start, int end) => bytes
        .sublist(start, end)
        .map((int byte) => byte.toRadixString(16).padLeft(2, '0'))
        .join();

    return '${hexRange(0, 4)}-${hexRange(4, 6)}-${hexRange(6, 8)}-'
        '${hexRange(8, 10)}-${hexRange(10, 16)}';
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

  /// Converts a rejected `trusted_device_credential` hello's wire error code into the typed
  /// reason [authenticate] recovers from, or `null` when [code] is not a recoverable rejection.
  CredentialRejectionReason? _credentialRejectionReason(String code) =>
      switch (code) {
        'revoked' => CredentialRejectionReason.revoked,
        'unauthenticated' => CredentialRejectionReason.unrecognized,
        _ => null,
      };
}
