import 'package:dovahlink_client_sdk/src/dovahlink_compatibility_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/hello_result.dart';
import 'package:dovahlink_client_sdk/src/internal/authentication/client_id_cache.dart';
import 'package:dovahlink_client_sdk/src/internal/authentication/client_id_resolver.dart';
import 'package:dovahlink_client_sdk/src/internal/compatibility/host_version_compatibility.dart';
import 'package:dovahlink_client_sdk/src/internal/protocol_payload_decoder.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/request_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_admission_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/persistence/client_storage.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/hello_ack_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/hello_payload.dart';
import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Defines authentication operations for resolving this installation's
/// [IAuthenticationService.clientId], negotiating Host trust, and recovering from a rejected saved
/// credential.
abstract interface class IAuthenticationService {
  /// This installation's stable client ID, or `null` before [IAuthenticationService.hello] resolves
  /// it.
  String? get clientId;

  /// Sends `hello` and admits the returned session. Resolves and persists this installation's
  /// [IAuthenticationService.clientId] on first use. A saved credential is presented unless
  /// [PairingRecoveryState.confirming] is pending; the Host has not trusted that credential yet.
  /// After admission, retries any orphaned operation whose trust requirement the new session meets.
  /// @throws [DovahLinkProtocolException] if the Host rejects authentication.
  /// @throws [DovahLinkCompatibilityException] if the Host version is outside the SDK's supported
  ///     range.
  /// @throws [DovahLinkConnectionException] if disconnect cancels an in-flight authentication.
  Future<HelloResult> hello();

  /// Invalidates any authentication continuation waiting on storage or Host I/O.
  void cancelPendingAuthentication();

  /// Connects to [uri] and authenticates. If the Host rejects a saved credential as
  /// [CredentialRejectionReason.revoked] or [CredentialRejectionReason.unrecognized], discards it
  /// and retries once with [AuthMethod.unpaired].
  /// [HelloResult.recoveredFromRejectedCredential] reports that recovery. Transport failures,
  /// incompatible Host versions, and non-recoverable protocol errors still throw.
  ///
  /// Returns the cached result when [DovahLinkConnectionState.connected] and
  /// [DovahLinkTrustState.trusted]. Otherwise closes any existing connection before opening
  /// another, since each socket can admit only one session.
  /// @throws [DovahLinkConnectionException] if the socket cannot be established (initial or retry).
  /// @throws [DovahLinkProtocolException] if hello is rejected for a non-recoverable reason, or the
  ///     retry attempt is itself rejected.
  /// @throws [DovahLinkCompatibilityException] if the Host version is outside the SDK's supported
  ///     range.
  Future<HelloResult> authenticate(Uri uri);

  /// Discards the stored credential and pairing-recovery state while preserving
  /// [IAuthenticationService.clientId]. The next [IAuthenticationService.hello] authenticates as
  /// [AuthMethod.unpaired]. Does not alter the transport or session state.
  Future<void> forgetCredential();
}

/// Implements [IAuthenticationService] by coordinating credential loading, Host hello requests,
/// trust recovery, and session admission.
class AuthenticationService implements IAuthenticationService {
  /// Connects, disconnects, and reads live connection/trust state.
  final ISessionService _sessionService;

  /// Admits a newly authenticated session -- the only class permitted to.
  final ISessionAdmissionService _sessionAdmissionService;

  /// Sends `hello` and awaits its correlated reply.
  final IRequestService _requestService;

  /// The SDK-owned persistence boundary for this client's identity, credential, and pairing
  /// recovery state.
  final IClientStorage _storage;

  /// Resolves this installation's persisted client ID on first use.
  final ClientIdResolver _clientIdResolver;

  /// Shares this installation's resolved [IAuthenticationService.clientId] with
  /// [PendingOperationTransmitter], so
  /// authentication and pending retries use the same client identity.
  final ClientIdCache _clientIdCache;

  /// Creates an authentication service over [sessionService], [sessionAdmissionService],
  /// [requestService], [storage], [clientIdResolver], and [clientIdCache].
  AuthenticationService({
    required ISessionService sessionService,
    required ISessionAdmissionService sessionAdmissionService,
    required IRequestService requestService,
    required IClientStorage storage,
    required ClientIdResolver clientIdResolver,
    required ClientIdCache clientIdCache,
  }) : _sessionService = sessionService,
       _sessionAdmissionService = sessionAdmissionService,
       _requestService = requestService,
       _storage = storage,
       _clientIdResolver = clientIdResolver,
       _clientIdCache = clientIdCache;

  /// The complete result from the last successful Host handshake, cached for an admitted session.
  HelloResult? _lastHelloResult;

  /// Generation invalidating authentication work when the client disconnects.
  int _authenticationGeneration = 0;

  /// Implements [IAuthenticationService.clientId].
  @override
  String? get clientId => _clientIdCache.clientId;

  /// Implements [IAuthenticationService.hello].
  @override
  Future<HelloResult> hello() => _hello(_authenticationGeneration);

  /// Sends hello while [generation] remains the active authentication generation.
  Future<HelloResult> _hello(int generation) async {
    bool disconnectAfterFailure = false;
    try {
      final PersistedClientState state = await _storage.load();
      _ensureAuthenticationCurrent(generation);
      final String clientId = await _clientIdResolver.resolve(state);
      _ensureAuthenticationCurrent(generation);
      final String? credential =
          state.recoveryState == PairingRecoveryState.none
          ? state.credential
          : null;
      _clientIdCache.set(clientId);

      final HelloPayload payload = HelloPayload(
        clientId: clientId,
        authMethod: credential == null
            ? AuthMethod.unpaired
            : AuthMethod.trustedDeviceCredential,
        authToken: credential,
      );
      disconnectAfterFailure = true;
      final Envelope response = await _requestService.sendAndAwait(
        messageType: ProtocolMessageType.hello,
        payload: payload.toJson(),
        expectedType: ProtocolMessageType.helloAck,
        policy: const RequestPolicy(
          retrySafe: false,
          requiredTrustState: null,
          timeoutClass: TimeoutClass.normal,
        ),
      );
      _ensureAuthenticationCurrent(generation);
      final HelloAckPayload ack = ProtocolPayloadDecoder.decode(
        HelloAckPayload.fromJson,
        response.payload,
      );
      validateHostVersionCompatibility(ack.hostVersion);
      final DovahLinkTrustState trustState = switch (ack.clientIdentityKind) {
        ClientIdentityKind.unpaired => DovahLinkTrustState.unpaired,
        ClientIdentityKind.paired => DovahLinkTrustState.trusted,
      };

      final String? sessionId = response.sessionId;
      if (sessionId == null) {
        // hello_ack requires a sessionId. A null value is malformed and must be rejected before
        // session admission.
        throw const DovahLinkProtocolException(
          code: ProtocolErrorCode.malformedMessage,
          message: 'The host reported hello_ack with no sessionId.',
          retryable: false,
        );
      }
      // admitSession also retransmits any retry-safe operation an earlier ordinary transport
      // loss orphaned, now that this new session's trust state is known -- see
      // ISessionAdmissionService.admitSession's documentation.
      _sessionAdmissionService.admitSession(
        sessionId: sessionId,
        trustState: trustState,
      );
      final HelloResult result = HelloResult(
        hostId: ack.hostId,
        hostName: ack.hostName,
        hostVersion: ack.hostVersion,
        trustState: trustState,
      );
      _lastHelloResult = result;

      // The Host always sends an unprompted `capabilities` message right after `hello_ack`; it
      // arrives as an unsolicited (null-correlationId) message and is discarded by
      // MessageRouter -- exposing it is out of this client's current scope. hello() does not
      // wait for it.

      return result;
    } on Object {
      _ensureAuthenticationCurrent(generation);
      if (!disconnectAfterFailure) {
        rethrow;
      }
      // Every HandleHello failure path closes the connection (handshake_handler.cpp's Fail()
      // always sets closeConnection), and a genuine transport failure leaves the socket equally
      // unusable either way -- reset so the next connect() attempt does not find a stale socket
      // WebSocketTransport still considers open (its "Already connected" guard in connect()).
      // disconnect() itself never throws, so this cannot mask the error being rethrown below.
      // Preserves any retry-safe operation an earlier ordinary transport loss already orphaned:
      // this cleanup is one failed attempt within a bounded reconnect cycle that may still retry,
      // not that cycle's own final give-up -- only the cycle's own last disconnect() call (default
      // orphanRetrySafeOperations: false) should finalize/fail what this preserved.
      await _sessionService.disconnect(orphanRetrySafeOperations: true);
      _ensureAuthenticationCurrent(generation);
      rethrow;
    }
  }

  /// Implements [IAuthenticationService.authenticate].
  @override
  Future<HelloResult> authenticate(Uri uri) async {
    final int generation = _authenticationGeneration;
    final HelloResult? cachedHelloResult = _lastHelloResult;
    if (_sessionService.connectionState == DovahLinkConnectionState.connected &&
        _sessionService.currentTrustState == DovahLinkTrustState.trusted &&
        cachedHelloResult != null) {
      return HelloResult(
        hostId: cachedHelloResult.hostId,
        hostName: cachedHelloResult.hostName,
        hostVersion: cachedHelloResult.hostVersion,
        trustState: DovahLinkTrustState.trusted,
      );
    }
    // A connection this method (or an earlier hello()) already admitted, but that never reached
    // the cached-and-trusted shortcut above, must be closed before reconnecting -- the transport
    // rejects a second connect() on a socket it has not closed.
    if (_sessionService.connectionState !=
        DovahLinkConnectionState.disconnected) {
      await _sessionService.disconnect(orphanRetrySafeOperations: false);
      _ensureAuthenticationCurrent(generation);
    }
    await _sessionService.connect(uri);
    _ensureAuthenticationCurrent(generation);
    try {
      final HelloResult result = await _hello(generation);
      _ensureAuthenticationCurrent(generation);
      return result;
    } on DovahLinkProtocolException catch (error) {
      _ensureAuthenticationCurrent(generation);
      final CredentialRejectionReason? reason =
          CredentialRejectionReason.fromProtocolErrorCode(error.code);
      if (reason == null) {
        rethrow;
      }
      await _forgetCredential(generation);
      _ensureAuthenticationCurrent(generation);
      await _sessionService.connect(uri);
      _ensureAuthenticationCurrent(generation);
      final HelloResult result = await _hello(generation);
      _ensureAuthenticationCurrent(generation);
      return HelloResult(
        hostId: result.hostId,
        hostName: result.hostName,
        hostVersion: result.hostVersion,
        trustState: result.trustState,
        recoveredFromRejectedCredential: reason,
      );
    }
  }

  /// Implements [IAuthenticationService.cancelPendingAuthentication].
  @override
  void cancelPendingAuthentication() {
    _authenticationGeneration++;
  }

  /// Throws when explicit disconnect invalidated [generation] during authentication.
  void _ensureAuthenticationCurrent(int generation) {
    if (generation != _authenticationGeneration) {
      throw const DovahLinkConnectionException(
        'Authentication was cancelled by disconnect.',
      );
    }
  }

  /// Implements [IAuthenticationService.forgetCredential].
  @override
  Future<void> forgetCredential() => _forgetCredential(null);

  /// Clears persisted trust only while [generation] remains current.
  Future<void> _forgetCredential(int? generation) async {
    final PersistedClientState state = await _storage.load();
    if (generation != null) {
      _ensureAuthenticationCurrent(generation);
    }
    await _storage.save(PersistedClientState(clientId: state.clientId));
  }
}
