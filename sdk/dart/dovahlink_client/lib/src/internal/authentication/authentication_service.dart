import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/hello_result.dart';
import 'package:dovahlink_client_sdk/src/internal/authentication/client_id_resolver.dart';
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

/// Owns `hello`/authentication and credential-rejection recovery, per
/// `ai/context/sdk/architecture.md`'s "Internal composition". Resolves and persists this
/// installation's `clientId`, negotiates trust with the bridge, and recovers from a rejected
/// `trusted_device_credential` hello by discarding it and retrying once as `unpaired`.
abstract interface class IAuthenticationService {
  /// This installation's stable client ID, or `null` before [hello] has resolved it.
  String? get clientId;

  /// Sends `hello` and negotiates the session. Resolves and persists this installation's
  /// `clientId` on first use, and automatically presents a stored trusted credential as
  /// `trusted_device_credential` for an ordinary reconnect. Admits `unpaired` both when no
  /// credential is stored yet and when a `CONFIRMING` pairing is still outstanding -- the bridge
  /// has not yet committed that credential as trusted, so it must not be presented as one. Once
  /// the new session's trust state is known, retransmits any retry-safe operation an earlier
  /// ordinary transport loss orphaned, provided the new session still satisfies its required
  /// trust state.
  /// @throws [DovahLinkProtocolException] if the bridge rejects authentication.
  Future<HelloResult> hello();

  /// Connects to [uri] and authenticates, recovering from a rejected `trusted_device_credential`
  /// hello (`revoked` or an unrecognized credential) by discarding it and retrying once as
  /// `unpaired` -- the bridge always accepts that, so a recoverable rejection never surfaces as a
  /// thrown exception here. [HelloResult.recoveredFromRejectedCredential] reports whether that
  /// happened and why, so a caller can still explain it to the user. A transport failure, a
  /// non-recoverable protocol rejection, or the retry attempt's own failure still throws normally.
  ///
  /// A no-op that returns the cached result of the last [hello] when this client is already
  /// connected and trusted -- the bridge's one-session-per-connection limit
  /// (`handshake_handler.cpp`'s `TryCreateSession`) rejects a second `hello` on a socket that
  /// already holds a session, so re-authenticating an already-trusted, still-open connection must
  /// not re-send one.
  /// @throws [DovahLinkConnectionException] if the socket cannot be established (initial or retry).
  /// @throws [DovahLinkProtocolException] if hello is rejected for a non-recoverable reason, or the
  ///     retry attempt is itself rejected.
  Future<HelloResult> authenticate(Uri uri);

  /// Discards the persisted pairing credential and recovery state while preserving [clientId], so
  /// the next [hello] presents `AuthMethod.unpaired` instead of a credential the bridge has
  /// already rejected. Call after a `trusted_device_credential` hello is rejected
  /// (`unauthenticated`/`revoked`) and before retrying -- this installation's identity is not
  /// itself invalid, only its stored credential. Does not touch the transport or in-memory
  /// connection state.
  Future<void> forgetCredential();
}

/// Implements [IAuthenticationService], per `ai/context/sdk/architecture.md`'s "Internal
/// composition". Every collaborator ([ISessionService], [ISessionAdmissionService], [IRequestService],
/// [IClientStorage], [ClientIdResolver]) is supplied by the caller per
/// `ai/context/sdk/architecture.md`'s "Dependency injection" -- this class never constructs one of
/// its own dependencies, including [ClientIdResolver], despite it being a small, otherwise
/// dependency-free collaborator.
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

  /// Creates an authentication service over [sessionService], [sessionAdmissionService],
  /// [requestService], [storage], and [clientIdResolver].
  AuthenticationService({
    required ISessionService sessionService,
    required ISessionAdmissionService sessionAdmissionService,
    required IRequestService requestService,
    required IClientStorage storage,
    required ClientIdResolver clientIdResolver,
  }) : _sessionService = sessionService,
       _sessionAdmissionService = sessionAdmissionService,
       _requestService = requestService,
       _storage = storage,
       _clientIdResolver = clientIdResolver;

  /// This installation's stable client ID, or `null` before [hello] has resolved it.
  String? _clientId;

  /// The DovahLink Bridge/mod release version reported by the last successful [hello], or `null`
  /// before [hello] succeeds. Cached so [authenticate] can report it again without re-sending
  /// `hello` on an already-admitted session.
  String? _bridgeVersion;

  /// Implements [IAuthenticationService.clientId].
  @override
  String? get clientId => _clientId;

  /// Implements [IAuthenticationService.hello].
  @override
  Future<HelloResult> hello() async {
    final PersistedClientState state = await _storage.load();
    final String clientId = await _clientIdResolver.resolve(state);
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
      final HelloAckPayload ack = ProtocolPayloadDecoder.decode(
        HelloAckPayload.fromJson,
        response.payload,
      );
      final DovahLinkTrustState trustState = switch (ack.clientIdentityKind) {
        ClientIdentityKind.unpaired => DovahLinkTrustState.unpaired,
        ClientIdentityKind.paired => DovahLinkTrustState.trusted,
      };

      final String? sessionId = response.sessionId;
      if (sessionId == null) {
        // hello_ack always carries a real sessionId per `protocol/schema/README.md`; a null one
        // is a malformed reply, not a state ISessionAdmissionService.admitSession's typed contract
        // accepts silently the way the pre-extraction field assignment once did.
        throw const DovahLinkProtocolException(
          code: ProtocolErrorCode.malformedMessage,
          message: 'The bridge reported hello_ack with no sessionId.',
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
      _bridgeVersion = ack.bridgeVersion;

      // The bridge always sends an unprompted `capabilities` message right after `hello_ack`; it
      // arrives as an unsolicited (null-correlationId) message and is discarded by
      // MessageRouter -- exposing it is out of this client's current scope. hello() does not
      // wait for it.

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
      // Preserves any retry-safe operation an earlier ordinary transport loss already orphaned:
      // this cleanup is one failed attempt within a bounded reconnect cycle that may still retry,
      // not that cycle's own final give-up -- only the cycle's own last disconnect() call (default
      // orphanRetrySafeOperations: false) should finalize/fail what this preserved.
      await _sessionService.disconnect(orphanRetrySafeOperations: true);
      rethrow;
    }
  }

  /// Implements [IAuthenticationService.authenticate].
  @override
  Future<HelloResult> authenticate(Uri uri) async {
    final String? cachedBridgeVersion = _bridgeVersion;
    if (_sessionService.connectionState == DovahLinkConnectionState.connected &&
        _sessionService.currentTrustState == DovahLinkTrustState.trusted &&
        cachedBridgeVersion != null) {
      return HelloResult(
        bridgeVersion: cachedBridgeVersion,
        trustState: DovahLinkTrustState.trusted,
      );
    }
    await _sessionService.connect(uri);
    try {
      return await hello();
    } on DovahLinkProtocolException catch (error) {
      final CredentialRejectionReason? reason =
          CredentialRejectionReason.fromProtocolErrorCode(error.code);
      if (reason == null) {
        rethrow;
      }
      await forgetCredential();
      await _sessionService.connect(uri);
      final HelloResult result = await hello();
      return HelloResult(
        bridgeVersion: result.bridgeVersion,
        trustState: result.trustState,
        recoveredFromRejectedCredential: reason,
      );
    }
  }

  /// Implements [IAuthenticationService.forgetCredential].
  @override
  Future<void> forgetCredential() async {
    final PersistedClientState state = await _storage.load();
    await _storage.save(PersistedClientState(clientId: state.clientId));
  }
}
