import 'dart:convert';
import 'dart:math';

import 'dovahlink_client_exception.dart';
import 'protocol/envelope.dart';
import 'protocol/error_payload.dart';
import 'protocol/hello_payloads.dart';
import 'protocol/json_map.dart';
import 'protocol/pairing_payloads.dart';
import 'transport/dovahlink_transport.dart';
import 'transport/websocket_transport.dart';

/// The connection lifecycle phase of a [DovahLinkClient].
enum DovahLinkConnectionState {
  /// No socket is connected.
  disconnected,

  /// Establishing the socket connection.
  connecting,

  /// A socket is connected.
  connected,
}

/// The client's own trust standing with the bridge, established once [DovahLinkClient.hello]
/// succeeds and possibly upgraded by [DovahLinkClient.acknowledgeTrustedCredential].
enum DovahLinkTrustState {
  /// Admitted without a trust credential; restricted to the pairing message set.
  unpaired,

  /// Fully trusted, either by a `trusted_device_credential` hello or a completed pairing.
  trusted,
}

/// The bridge's report of pairing availability, from [DovahLinkClient.requestPairing].
enum PairingAvailability {
  /// No challenge is currently active, and none was just started.
  unavailable,

  /// A fresh six-digit code was just generated and shown in Skyrim.
  available,

  /// A challenge is already active; no new code was generated.
  inProgress,
}

/// The bootstrap information [DovahLinkClient.hello] returns on success.
class HelloResult {
  /// Creates a hello result.
  const HelloResult({required this.bridgeVersion, required this.trustState});

  /// The DovahLink Bridge/mod release version.
  final String bridgeVersion;

  /// The trust tier the session was admitted at.
  final DovahLinkTrustState trustState;
}

/// A minimal, real, Flutter/Redux-independent DovahLink protocol client: connect, authenticate,
/// pair, and disconnect. Deliberately narrow -- this is the pre-SDK contract described in
/// `sdk/README.md`, not the eventual Dart Client SDK; it does not expose subscriptions, state
/// delivery, reconnect, or durable credential storage.
///
/// Never exposes raw JSON or transport details: every method takes and returns typed values.
class DovahLinkClient {
  /// Creates a client. [transport] defaults to a real [WebSocketTransport]; inject a fake for
  /// deterministic tests.
  DovahLinkClient({DovahLinkTransport? transport})
    : _transport = transport ?? WebSocketTransport();

  /// The transport this client sends and receives encoded envelopes over.
  final DovahLinkTransport _transport;

  /// Source of randomness for generating outgoing `messageId` values.
  final Random _random = Random.secure();

  /// The current connection lifecycle phase.
  DovahLinkConnectionState _connectionState =
      DovahLinkConnectionState.disconnected;

  /// The current trust standing, or `null` before [hello] succeeds.
  DovahLinkTrustState? _trustState;

  /// The server-issued session identifier, or `null` before [hello] succeeds.
  String? _sessionId;

  /// The current connection lifecycle phase.
  DovahLinkConnectionState get connectionState => _connectionState;

  /// The current trust standing, or `null` before [hello] succeeds.
  DovahLinkTrustState? get trustState => _trustState;

  /// The server-issued session identifier, or `null` before [hello] succeeds.
  String? get sessionId => _sessionId;

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

  /// Sends `hello` and negotiates the session. [credential] absent admits an `unpaired` session;
  /// present sends it as a `trusted_device_credential` for an ordinary reconnect.
  /// @throws DovahLinkProtocolException if the bridge rejects authentication.
  Future<HelloResult> hello({
    required String clientId,
    String? credential,
  }) async {
    final HelloPayload payload = HelloPayload(
      clientId: clientId,
      authMethod: credential == null ? 'unpaired' : 'trusted_device_credential',
      authToken: credential,
    );
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
    // consumed and discarded here -- exposing it is out of this pre-SDK's scope.
    await _readEnvelope(expectedType: 'capabilities');

    return HelloResult(
      bridgeVersion: ack.bridgeVersion,
      trustState: trustState,
    );
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

  /// Submits the six-digit code the user read from Skyrim.
  /// @return The hex-encoded credential to durably save before calling
  ///     [acknowledgeTrustedCredential].
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
    return credential;
  }

  /// Echoes back a [credential] durably saved from [confirmPairingCode], completing pairing.
  /// [trustState] becomes [DovahLinkTrustState.trusted] on success.
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
  }

  /// Closes the connection and resets session state. Idempotent.
  Future<void> disconnect() async {
    await _transport.close();
    _connectionState = DovahLinkConnectionState.disconnected;
    _trustState = null;
    _sessionId = null;
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
  String _generateMessageId() {
    final List<int> bytes = List<int>.generate(16, (_) => _random.nextInt(256));
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
