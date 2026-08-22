import 'package:meta/meta.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_pairing_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/hello_result.dart';
import 'package:dovahlink_client_sdk/src/internal/authentication_service.dart';
import 'package:dovahlink_client_sdk/src/internal/client_session.dart';
import 'package:dovahlink_client_sdk/src/internal/pairing_service.dart';
import 'package:dovahlink_client_sdk/src/internal/request_manager.dart';
import 'package:dovahlink_client_sdk/src/pairing_cancel_outcome.dart';
import 'package:dovahlink_client_sdk/src/pairing_challenge_status.dart';
import 'package:dovahlink_client_sdk/src/pairing_renotify_result.dart';
import 'package:dovahlink_client_sdk/src/persistence/client_storage.dart';
import 'package:dovahlink_client_sdk/src/persistence/windows/dpapi_client_storage.dart';
import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/constants.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/transport/dovahlink_transport.dart';
import 'package:dovahlink_client_sdk/src/transport/websocket_transport.dart';

/// A real, Flutter/Redux-independent DovahLink protocol client: connect, authenticate, pair, and
/// disconnect. Owns its local `clientId`, pairing credential, and `CONFIRMING` recovery state
/// behind [ClientStorage] -- see `ai/context/sdk/persistence.md`'s ownership rule -- so a consumer
/// never threads identity or credential material through this API by hand.
///
/// Never exposes raw JSON or transport details: every method takes and returns typed values.
class DovahLinkClient {
  /// The SDK-owned persistence boundary for this client's identity, credential, and pairing
  /// recovery state.
  final ClientStorage _storage;

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
    _authenticationService = AuthenticationService(
      requestSender: _requestManager,
      storage: _storage,
      sessionConnector: _session,
      sessionContext: _session,
      messageReceiver: _session,
    );
    _pairingService = PairingService(
      requestSender: _requestManager,
      storage: _storage,
      sessionTrustWriter: _session,
      messageReceiver: _session,
    );
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
    _authenticationService = AuthenticationService(
      requestSender: _requestManager,
      storage: _storage,
      sessionConnector: _session,
      sessionContext: _session,
      messageReceiver: _session,
    );
    _pairingService = PairingService(
      requestSender: _requestManager,
      storage: _storage,
      sessionTrustWriter: _session,
      messageReceiver: _session,
    );
  }

  /// Owns transport lifecycle, connection state, and stream ownership -- the sole owner of every
  /// socket-scoped field this client has; see `ai/context/sdk/architecture.md`'s "Session-state
  /// ownership". This façade never assigns session state directly; [ClientSession]'s own commands
  /// ([ClientSession.admitSession], [ClientSession.markTrusted]) are invoked only by
  /// [_authenticationService] and [_pairingService] respectively.
  late final ClientSession _session;

  /// Owns pending requests, timeouts, and retry behavior. The same instance [_session] itself
  /// uses internally, exposed via [ClientSession.requestManager] so [_authenticationService] and
  /// [_pairingService] can send requests directly without depending on the whole session.
  late final RequestManager _requestManager;

  /// Owns `hello`/authentication and credential-rejection recovery.
  late final AuthenticationService _authenticationService;

  /// Owns pairing operations.
  late final PairingService _pairingService;

  /// The current connection lifecycle phase.
  DovahLinkConnectionState get connectionState => _session.connectionState;

  /// The current trust standing, or `null` before [hello] succeeds.
  DovahLinkTrustState? get trustState => _session.currentTrustState;

  /// The server-issued session identifier, or `null` before [hello] succeeds.
  String? get sessionId => _session.currentSessionId;

  /// This installation's stable client ID, or `null` before [hello] has resolved it.
  String? get clientId => _authenticationService.clientId;

  /// The reason [connectionState] is [DovahLinkConnectionState.administrativelyInvalidated], or
  /// `null` otherwise.
  AdministrativeInvalidationReason? get invalidationReason =>
      _session.invalidationReason;

  /// Establishes the transport connection to [uri]. Must be called before [hello].
  /// @throws [DovahLinkConnectionException] if the socket cannot be established.
  Future<void> connect(Uri uri) => _session.connect(uri);

  /// Sends `hello` and negotiates the session. Resolves and persists this installation's
  /// `clientId` on first use, and automatically presents a stored trusted credential as
  /// `trusted_device_credential` for an ordinary reconnect. Admits `unpaired` both when no
  /// credential is stored yet and when a `CONFIRMING` pairing is still outstanding -- the bridge
  /// has not yet committed that credential as trusted, so it must not be presented as one. Once
  /// the new session's trust state is known, retransmits any retry-safe operation an earlier
  /// ordinary transport loss orphaned, provided the new session still satisfies its required
  /// trust state; see [RequestPolicy.requiredTrustState] and [ClientSession.admitSession].
  /// @throws [DovahLinkProtocolException] if the bridge rejects authentication.
  Future<HelloResult> hello() => _authenticationService.hello();

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
  /// @throws [DovahLinkConnectionException] if the socket cannot be established (initial or retry).
  /// @throws [DovahLinkProtocolException] if hello is rejected for a non-recoverable reason, or the
  ///     retry attempt is itself rejected.
  Future<HelloResult> authenticate(Uri uri) =>
      _authenticationService.authenticate(uri);

  /// Starts, or queries the status of, a pairing challenge. Valid only on an `unpaired` session.
  /// [PairingChallengeStatus.availability] being [PairingAvailability.otherDevicePairing] means a
  /// different clientId currently owns the active challenge or pending credential.
  Future<PairingChallengeStatus> requestPairing() =>
      _pairingService.requestPairing();

  /// Requests redisplay of the active pairing challenge's code the caller owns. Never generates a
  /// new code and never sends the code itself over the wire -- redisplay occurs through the
  /// in-game notification, not the connection. Valid only on an `unpaired` session.
  Future<PairingRenotifyResult> requestPairingRenotify() =>
      _pairingService.requestPairingRenotify();

  /// Gives up an owned active challenge or pending credential, freeing the slot for a fresh
  /// [requestPairing]. Never touches persisted trust or an already-committed credential. Valid
  /// only on an `unpaired` session.
  Future<PairingCancelOutcome> cancelPairing() =>
      _pairingService.cancelPairing();

  /// Submits the six-digit code the user read from Skyrim. Durably persists the issued credential
  /// and a `CONFIRMING` recovery state before returning it, per
  /// `ai/context/protocol/security.md`'s "client durably persists its issued credential and its
  /// `CONFIRMING` recovery state before sending final confirmation."
  /// @return The issued credential, already persisted.
  /// @throws [DovahLinkPairingException] if the code was expired, invalid, paced too soon, or
  ///     hit the hard wrong-attempt limit.
  Future<String> confirmPairingCode({
    required String code,
    String? displayName,
  }) =>
      _pairingService.confirmPairingCode(code: code, displayName: displayName);

  /// Echoes back a [credential] durably saved from [confirmPairingCode], completing pairing.
  /// [trustState] becomes [DovahLinkTrustState.trusted] on success, and the persisted recovery
  /// state clears back to [PairingRecoveryState.none] while keeping the credential.
  /// @throws [DovahLinkPairingException] if the bridge has no matching pending confirmation.
  Future<void> acknowledgeTrustedCredential(String credential) =>
      _pairingService.acknowledgeTrustedCredential(credential);

  /// Resumes an interrupted pairing confirmation after a crash or relaunch, per
  /// `ai/context/protocol/security.md`'s "a client that saves the credential but crashes before
  /// confirming retries confirmation on restart." Call after [hello] admits an `unpaired` session.
  ///
  /// A no-op returning [DovahLinkTrustState.unpaired] when no confirmation is outstanding. When
  /// one is, retries [acknowledgeTrustedCredential] with the stored credential: a
  /// `pending_not_found` outcome (the bridge restarted and lost the pending credential) discards
  /// the local credential and resets to unpaired rather than treating that as a fatal error; any
  /// other failure leaves the `CONFIRMING` state untouched so a later relaunch can retry again.
  Future<DovahLinkTrustState> recoverPendingPairing() =>
      _pairingService.recoverPendingPairing();

  /// Closes the connection and resets in-memory session state. Idempotent, and never throws: this
  /// is a best-effort cleanup operation, matching [DovahLinkTransport.close]'s own "Idempotent"
  /// contract. In-memory state resets even when the underlying transport cannot be closed
  /// cleanly -- a broken close must not leave [connectionState]/[trustState]/[sessionId] lying
  /// about a session that no longer exists. Persisted identity, credential, and recovery state are
  /// untouched -- trust survives a disconnect. Fails any operation still awaiting a reply, and any
  /// operation an earlier transport loss orphaned for retry, instead of leaving it to hang
  /// forever: unlike an unexpected transport loss, a deliberate disconnect never retries. Repeated
  /// calls remain safe because transport close and pending-operation failure are idempotent; an
  /// administrative invalidation's typed reason is preserved, not reset to generic disconnect.
  Future<void> disconnect() => _session.disconnect();

  /// Discards the persisted pairing credential and recovery state while preserving [clientId], so
  /// the next [hello] presents [AuthMethod.unpaired] instead of a credential the bridge has
  /// already rejected. Call after a `trusted_device_credential` hello is rejected
  /// (`unauthenticated`/`revoked`) and before retrying -- this installation's identity is not
  /// itself invalid, only its stored credential. Does not touch the transport or in-memory
  /// connection state; call [disconnect] separately if the connection also needs resetting.
  Future<void> forgetCredential() => _authenticationService.forgetCredential();
}
