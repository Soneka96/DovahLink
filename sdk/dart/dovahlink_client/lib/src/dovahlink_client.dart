import 'dart:convert';
import 'dart:math';

import 'dovahlink_client_exception.dart';
import 'enums.dart';
import 'hello_result.dart';
import 'persistence/client_storage.dart';
import 'persistence/persisted_client_state.dart';
import 'persistence/windows/dpapi_client_storage.dart';
import 'protocol/envelope.dart';
import 'protocol/error_payload.dart';
import 'protocol/hello_payloads.dart';
import 'protocol/json_map.dart';
import 'protocol/pairing_payloads.dart';
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
  DovahLinkClient({DovahLinkTransport? transport, required ClientStorage storage})
    : _transport = transport ?? WebSocketTransport(),
      _storage = storage;

  /// Creates a client backed by real infrastructure: a [WebSocketTransport] and a
  /// [DpapiClientStorage] persisting to this Windows user's default per-user location.
  factory DovahLinkClient.windows() =>
      DovahLinkClient(storage: DpapiClientStorage());

  /// The transport this client sends and receives encoded envelopes over.
  final DovahLinkTransport _transport;

  /// The SDK-owned persistence boundary for this client's identity, credential, and pairing
  /// recovery state.
  final ClientStorage _storage;

  /// Source of randomness for generating outgoing `messageId` values and, on first use, this
  /// installation's `clientId`.
  final Random _random = Random.secure();

  /// The current connection lifecycle phase.
  DovahLinkConnectionState _connectionState =
      DovahLinkConnectionState.disconnected;

  /// The current trust standing, or `null` before [hello] succeeds.
  DovahLinkTrustState? _trustState;

  /// The server-issued session identifier, or `null` before [hello] succeeds.
  String? _sessionId;

  /// This installation's stable client ID, or `null` before [hello] has resolved it.
  String? _clientId;

  /// The current connection lifecycle phase.
  DovahLinkConnectionState get connectionState => _connectionState;

  /// The current trust standing, or `null` before [hello] succeeds.
  DovahLinkTrustState? get trustState => _trustState;

  /// The server-issued session identifier, or `null` before [hello] succeeds.
  String? get sessionId => _sessionId;

  /// This installation's stable client ID, or `null` before [hello] has resolved it.
  String? get clientId => _clientId;

  /// Establishes the transport connection to [uri]. Must be called before [hello].
  /// @throws DovahLinkConnectionException if the socket cannot be established.
  Future<void> connect(Uri uri) async {
    _connectionState = DovahLinkConnectionState.connecting;
    try {
      await _transport.connect(uri);
      _connectionState = DovahLinkConnectionState.connected;
    } on Object catch (error) {
      _connectionState = DovahLinkConnectionState.disconnected;
      throw DovahLinkConnectionException('Failed to connect to $uri: $error');
    }
  }

  /// Sends `hello` and negotiates the session. Resolves and persists this installation's
  /// `clientId` on first use, and automatically presents a stored trusted credential as
  /// `trusted_device_credential` for an ordinary reconnect. Admits `unpaired` both when no
  /// credential is stored yet and when a `CONFIRMING` pairing is still outstanding -- the bridge
  /// has not yet committed that credential as trusted, so it must not be presented as one.
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
      );
      final HelloAckPayload ack = HelloAckPayload.fromJson(response.payload);
      final DovahLinkTrustState trustState = _parseClientIdentityKind(
        ack.clientIdentityKind,
      );

      _sessionId = response.sessionId;
      _trustState = trustState;

      // The bridge always sends an unprompted `capabilities` message right after `hello_ack`;
      // consumed and discarded here -- exposing it is out of this client's current scope.
      await _readEnvelope(expectedType: 'capabilities');

      return HelloResult(
        bridgeVersion: ack.bridgeVersion,
        trustState: trustState,
      );
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

  /// Starts, or queries the status of, a pairing challenge. Valid only on an `unpaired` session.
  Future<PairingAvailability> requestPairing() async {
    final Envelope response = await _sendAndAwait(
      messageType: 'pairing_request',
      payload: const <String, dynamic>{},
      expectedType: 'pairing_status',
    );
    final PairingStatusPayload status = PairingStatusPayload.fromJson(
      response.payload,
    );
    return _parsePairingAvailability(status.state);
  }

  /// Submits the six-digit code the user read from Skyrim. Durably persists the issued credential
  /// and a `CONFIRMING` recovery state before returning it, per
  /// `ai/context/protocol/security.md`'s "client durably persists its issued credential and its
  /// `CONFIRMING` recovery state before sending final confirmation."
  /// @return The issued credential, already persisted.
  /// @throws DovahLinkPairingException if the code was expired, invalid, or rate-limited.
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
    );
    final PairingOutcomePayload outcome = PairingOutcomePayload.fromJson(
      response.payload,
    );
    if (outcome.outcome != 'credential_issued') {
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
    );
    final PairingOutcomePayload outcome = PairingOutcomePayload.fromJson(
      response.payload,
    );
    if (outcome.outcome != 'trusted' && outcome.outcome != 'already_trusted') {
      throw DovahLinkPairingException(outcome.outcome);
    }
    _trustState = DovahLinkTrustState.trusted;

    final PersistedClientState state = await _storage.load();
    await _storage.save(state.copyWith(recoveryState: PairingRecoveryState.none));
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
      if (error.outcome == 'pending_not_found') {
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
  /// untouched -- trust survives a disconnect.
  Future<void> disconnect() async {
    _connectionState = DovahLinkConnectionState.disconnected;
    _trustState = null;
    _sessionId = null;
    try {
      await _transport.close();
    } on Object {
      // Best-effort: in-memory state is already reset above regardless of whether the
      // underlying transport could be closed cleanly.
    }
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

  /// Sends one envelope carrying [messageType]/[payload] and awaits its expected reply.
  Future<Envelope> _sendAndAwait({
    required String messageType,
    required JsonMap payload,
    required String expectedType,
  }) async {
    final Envelope outgoing = Envelope(
      messageType: messageType,
      messageId: _generateMessageId(),
      sessionId: _sessionId,
      correlationId: null,
      payload: payload,
      bridgeInstanceId: null,
      playContextId: null,
      clientId: null,
    );
    try {
      await _transport.send(jsonEncode(outgoing.toJson()));
    } on Object catch (error) {
      throw DovahLinkConnectionException('Failed to send $messageType: $error');
    }
    return _readEnvelope(expectedType: expectedType);
  }

  /// Reads one reply envelope, translating a wire `error` or an unexpected message type into a
  /// typed exception.
  Future<Envelope> _readEnvelope({required String expectedType}) async {
    final String raw;
    try {
      raw = await _transport.messages.first;
    } on Object catch (error) {
      throw DovahLinkConnectionException(
        'Connection lost while awaiting $expectedType: $error',
      );
    }

    final Envelope envelope;
    try {
      envelope = Envelope.fromJson(jsonDecode(raw) as JsonMap);
    } on Object catch (error) {
      throw DovahLinkConnectionException(
        'Received malformed JSON from the bridge: $error',
      );
    }
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

  /// Interprets `hello_ack.clientIdentityKind`'s raw wire value.
  DovahLinkTrustState _parseClientIdentityKind(String raw) => switch (raw) {
    'unpaired' => DovahLinkTrustState.unpaired,
    'paired' => DovahLinkTrustState.trusted,
    _ => throw DovahLinkProtocolException(
      code: 'malformed_message',
      message: 'Unrecognized clientIdentityKind: $raw',
      retryable: false,
    ),
  };

  /// Interprets `pairing_status.state`'s raw wire value.
  PairingAvailability _parsePairingAvailability(String raw) => switch (raw) {
    'unavailable' => PairingAvailability.unavailable,
    'available' => PairingAvailability.available,
    'in_progress' => PairingAvailability.inProgress,
    _ => throw DovahLinkProtocolException(
      code: 'malformed_message',
      message: 'Unrecognized pairing_status.state: $raw',
      retryable: false,
    ),
  };
}
