import 'dart:async';

import 'package:meta/meta.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_pairing_exception.dart';
import 'package:dovahlink_client_sdk/src/hello_result.dart';
import 'package:dovahlink_client_sdk/src/internal/authentication/authentication_service.dart';
import 'package:dovahlink_client_sdk/src/internal/authentication/client_id_resolver.dart';
import 'package:dovahlink_client_sdk/src/internal/pairing/pairing_service.dart';
import 'package:dovahlink_client_sdk/src/internal/random_id_generator.dart';
import 'package:dovahlink_client_sdk/src/internal/reconnect/reconnect_service.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/message_router.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/pending_operation_bookkeeping.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/pending_operation_transmitter.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/request_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/connection_teardown_coordinator.dart';
import 'package:dovahlink_client_sdk/src/internal/session/lifecycle_operation_queue.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_admission_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_state.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_trust_service.dart';
import 'package:dovahlink_client_sdk/src/pairing_cancel_outcome.dart';
import 'package:dovahlink_client_sdk/src/pairing_challenge_status.dart';
import 'package:dovahlink_client_sdk/src/pairing_renotify_result.dart';
import 'package:dovahlink_client_sdk/src/persistence/client_storage.dart';
import 'package:dovahlink_client_sdk/src/persistence/windows/dpapi_client_storage.dart';
import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/constants.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/transport/websocket_transport.dart';

/// A real, Flutter/Redux-independent DovahLink protocol client: connect, authenticate, pair, and
/// disconnect. Owns its local `clientId`, pairing credential, and `CONFIRMING` recovery state
/// behind [IClientStorage] -- see `ai/context/sdk/persistence.md`'s ownership rule -- so a consumer
/// never threads identity or credential material through this API by hand.
///
/// Never exposes raw JSON or transport details: every method takes and returns typed values.
class DovahLinkClient {
  /// The SDK-owned persistence boundary for this client's identity, credential, and pairing
  /// recovery state.
  final IClientStorage _storage;

  /// Creates a client. [transport] defaults to a real [WebSocketTransport]; inject a fake for
  /// deterministic tests. [storage] is required so every consumer makes its persistence choice
  /// explicit; see [DovahLinkClient.windows] for the real Windows-backed convenience factory.
  DovahLinkClient({
    IDovahLinkTransport? transport,
    required IClientStorage storage,
  }) : this._build(
         transport: transport ?? WebSocketTransport(),
         storage: storage,
         timeoutDurations: kTimeoutClassDurations,
       );

  /// Creates a client backed by real infrastructure: a [WebSocketTransport] and a
  /// [DpapiClientStorage] persisting to this Windows user's default per-user location.
  factory DovahLinkClient.windows() =>
      DovahLinkClient(storage: DpapiClientStorage());

  /// Creates a client with directly-injected [timeoutDurations], bypassing the centralized
  /// production defaults in `shared/constants.dart`. Test-only: production code must always use
  /// the unnamed constructor so every operation shares the same centrally tuned timeout policy.
  @visibleForTesting
  DovahLinkClient.withTimeoutDurations({
    required IDovahLinkTransport transport,
    required IClientStorage storage,
    required Map<TimeoutClass, Duration> timeoutDurations,
  }) : this._build(
         transport: transport,
         storage: storage,
         timeoutDurations: timeoutDurations,
       );

  /// Creates a client with directly-injected reconnect timing controls. Test-only: production code
  /// must use the unnamed constructor so reconnect shares the centrally tuned policy.
  @visibleForTesting
  DovahLinkClient.withReconnectPolicy({
    required IDovahLinkTransport transport,
    required IClientStorage storage,
    required List<Duration> attemptDelays,
    required Duration deadline,
    DateTime Function() now = DateTime.now,
  }) : this._build(
         transport: transport,
         storage: storage,
         timeoutDurations: kTimeoutClassDurations,
         attemptDelays: attemptDelays,
         reconnectDeadline: deadline,
         reconnectNow: now,
       );

  /// Assembles the full seven-service object graph over [transport], timed per
  /// [timeoutDurations] and recovered with the supplied reconnect policy, per
  /// `ai/context/sdk/architecture.md`'s "Internal composition" -- the one wiring path all client
  /// constructors and factories share, differing only in [transport] source, [timeoutDurations],
  /// and test-only reconnect controls. Every collaborator is constructed here, exactly once, and
  /// handed to its consumer as an already-built constructor parameter; no class below this
  /// composition root ever constructs one of its own dependencies.
  DovahLinkClient._build({
    required IDovahLinkTransport transport,
    required IClientStorage storage,
    required Map<TimeoutClass, Duration> timeoutDurations,
    List<Duration> attemptDelays = kReconnectAttemptDelays,
    Duration reconnectDeadline = kReconnectDeadline,
    DateTime Function() reconnectNow = DateTime.now,
  }) : _storage = storage {
    final SessionState state = SessionState();
    final LifecycleOperationQueue lifecycleQueue = LifecycleOperationQueue();
    // Forwards to `_sessionService.onTeardown`, referenced here before `_sessionService` is
    // assigned below -- resolved only when a real teardown later invokes it, by which point
    // construction has completed. `ConnectionTeardownCoordinator` must exist before
    // `SessionService` (which owns it), but the failure handler it needs can only be supplied
    // by `RequestService`, which itself depends on `ISessionService` and so must be built after
    // it -- see `ai/context/sdk/architecture.md`'s "Callbacks".
    final ConnectionTeardownCoordinator teardownCoordinator =
        ConnectionTeardownCoordinator(
          transport: transport,
          lifecycleQueue: lifecycleQueue,
          pendingOperationFailureHandler:
              (Exception reason, {required bool orphanRetrySafeOperations}) =>
                  _sessionService.onTeardown?.call(
                    reason,
                    orphanRetrySafeOperations: orphanRetrySafeOperations,
                  ),
          state: state,
        );
    _sessionService = SessionService(
      transport: transport,
      state: state,
      lifecycleQueue: lifecycleQueue,
      teardownCoordinator: teardownCoordinator,
    );

    final PendingOperationBookkeeping bookkeeping =
        PendingOperationBookkeeping();
    final PendingOperationTransmitter transmitter = PendingOperationTransmitter(
      transport: transport,
      timeoutDurations: timeoutDurations,
      sessionService: _sessionService,
      bookkeeping: bookkeeping,
    );
    final MessageRouter messageRouter = MessageRouter(
      bookkeeping: bookkeeping,
      sessionService: _sessionService,
    );
    _requestService = RequestService(
      sessionService: _sessionService,
      bookkeeping: bookkeeping,
      transmitter: transmitter,
      messageRouter: messageRouter,
    );
    _sessionService.onIncomingMessage = _requestService.handleIncoming;

    final SessionAdmissionService sessionAdmissionService =
        SessionAdmissionService(state: state, requestService: _requestService);
    final SessionTrustService sessionTrustService = SessionTrustService(
      state: state,
    );
    final ClientIdResolver clientIdResolver = ClientIdResolver(
      storage: _storage,
      randomIdGenerator: RandomIdGenerator(),
    );
    _authenticationService = AuthenticationService(
      sessionService: _sessionService,
      sessionAdmissionService: sessionAdmissionService,
      requestService: _requestService,
      storage: _storage,
      clientIdResolver: clientIdResolver,
    );
    _sessionService
        .onTeardown = (Exception reason, {required bool orphanRetrySafeOperations}) {
      _requestService.failAll(
        reason,
        orphanRetrySafeOperations: orphanRetrySafeOperations,
      );
      if (_sessionService.invalidationReason != null) {
        unawaited(
          _authenticationService.forgetCredential().catchError((
            Object _,
            StackTrace __,
          ) {
            // Invalidation is already terminal; a later explicit authentication can retry this
            // best-effort cleanup when the persistence failure has been resolved.
          }),
        );
      }
    };
    _pairingService = PairingService(
      sessionTrustService: sessionTrustService,
      requestService: _requestService,
      storage: _storage,
    );
    _reconnectService = ReconnectService(
      sessionService: _sessionService,
      authenticationService: _authenticationService,
      attemptDelays: attemptDelays,
      deadline: reconnectDeadline,
      now: reconnectNow,
    );
    _sessionService.onOrdinaryTransportLoss =
        _reconnectService.onOrdinaryTransportLoss;
  }

  /// Owns transport lifecycle, connection state, and stream ownership -- the sole owner of every
  /// socket-scoped field this client has; see `ai/context/sdk/architecture.md`'s "Session-state
  /// ownership". This façade never assigns session state directly; session transitions remain
  /// owned by [SessionService]. Typed as the implementation, not [ISessionService], because only
  /// [DovahLinkClient] itself assigns its late-bound callback fields.
  late final SessionService _sessionService;

  /// Owns pending requests, timeouts, and retry behavior for this client's session.
  late final IRequestService _requestService;

  /// Owns `hello`/authentication and credential-rejection recovery.
  late final IAuthenticationService _authenticationService;

  /// Owns pairing operations.
  late final IPairingService _pairingService;

  /// Owns bounded automatic recovery from ordinary transport loss.
  late final IReconnectService _reconnectService;

  /// The current connection lifecycle phase. Reaches
  /// [DovahLinkConnectionState.reconnecting] only after ordinary, unexpected transport loss (never
  /// after [disconnect] or an administrative invalidation), moving to
  /// [DovahLinkConnectionState.reauthenticating] once that recovery attempt's transport reconnects
  /// -- trust not yet confirmed -- and resolving on its own to
  /// [DovahLinkConnectionState.connected] once that attempt's `hello` actually admits a session, or
  /// to [DovahLinkConnectionState.disconnected] once the recovery cycle is exhausted first; no
  /// action from this client is required to observe or drive that recovery.
  DovahLinkConnectionState get connectionState =>
      _sessionService.connectionState;

  /// A stream of every [connectionState] transition: the current value immediately on listen,
  /// then each subsequent real change -- including administrative invalidation, without waiting
  /// for another request to notice it. See [ISessionService.connectionStateChanges].
  Stream<DovahLinkConnectionState> get connectionStateChanges =>
      _sessionService.connectionStateChanges;

  /// The current trust standing, or `null` before [hello] succeeds.
  DovahLinkTrustState? get trustState => _sessionService.currentTrustState;

  /// The server-issued session identifier, or `null` before [hello] succeeds.
  String? get sessionId => _sessionService.currentSessionId;

  /// This installation's stable client ID, or `null` before [hello] has resolved it.
  String? get clientId => _authenticationService.clientId;

  /// The reason [connectionState] is [DovahLinkConnectionState.administrativelyInvalidated], or
  /// `null` otherwise.
  AdministrativeInvalidationReason? get invalidationReason =>
      _sessionService.invalidationReason;

  /// Establishes the transport connection to [uri]. Must be called before [hello].
  /// @throws [DovahLinkConnectionException] if the socket cannot be established.
  Future<void> connect(Uri uri) => _sessionService.connect(uri);

  /// Sends `hello` and negotiates the session. Resolves and persists this installation's
  /// `clientId` on first use, and automatically presents a stored trusted credential as
  /// `trusted_device_credential` for an ordinary reconnect. Admits `unpaired` both when no
  /// credential is stored yet and when a `CONFIRMING` pairing is still outstanding -- the bridge
  /// has not yet committed that credential as trusted, so it must not be presented as one. Once
  /// the new session's trust state is known, retransmits any retry-safe operation an earlier
  /// ordinary transport loss orphaned, provided the new session still satisfies its required
  /// trust state; see [RequestPolicy.requiredTrustState] and [IAuthenticationService.hello].
  /// @throws [DovahLinkProtocolException] if the bridge rejects authentication.
  Future<HelloResult> hello() => _authenticationService.hello();

  /// See [IAuthenticationService.authenticate].
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
  /// @throws [DovahLinkPairingException] if the bridge has no matching pending confirmation or
  ///     an administrative mutation invalidated the pending credential.
  Future<void> acknowledgeTrustedCredential(String credential) =>
      _pairingService.acknowledgeTrustedCredential(credential);

  /// Resumes an interrupted pairing confirmation after a crash or relaunch, per
  /// `ai/context/protocol/security.md`'s "a client that saves the credential but crashes before
  /// confirming retries confirmation on restart." Call after [hello] admits an `unpaired` session.
  ///
  /// A no-op returning [DovahLinkTrustState.unpaired] when no confirmation is outstanding. When
  /// one is, retries [acknowledgeTrustedCredential] with the stored credential: a
  /// `pending_not_found` outcome (the bridge restarted and lost the pending credential) or
  /// `pairing_invalidated` outcome (an administrative mutation rejected the pending credential)
  /// discards the local credential and resets to unpaired rather than treating that as a fatal
  /// error; any other failure leaves the `CONFIRMING` state untouched so a later relaunch can retry
  /// again.
  Future<DovahLinkTrustState> recoverPendingPairing() =>
      _pairingService.recoverPendingPairing();

  /// Closes the connection and resets in-memory session state. Idempotent, and never throws: this
  /// is a best-effort cleanup operation, matching [IDovahLinkTransport.close]'s own "Idempotent"
  /// contract. In-memory state resets even when the underlying transport cannot be closed
  /// cleanly -- a broken close must not leave [connectionState]/[trustState]/[sessionId] lying
  /// about a session that no longer exists. Persisted identity, credential, and recovery state are
  /// untouched -- trust survives a disconnect. Fails any operation still awaiting a reply, and any
  /// operation an earlier transport loss orphaned for retry, instead of leaving it to hang
  /// forever: unlike an unexpected transport loss, a deliberate disconnect never retries. Also
  /// cancels bounded automatic recovery already in progress from an earlier transport loss --
  /// [connectionState] moves directly to [DovahLinkConnectionState.disconnected] rather than
  /// letting that recovery keep running. Repeated calls remain safe because transport close and
  /// pending-operation failure are idempotent; an administrative invalidation's typed reason is
  /// preserved, not reset to generic disconnect.
  Future<void> disconnect() => _sessionService.disconnect();

  /// Discards the persisted pairing credential and recovery state while preserving [clientId], so
  /// the next [hello] presents [AuthMethod.unpaired] instead of a credential the bridge has
  /// already rejected. Call after a `trusted_device_credential` hello is rejected
  /// (`unauthenticated`/`revoked`) and before retrying -- this installation's identity is not
  /// itself invalid, only its stored credential. Does not touch the transport or in-memory
  /// connection state; call [disconnect] separately if the connection also needs resetting.
  Future<void> forgetCredential() => _authenticationService.forgetCredential();
}
