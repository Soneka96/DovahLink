import 'dart:async';
import 'dart:convert';
import 'dart:math';

import 'package:meta/meta.dart';

import 'dovahlink_client_exception.dart';
import 'hello_result.dart';
import 'pairing_cancel_outcome.dart';
import 'pairing_challenge_status.dart';
import 'pairing_renotify_result.dart';
import 'persistence/client_storage.dart';
import 'persistence/persisted_client_state.dart';
import 'persistence/windows/dpapi_client_storage.dart';
import 'protocol/envelope.dart';
import 'protocol/error_payload.dart';
import 'protocol/hello_ack_payload.dart';
import 'protocol/hello_payload.dart';
import 'protocol/json_map.dart';
import 'protocol/pairing_ack_payload.dart';
import 'protocol/pairing_confirm_payload.dart';
import 'protocol/pairing_outcome_payload.dart';
import 'protocol/pairing_status_payload.dart';
import 'protocol/protocol_format_exception.dart';
import 'protocol/session_invalidated_payload.dart';
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
  }) : _transport = transport ?? WebSocketTransport(),
       _storage = storage,
       _timeoutDurations = kTimeoutClassDurations;

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
  }) : _transport = transport,
       _storage = storage,
       _timeoutDurations = timeoutDurations;

  /// The transport this client sends and receives encoded envelopes over.
  final DovahLinkTransport _transport;

  /// The SDK-owned persistence boundary for this client's identity, credential, and pairing
  /// recovery state.
  final ClientStorage _storage;

  /// The bounded wait allowed for each [TimeoutClass], keyed by class. `kTimeoutClassDurations`
  /// in production; overridable only through [DovahLinkClient.withTimeoutDurations] for tests.
  final Map<TimeoutClass, Duration> _timeoutDurations;

  /// Source of randomness for generating outgoing `messageId` values and, on first use, this
  /// installation's `clientId`.
  final Random _random = Random.secure();

  /// Every operation awaiting a correlated reply on the current connection, keyed by the outgoing
  /// `messageId` it was transmitted under.
  final Map<String, _PendingOperation> _pendingOperations =
      <String, _PendingOperation>{};

  /// Retry-safe operations an ordinary (non-administrative) transport loss orphaned before they
  /// received a reply, awaiting a chance to be retransmitted once the next `hello` succeeds.
  final List<_PendingOperation> _orphanedRetryableOperations =
      <_PendingOperation>[];

  /// The subscription currently reading [_transport]'s inbound message stream, or `null` when no
  /// connection is being received for. The SDK owns exactly one of these at a time, per
  /// `ai/context/sdk/architecture.md`'s "Inbound message handling".
  StreamSubscription<String>? _messageSubscription;

  /// Identifies the connection [_messageSubscription] currently belongs to. Incremented every
  /// time that connection is torn down, so a callback still in flight from an old subscription
  /// can recognize it no longer belongs to the current connection and must not mutate this
  /// client's state -- teardown itself still awaits full cancellation before a new subscription
  /// is established; this is defense in depth on top of that ordering, not a substitute for it.
  int _connectionGeneration = 0;

  /// The current connection lifecycle phase.
  DovahLinkConnectionState _connectionState =
      DovahLinkConnectionState.disconnected;

  /// The current trust standing, or `null` before [hello] succeeds.
  DovahLinkTrustState? _trustState;

  /// The server-issued session identifier, or `null` before [hello] succeeds.
  String? _sessionId;

  /// This installation's stable client ID, or `null` before [hello] has resolved it.
  String? _clientId;

  /// The DovahLink Bridge/mod release version reported by the last successful [hello], or `null`
  /// before [hello] succeeds. Cached so [authenticate] can report it again without re-sending
  /// `hello` on an already-admitted session.
  String? _bridgeVersion;

  /// The reason [connectionState] is [DovahLinkConnectionState.administrativelyInvalidated], or
  /// `null` otherwise.
  AdministrativeInvalidationReason? _invalidationReason;

  /// The current connection lifecycle phase.
  DovahLinkConnectionState get connectionState => _connectionState;

  /// The current trust standing, or `null` before [hello] succeeds.
  DovahLinkTrustState? get trustState => _trustState;

  /// The server-issued session identifier, or `null` before [hello] succeeds.
  String? get sessionId => _sessionId;

  /// This installation's stable client ID, or `null` before [hello] has resolved it.
  String? get clientId => _clientId;

  /// The reason [connectionState] is [DovahLinkConnectionState.administrativelyInvalidated], or
  /// `null` otherwise.
  AdministrativeInvalidationReason? get invalidationReason =>
      _invalidationReason;

  /// Establishes the transport connection to [uri]. Must be called before [hello].
  /// @throws DovahLinkConnectionException if the socket cannot be established.
  Future<void> connect(Uri uri) async {
    _connectionState = DovahLinkConnectionState.connecting;
    try {
      await _transport.connect(uri);
      _connectionState = DovahLinkConnectionState.connected;
      _ensureReceiving();
    } on Object catch (error) {
      _connectionState = DovahLinkConnectionState.disconnected;
      throw DovahLinkConnectionException('Failed to connect to $uri: $error');
    }
  }

  /// Sends `hello` and negotiates the session. Resolves and persists this installation's
  /// `clientId` on first use, and automatically presents a stored trusted credential as
  /// `trusted_device_credential` for an ordinary reconnect. Admits `unpaired` both when no
  /// credential is stored yet and when a `CONFIRMING` pairing is still outstanding -- the bridge
  /// has not yet committed that credential as trusted, so it must not be presented as one. Once
  /// the new session's trust state is known, retransmits any retry-safe operation an earlier
  /// ordinary transport loss orphaned, provided the new session still satisfies its required
  /// trust state; see [RequestPolicy.requiredTrustState].
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
      final Envelope response = await _sendAndAwait(
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

      _sessionId = response.sessionId;
      _trustState = trustState;
      _bridgeVersion = ack.bridgeVersion;

      // The bridge always sends an unprompted `capabilities` message right after `hello_ack`; it
      // arrives as an unsolicited (null-correlationId) message and is discarded by
      // _handleUnsolicited -- exposing it is out of this client's current scope. hello() does not
      // wait for it.
      _retryOrphanedOperations();

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
    if (_connectionState == DovahLinkConnectionState.connected &&
        _trustState == DovahLinkTrustState.trusted &&
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
    final Envelope response = await _sendAndAwait(
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
    final Envelope response = await _sendAndAwait(
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
    final Envelope response = await _sendAndAwait(
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
    final Envelope response = await _sendAndAwait(
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
    final Envelope response = await _sendAndAwait(
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
    _trustState = DovahLinkTrustState.trusted;

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
  Future<void> disconnect() async {
    await _teardownConnection(
      const DovahLinkConnectionException('Disconnected.'),
      orphanRetrySafeOperations: false,
    );
  }

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

  /// Ensures exactly one subscription is reading [_transport]'s inbound message stream for the
  /// current connection. Idempotent: a no-op once a subscription already exists. Called both from
  /// [connect] (the normal production trigger -- the SDK owns one reader for the connection's
  /// whole lifetime, not only while a request happens to be outstanding) and defensively from
  /// [_sendAndAwait] (a no-op in production, since [connect] always runs first there; it only
  /// matters for a caller that sends before ever calling [connect]).
  void _ensureReceiving() {
    if (_messageSubscription != null) {
      return;
    }
    final int generation = _connectionGeneration;
    final Stream<String> messages;
    try {
      messages = _transport.messages;
    } on Object catch (error) {
      // A caller that sends before ever calling connect() reaches this synchronously (the real
      // transport's messages getter throws when not connected) -- convert to the same typed
      // exception every other connection-level failure in this class already uses, rather than
      // letting a raw transport-specific error type escape.
      throw DovahLinkConnectionException('Cannot receive: $error');
    }
    _messageSubscription = messages.listen(
      (String raw) => _handleIncomingMessage(raw, generation),
      onError: (Object error, StackTrace stackTrace) => _handleReceiveFailure(
        generation,
        DovahLinkConnectionException('Connection lost while receiving: $error'),
      ),
      onDone: () => _handleReceiveFailure(
        generation,
        const DovahLinkConnectionException('Connection closed by the bridge.'),
      ),
    );
  }

  /// Sends one envelope carrying [messageType]/[payload], classified against [policy], and
  /// returns its correlated reply. The reply may arrive on a later wire attempt than the one this
  /// call makes: if the connection is lost before a reply arrives and [policy] is `retrySafe`,
  /// this same call keeps waiting while the operation is retransmitted once, automatically, after
  /// the next successful [hello] -- see [_retryOrphanedOperations]. A
  /// [DovahLinkConnectionException] means the connection itself is unhealthy (transport failure,
  /// timeout, or a non-retriable/failed retry); a [DovahLinkProtocolException] means the bridge
  /// answered on an otherwise-live connection with a wire-level rejection or an unexpected reply.
  Future<Envelope> _sendAndAwait({
    required String messageType,
    required JsonMap payload,
    required String expectedType,
    required RequestPolicy policy,
  }) async {
    _ensureReceiving();
    final _PendingOperation operation = _PendingOperation(
      messageType: messageType,
      payload: payload,
      policy: policy,
    );
    _transmit(operation);
    final Envelope envelope = await operation.completer.future;
    return _validateReply(expectedType: expectedType, envelope: envelope);
  }

  /// Generates a fresh `messageId`, registers [operation] as pending under it, arms its timeout,
  /// and sends it. Used both for an operation's initial transmission and its at-most-one retry
  /// after reconnect, so both share identical wire behavior. Fire-and-forget: a send failure
  /// routes through [_teardownConnection] rather than throwing here, since nothing local is
  /// positioned to catch it synchronously once retries are involved -- see [operation]'s shared
  /// `completer`.
  void _transmit(_PendingOperation operation) {
    final String messageId = _generateMessageId();
    _pendingOperations[messageId] = operation;
    operation.timer = Timer(
      _timeoutDurations[operation.policy.timeoutClass]!,
      () {
        unawaited(
          _teardownConnection(
            DovahLinkConnectionException(
              'Timed out awaiting a reply to ${operation.messageType}.',
            ),
          ),
        );
      },
    );

    final Envelope outgoing = Envelope(
      messageType: operation.messageType,
      messageId: messageId,
      sessionId: _sessionId,
      correlationId: null,
      payload: operation.payload,
      bridgeInstanceId: null,
      playContextId: null,
      clientId: null,
    );
    unawaited(
      _transport.send(jsonEncode(outgoing.toJson())).catchError((Object error) {
        unawaited(
          _teardownConnection(
            DovahLinkConnectionException(
              'Failed to send ${operation.messageType}: $error',
            ),
          ),
        );
      }),
    );
  }

  /// Translates a wire `error` or an unexpected message type into a typed exception; otherwise
  /// returns [envelope] unchanged.
  Envelope _validateReply({
    required String expectedType,
    required Envelope envelope,
  }) {
    if (envelope.messageType == 'error') {
      final ErrorPayload error = ErrorPayload.fromJson(envelope.payload);
      throw DovahLinkProtocolException(
        code: error.code,
        message: error.message,
        retryable: error.retryable,
      );
    }
    if (envelope.messageType != expectedType) {
      throw DovahLinkProtocolException(
        code: 'unexpected_message_type',
        message: 'Expected $expectedType but received ${envelope.messageType}.',
        retryable: false,
      );
    }
    return envelope;
  }

  /// Dispatches one decoded inbound message from the connection [generation] its subscription was
  /// established under. A stale message from an already-superseded generation is ignored, never
  /// mutating current state. Matches a correlated reply to its pending operation strictly by
  /// `correlationId`/`messageId`; routes an unsolicited (`correlationId: null`) message by type;
  /// fails closed on a non-null `correlationId` matching no pending operation, and on malformed
  /// JSON, rather than letting either escape this callback as an uncaught error.
  void _handleIncomingMessage(String raw, int generation) {
    if (generation != _connectionGeneration) {
      return;
    }
    final Envelope envelope;
    try {
      envelope = Envelope.fromJson(jsonDecode(raw) as JsonMap);
    } on Object catch (error) {
      // A protocol-level anomaly on an otherwise-live connection, not ordinary connectivity
      // loss -- never assumed safe to retry, unlike a send failure/timeout/socket drop.
      unawaited(
        _teardownConnection(
          DovahLinkConnectionException(
            'Received malformed JSON from the bridge: $error',
          ),
          orphanRetrySafeOperations: false,
        ),
      );
      return;
    }

    final String? correlationId = envelope.correlationId;
    if (correlationId == null) {
      _handleUnsolicited(envelope);
      return;
    }

    final _PendingOperation? operation = _pendingOperations.remove(
      correlationId,
    );
    if (operation == null) {
      // Protocol violation, not ordinary connectivity loss -- see _handleIncomingMessage's
      // malformed-JSON branch above for why this never orphans a retry-safe operation either.
      unawaited(
        _teardownConnection(
          DovahLinkProtocolException(
            code: 'unexpected_correlation_id',
            message:
                'Received a reply correlated to $correlationId with no matching pending operation.',
            retryable: false,
          ),
          orphanRetrySafeOperations: false,
        ),
      );
      return;
    }
    operation.timer?.cancel();
    if (!operation.completer.isCompleted) {
      operation.completer.complete(envelope);
    }
  }

  /// Routes one unsolicited (`correlationId: null`) inbound message by its type.
  void _handleUnsolicited(Envelope envelope) {
    switch (envelope.messageType) {
      case 'capabilities':
        // Declared once after hello_ack; exposing it is out of this client's current scope.
        break;
      case 'session_invalidated':
        unawaited(_handleSessionInvalidated(envelope));
        break;
      default:
        // Forward-compatible: an unsolicited message type this client does not yet model is
        // ignored rather than treated as a protocol violation -- only a non-null correlationId
        // matching no pending operation fails closed.
        break;
    }
  }

  /// Handles an authoritative `session_invalidated` push. Receiving one with no currently
  /// authenticated session is an impossible state per `protocol/schema/README.md` (the event is
  /// only ever sent for an existing authenticated session) and fails closed as a protocol
  /// violation via [_teardownConnection] rather than being accepted. Otherwise sets
  /// [connectionState] to [DovahLinkConnectionState.administrativelyInvalidated] and
  /// [invalidationReason], fails every pending or retry-orphaned operation (administrative
  /// invalidation is terminal and never eligible for the automatic retry [_retryOrphanedOperations]
  /// performs -- recovery is Stage 2's explicit, user-initiated Retry only), and tears the
  /// connection down directly rather than through [_teardownConnection]/[disconnect], so a
  /// following socket close the bridge itself performs cannot overwrite this typed reason back to
  /// generic transport loss -- see [_handleReceiveFailure]'s generation/state guard.
  Future<void> _handleSessionInvalidated(Envelope envelope) async {
    if (_sessionId == null || _trustState == null) {
      await _teardownConnection(
        const DovahLinkProtocolException(
          code: 'malformed_message',
          message:
              'Received session_invalidated with no authenticated session.',
          retryable: false,
        ),
      );
      return;
    }

    final AdministrativeInvalidationReason reason;
    try {
      final SessionInvalidatedPayload payload =
          SessionInvalidatedPayload.fromJson(envelope.payload);
      reason = payload.reason;
    } on ProtocolFormatException catch (error) {
      await _teardownConnection(_malformedMessageException(error));
      return;
    }

    _invalidationReason = reason;
    _connectionState = DovahLinkConnectionState.administrativelyInvalidated;
    _connectionGeneration++;

    final DovahLinkConnectionException invalidatedException =
        DovahLinkConnectionException(
          'Session invalidated ($reason) while awaiting a reply.',
        );
    for (final _PendingOperation operation in _pendingOperations.values) {
      operation.timer?.cancel();
      if (!operation.completer.isCompleted) {
        operation.completer.completeError(invalidatedException);
      }
    }
    _pendingOperations.clear();
    for (final _PendingOperation operation in _orphanedRetryableOperations) {
      if (!operation.completer.isCompleted) {
        operation.completer.completeError(invalidatedException);
      }
    }
    _orphanedRetryableOperations.clear();

    final StreamSubscription<String>? subscription = _messageSubscription;
    _messageSubscription = null;
    if (subscription != null) {
      try {
        await subscription.cancel();
      } on Object {
        // Best-effort, matching disconnect()'s existing tolerance.
      }
    }
    try {
      await _transport.close();
    } on Object {
      // Best-effort, matching disconnect()'s existing tolerance.
    }
  }

  /// Handles a transport-level failure (`onError`/`onDone`) reported for the connection
  /// [generation]'s subscription. Ignored if [generation] has already been superseded -- a stale
  /// receiver's own failure must not tear down a newer, already-healthy connection.
  void _handleReceiveFailure(
    int generation,
    DovahLinkConnectionException reason,
  ) {
    if (generation != _connectionGeneration) {
      return;
    }
    unawaited(_teardownConnection(reason));
  }

  /// The single path that tears an unhealthy connection down: bumps [_connectionGeneration] so a
  /// stale callback still in flight recognizes it no longer applies, awaits cancelling
  /// [_messageSubscription] and closing [_transport] before returning, then resolves every
  /// currently pending operation. A no-op if [connectionState] is already
  /// [DovahLinkConnectionState.administrativelyInvalidated] -- that typed reason is never
  /// overwritten by a failure that follows it, such as the bridge's own follow-up socket close.
  /// Otherwise always runs its full cleanup, even if [connectionState] was already
  /// [DovahLinkConnectionState.disconnected] (for example a caller that never called [connect] at
  /// all before a request failed) -- every step below ([_messageSubscription] cancellation,
  /// [_transport] close) is independently idempotent, so a redundant call is harmless, but
  /// skipping it entirely would leave [_transport] never actually closed for that caller.
  ///
  /// When [orphanRetrySafeOperations] is `true` (a genuine transport-level cause: send failure,
  /// timeout, receive error/done), a `retrySafe` operation that has not already been retried once
  /// is parked in [_orphanedRetryableOperations] instead of being failed immediately, awaiting
  /// [_retryOrphanedOperations]. When `false` (a protocol violation -- malformed JSON, an
  /// unmatched correlationId -- or [disconnect]'s deliberate, explicit teardown), every pending
  /// operation is failed immediately and any already-orphaned operation is failed too: a protocol
  /// violation is not assumed safe to blindly retry, and a deliberate disconnect never retries.
  Future<void> _teardownConnection(
    Exception reason, {
    bool orphanRetrySafeOperations = true,
  }) async {
    if (_connectionState ==
        DovahLinkConnectionState.administrativelyInvalidated) {
      return;
    }
    _connectionGeneration++;

    final StreamSubscription<String>? subscription = _messageSubscription;
    _messageSubscription = null;
    if (subscription != null) {
      try {
        await subscription.cancel();
      } on Object {
        // Best-effort: in-memory state resets below regardless.
      }
    }
    try {
      await _transport.close();
    } on Object {
      // Best-effort: in-memory state resets below regardless of whether the transport could
      // close cleanly.
    }

    _connectionState = DovahLinkConnectionState.disconnected;
    _trustState = null;
    _sessionId = null;

    final List<_PendingOperation> pending = _pendingOperations.values.toList();
    _pendingOperations.clear();
    for (final _PendingOperation operation in pending) {
      operation.timer?.cancel();
      if (orphanRetrySafeOperations &&
          operation.policy.retrySafe &&
          !operation.hasRetried) {
        _orphanedRetryableOperations.add(operation);
      } else if (!operation.completer.isCompleted) {
        operation.completer.completeError(reason);
      }
    }

    if (!orphanRetrySafeOperations) {
      final List<_PendingOperation> orphaned = _orphanedRetryableOperations
          .toList();
      _orphanedRetryableOperations.clear();
      for (final _PendingOperation operation in orphaned) {
        if (!operation.completer.isCompleted) {
          operation.completer.completeError(reason);
        }
      }
    }
  }

  /// Retransmits, at most once each, every operation an earlier ordinary transport loss orphaned,
  /// now that [hello] has re-established [trustState]. An operation whose
  /// [RequestPolicy.requiredTrustState] the new session no longer satisfies fails without
  /// retransmission instead of being retried into a session it was never classified for.
  void _retryOrphanedOperations() {
    final List<_PendingOperation> toRetry = _orphanedRetryableOperations
        .toList();
    _orphanedRetryableOperations.clear();
    for (final _PendingOperation operation in toRetry) {
      final DovahLinkTrustState? required = operation.policy.requiredTrustState;
      if (required != null && required != _trustState) {
        if (!operation.completer.isCompleted) {
          operation.completer.completeError(
            DovahLinkConnectionException(
              'Cannot retry ${operation.messageType} after reconnect: the new session no '
              'longer satisfies its required trust state.',
            ),
          );
        }
        continue;
      }
      operation.hasRetried = true;
      _transmit(operation);
    }
  }

  /// Generates a cryptographically random, session-unique `messageId`.
  String _generateMessageId() => _generateRandomHex(16);

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

  /// Generates [byteCount] cryptographically random bytes, hex-encoded.
  String _generateRandomHex(int byteCount) {
    final List<int> bytes = List<int>.generate(
      byteCount,
      (_) => _random.nextInt(256),
    );
    return bytes
        .map((int byte) => byte.toRadixString(16).padLeft(2, '0'))
        .join();
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

  /// Translates a DTO decode boundary failure into the typed exception SDK consumers already
  /// handle, per `ai/context/sdk/api-design.md`'s "Protocol DTO decoding" boundary translation.
  DovahLinkProtocolException _malformedMessageException(
    ProtocolFormatException error,
  ) => DovahLinkProtocolException(
    code: 'malformed_message',
    message: error.message,
    retryable: false,
  );

  /// Throws [_malformedMessageException] for [error]. Used at a call site that reports the
  /// translated failure by throwing rather than by passing it to [_teardownConnection].
  Never _throwMalformedMessage(ProtocolFormatException error) =>
      throw _malformedMessageException(error);

  /// Converts a rejected `trusted_device_credential` hello's wire error code into the typed
  /// reason [authenticate] recovers from, or `null` when [code] is not a recoverable rejection.
  CredentialRejectionReason? _credentialRejectionReason(String code) =>
      switch (code) {
        'revoked' => CredentialRejectionReason.revoked,
        'unauthenticated' => CredentialRejectionReason.unrecognized,
        _ => null,
      };
}

/// One request awaiting its correlated reply -- or, if `policy.retrySafe` and the connection was
/// lost before a reply arrived, awaiting a chance to be retransmitted once after the next
/// successful `hello`. Mutable bookkeeping for exactly one logical request across however many
/// wire attempts (the initial send, plus at most one retry) it takes to resolve [completer].
class _PendingOperation {
  /// Creates a pending operation for one logical request.
  _PendingOperation({
    required this.messageType,
    required this.payload,
    required this.policy,
  });

  /// The outgoing message type this operation sends.
  final String messageType;

  /// The outgoing payload this operation sends.
  final JsonMap payload;

  /// This operation's classification against the retry-safety/session-requirement/timeout-class
  /// model.
  final RequestPolicy policy;

  /// Resolved once, either by a correlated reply on the wire or by an outright failure -- shared
  /// across the initial attempt and its at-most-one retry, so the original caller's `await`
  /// resolves transparently regardless of which attempt actually succeeds.
  final Completer<Envelope> completer = Completer<Envelope>();

  /// Whether this operation has already been retransmitted once after a reconnect -- caps retry
  /// at exactly one attempt, per [RequestPolicy.retrySafe]'s contract.
  bool hasRetried = false;

  /// Bounds the current wire attempt; cancelled once [completer] settles or a new attempt starts.
  Timer? timer;
}
