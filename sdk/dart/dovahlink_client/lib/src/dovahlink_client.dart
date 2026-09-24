import 'dart:async';

import 'package:meta/meta.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_compatibility_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_pairing_exception.dart';
import 'package:dovahlink_client_sdk/src/hello_result.dart';
import 'package:dovahlink_client_sdk/src/internal/authentication/authentication_service.dart';
import 'package:dovahlink_client_sdk/src/internal/authentication/client_id_cache.dart';
import 'package:dovahlink_client_sdk/src/internal/authentication/client_id_resolver.dart';
import 'package:dovahlink_client_sdk/src/internal/pairing/pairing_service.dart';
import 'package:dovahlink_client_sdk/src/internal/random_id_generator.dart';
import 'package:dovahlink_client_sdk/src/internal/reconnect/reconnect_service.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/message_router.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/pending_operation_bookkeeping.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/pending_operation_transmitter.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/request_service.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/unsolicited_message_handler.dart';
import 'package:dovahlink_client_sdk/src/internal/session/connection_teardown_coordinator.dart';
import 'package:dovahlink_client_sdk/src/internal/session/lifecycle_operation_queue.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_admission_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_state.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_trust_service.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_domain_definition.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_message_handler.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_recovery_service.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_revision_tracker.dart';
import 'package:dovahlink_client_sdk/src/internal/state/subscription_service.dart';
import 'package:dovahlink_client_sdk/src/pairing_cancel_outcome.dart';
import 'package:dovahlink_client_sdk/src/pairing_challenge_status.dart';
import 'package:dovahlink_client_sdk/src/pairing_renotify_result.dart';
import 'package:dovahlink_client_sdk/src/persistence/client_storage.dart';
import 'package:dovahlink_client_sdk/src/persistence/windows/dpapi_client_storage.dart';
import 'package:dovahlink_client_sdk/src/shared/constants.dart';
import 'package:dovahlink_client_sdk/src/shared/current_value_stream.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/state/character_health_state.dart';
import 'package:dovahlink_client_sdk/src/state/character_level_state.dart';
import 'package:dovahlink_client_sdk/src/state/character_magicka_state.dart';
import 'package:dovahlink_client_sdk/src/state/character_stamina_state.dart';
import 'package:dovahlink_client_sdk/src/state/character_xp_state.dart';
import 'package:dovahlink_client_sdk/src/state/state_synchronization.dart';
import 'package:dovahlink_client_sdk/src/transport/websocket_transport.dart';

/// A real, Flutter/Redux-independent DovahLink protocol client: connect, authenticate, pair, and
/// disconnect. Owns its local [DovahLinkClient.clientId], pairing credential, and
/// [PairingRecoveryState.confirming] recovery state through [IClientStorage], so a consumer never
/// threads identity or credential material through this API by hand.
///
/// Never exposes raw JSON or transport details: every method takes and returns typed values.
class DovahLinkClient {
  /// The SDK-owned persistence boundary for this client's identity, credential, and pairing
  /// recovery state.
  final IClientStorage _storage;

  /// Creates a client. [storage] is required so every consumer makes its persistence choice
  /// explicit; see [DovahLinkClient.windows] for the real Windows-backed convenience factory.
  /// @param storage The SDK-owned storage boundary for this client's identity and credential.
  DovahLinkClient({required IClientStorage storage})
    : this._build(
        transport: WebSocketTransport(),
        storage: storage,
        timeoutDurations: kTimeoutClassDurations,
      );

  /// Creates a client backed by the SDK's default WebSocket transport and DPAPI storage for this
  /// Windows user's default per-user location.
  factory DovahLinkClient.windows() =>
      DovahLinkClient(storage: DpapiClientStorage());

  /// Assembles the client services over [transport], applies [timeoutDurations] and the supplied
  /// reconnect policy, and passes each collaborator to its owner. Public and test factories share
  /// this composition root.
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
    // The callback closes over the session service before it is assigned; it is only invoked by
    // a real teardown after construction has completed. The request service supplies teardown's
    // failure handler once the session service is available.
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

    final CurrentValueStream<StateSynchronization<CharacterXpState>>
    characterXpStream =
        CurrentValueStream<StateSynchronization<CharacterXpState>>(
          const StateSynchronization<CharacterXpState>.notSubscribed(),
        );
    _characterXpTracker = StateRevisionTracker<CharacterXpState>(
      state: characterXpStream,
    );
    final CurrentValueStream<StateSynchronization<CharacterHealthState>>
    characterHealthStream =
        CurrentValueStream<StateSynchronization<CharacterHealthState>>(
          const StateSynchronization<CharacterHealthState>.notSubscribed(),
        );
    _characterHealthTracker = StateRevisionTracker<CharacterHealthState>(
      state: characterHealthStream,
    );
    final CurrentValueStream<StateSynchronization<CharacterMagickaState>>
    characterMagickaStream =
        CurrentValueStream<StateSynchronization<CharacterMagickaState>>(
          const StateSynchronization<CharacterMagickaState>.notSubscribed(),
        );
    _characterMagickaTracker = StateRevisionTracker<CharacterMagickaState>(
      state: characterMagickaStream,
    );
    final CurrentValueStream<StateSynchronization<CharacterStaminaState>>
    characterStaminaStream =
        CurrentValueStream<StateSynchronization<CharacterStaminaState>>(
          const StateSynchronization<CharacterStaminaState>.notSubscribed(),
        );
    _characterStaminaTracker = StateRevisionTracker<CharacterStaminaState>(
      state: characterStaminaStream,
    );
    final CurrentValueStream<StateSynchronization<CharacterLevelState>>
    characterLevelStream =
        CurrentValueStream<StateSynchronization<CharacterLevelState>>(
          const StateSynchronization<CharacterLevelState>.notSubscribed(),
        );
    _characterLevelTracker = StateRevisionTracker<CharacterLevelState>(
      state: characterLevelStream,
    );

    final StateDomainDefinition<CharacterLevelState> characterLevelDomain =
        StateDomainDefinition<CharacterLevelState>(
          stateArea: DovahLinkStateArea.characterLevel.protocolValue,
          decode: CharacterLevelState.fromJson,
          tracker: _characterLevelTracker,
          isUnavailable: (CharacterLevelState state) => state.value == null,
          supportsEvents: true,
        );
    final IStateMessageHandler stateMessageHandler = StateMessageHandler(
      sessionService: _sessionService,
      domains: <IStateDomainDefinition<Object?>>[
        StateDomainDefinition<CharacterXpState>(
          stateArea: DovahLinkStateArea.characterXp.protocolValue,
          decode: CharacterXpState.fromJson,
          tracker: _characterXpTracker,
          isUnavailable: (CharacterXpState state) => state.value == null,
        ),
        StateDomainDefinition<CharacterHealthState>(
          stateArea: DovahLinkStateArea.characterHealth.protocolValue,
          decode: CharacterHealthState.fromJson,
          tracker: _characterHealthTracker,
          isUnavailable: (CharacterHealthState state) => state.value == null,
        ),
        StateDomainDefinition<CharacterMagickaState>(
          stateArea: DovahLinkStateArea.characterMagicka.protocolValue,
          decode: CharacterMagickaState.fromJson,
          tracker: _characterMagickaTracker,
          isUnavailable: (CharacterMagickaState state) => state.value == null,
        ),
        StateDomainDefinition<CharacterStaminaState>(
          stateArea: DovahLinkStateArea.characterStamina.protocolValue,
          decode: CharacterStaminaState.fromJson,
          tracker: _characterStaminaTracker,
          isUnavailable: (CharacterStaminaState state) => state.value == null,
        ),
        characterLevelDomain,
      ],
    );
    final IUnsolicitedMessageHandler unsolicitedMessageHandler =
        UnsolicitedMessageHandler(
          sessionService: _sessionService,
          stateMessageHandler: stateMessageHandler,
        );

    final PendingOperationBookkeeping bookkeeping =
        PendingOperationBookkeeping();
    // Build this shared cache before authentication: both authentication and the request
    // transmitter need the same resolved `clientId`, and making either depend on the other would
    // create a construction-order cycle.
    final ClientIdCache clientIdCache = ClientIdCache();
    final PendingOperationTransmitter transmitter = PendingOperationTransmitter(
      transport: transport,
      timeoutDurations: timeoutDurations,
      sessionService: _sessionService,
      bookkeeping: bookkeeping,
      clientIdCache: clientIdCache,
    );
    final MessageRouter messageRouter = MessageRouter(
      bookkeeping: bookkeeping,
      sessionService: _sessionService,
      unsolicitedMessageHandler: unsolicitedMessageHandler,
    );
    _requestService = RequestService(
      sessionService: _sessionService,
      bookkeeping: bookkeeping,
      transmitter: transmitter,
      messageRouter: messageRouter,
    );
    _sessionService.onIncomingMessage = _requestService.handleIncoming;
    _subscriptionService = SubscriptionService(
      requestService: _requestService,
      sessionService: _sessionService,
      stateMessageHandler: stateMessageHandler,
    );

    final StateRecoveryService<CharacterLevelState> levelRecoveryService =
        StateRecoveryService<CharacterLevelState>(
          domain: characterLevelDomain,
          requestService: _requestService,
          sessionService: _sessionService,
        );
    levelRecoveryService.start();

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
      clientIdCache: clientIdCache,
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

  /// Owns transport lifecycle, connection state, and stream ownership. This façade reads its
  /// state but leaves transitions to [SessionService]; it keeps the concrete type to assign the
  /// session's late-bound callbacks.
  late final SessionService _sessionService;

  /// Owns pending requests, timeouts, and retry behavior for this client's session.
  late final IRequestService _requestService;

  /// Owns desired state-area subscriptions and their current Host acknowledgement.
  late final ISubscriptionService _subscriptionService;

  /// Owns [IAuthenticationService.hello] and saved-credential rejection recovery.
  late final IAuthenticationService _authenticationService;

  /// Owns pairing operations.
  late final IPairingService _pairingService;

  /// Owns bounded automatic recovery from ordinary transport loss.
  late final IReconnectService _reconnectService;

  /// Owns the current character experience value and revisions.
  late final IStateRevisionTracker<CharacterXpState> _characterXpTracker;

  /// Owns the current character health value and revisions.
  late final IStateRevisionTracker<CharacterHealthState>
  _characterHealthTracker;

  /// Owns the current character magicka value and revisions.
  late final IStateRevisionTracker<CharacterMagickaState>
  _characterMagickaTracker;

  /// Owns the current character stamina value and revisions.
  late final IStateRevisionTracker<CharacterStaminaState>
  _characterStaminaTracker;

  /// Owns the current character level value and revisions.
  late final IStateRevisionTracker<CharacterLevelState> _characterLevelTracker;

  /// The current connection lifecycle phase. Reaches
  /// [DovahLinkConnectionState.reconnecting] only after ordinary, unexpected transport loss (never
  /// after [DovahLinkClient.disconnect] or an administrative invalidation), moving to
  /// [DovahLinkConnectionState.reauthenticating] once that recovery attempt's transport reconnects
  /// -- trust not yet confirmed -- and resolving on its own to
  /// [DovahLinkConnectionState.connected] once that attempt's `hello` actually admits a session, or
  /// to [DovahLinkConnectionState.disconnected] once the recovery cycle is exhausted first; no
  /// action from this client is required to observe or drive that recovery.
  DovahLinkConnectionState get connectionState =>
      _sessionService.connectionState;

  /// Emits [DovahLinkClient.connectionState] immediately on listen and then every real transition,
  /// including administrative invalidation.
  Stream<DovahLinkConnectionState> get connectionStateChanges =>
      _sessionService.connectionStateChanges;

  /// The current trust standing, or `null` before [DovahLinkClient.hello] succeeds.
  DovahLinkTrustState? get trustState => _sessionService.currentTrustState;

  /// The server-issued session identifier, or `null` before [DovahLinkClient.hello] succeeds.
  String? get sessionId => _sessionService.currentSessionId;

  /// This installation's stable client ID, or `null` before [DovahLinkClient.hello] has resolved
  /// it.
  String? get clientId => _authenticationService.clientId;

  /// The reason [DovahLinkClient.connectionState] is
  /// [DovahLinkConnectionState.administrativelyInvalidated], or
  /// `null` otherwise.
  AdministrativeInvalidationReason? get invalidationReason =>
      _sessionService.invalidationReason;

  /// Emits typed character experience values with their independent synchronization status.
  /// @return The current experience view immediately on listen and after each accepted update.
  Stream<StateSynchronization<CharacterXpState>> get characterXpChanges =>
      _characterXpTracker.changes;

  /// Emits typed character health values with their independent synchronization status.
  /// @return The current health view immediately on listen and after each accepted update.
  Stream<StateSynchronization<CharacterHealthState>>
  get characterHealthChanges => _characterHealthTracker.changes;

  /// Emits typed character magicka values with their independent synchronization status.
  /// @return The current magicka view immediately on listen and after each accepted update.
  Stream<StateSynchronization<CharacterMagickaState>>
  get characterMagickaChanges => _characterMagickaTracker.changes;

  /// Emits typed character stamina values with their independent synchronization status.
  /// @return The current stamina view immediately on listen and after each accepted update.
  Stream<StateSynchronization<CharacterStaminaState>>
  get characterStaminaChanges => _characterStaminaTracker.changes;

  /// Emits typed character level values with their independent synchronization status.
  /// @return The current level view immediately on listen and after each accepted update.
  Stream<StateSynchronization<CharacterLevelState>> get characterLevelChanges =>
      _characterLevelTracker.changes;

  /// Requests [area] as part of this client's complete desired state-area set.
  /// @param area The state domain to request.
  /// @return The areas the Host rejected from the resulting desired set.
  /// @throws [DovahLinkConnectionException] if no trusted session is active.
  /// @throws [DovahLinkProtocolException] if the Host returns a malformed acknowledgement.
  Future<Set<DovahLinkStateArea>> subscribeStateArea(DovahLinkStateArea area) =>
      _subscriptionService.subscribeStateArea(area);

  /// Removes [area] from this client's complete desired state-area set.
  /// @param area The state domain to remove.
  /// @return The areas the Host rejected from the resulting desired set.
  /// @throws [DovahLinkConnectionException] if no trusted session is active.
  /// @throws [DovahLinkProtocolException] if the Host returns a malformed acknowledgement.
  Future<Set<DovahLinkStateArea>> unsubscribeStateArea(
    DovahLinkStateArea area,
  ) => _subscriptionService.unsubscribeStateArea(area);

  /// Establishes the transport connection to [uri]. Must be called before [DovahLinkClient.hello].
  /// @throws [DovahLinkConnectionException] if the socket cannot be established.
  Future<void> connect(Uri uri) => _sessionService.connect(uri);

  /// Sends the `hello` protocol message and negotiates the session. Resolves and persists this
  /// installation's [DovahLinkClient.clientId] on first use, and automatically presents a stored
  /// trusted credential as
  /// `trusted_device_credential` on reconnect, except while a
  /// [PairingRecoveryState.confirming] pairing is outstanding;
  /// the Host has not trusted that credential yet. Once the session is admitted, retries orphaned
  /// requests whose trust requirements the new session satisfies.
  /// @throws [DovahLinkProtocolException] if the Host rejects authentication.
  /// @throws [DovahLinkCompatibilityException] if the Host version is outside the SDK's supported
  ///     range.
  Future<HelloResult> hello() => _authenticationService.hello();

  /// Connects and authenticates. Returns the cached result when
  /// [DovahLinkConnectionState.connected] and [DovahLinkTrustState.trusted]. If a saved credential
  /// is rejected as [CredentialRejectionReason.revoked] or
  /// [CredentialRejectionReason.unrecognized], clears it and retries once with
  /// [AuthMethod.unpaired];
  /// [HelloResult.recoveredFromRejectedCredential] reports that recovery.
  /// @throws [DovahLinkConnectionException] if the socket cannot be established (initial or retry).
  /// @throws [DovahLinkProtocolException] if hello is rejected for a non-recoverable reason, or the
  ///     retry attempt is itself rejected.
  /// @throws [DovahLinkCompatibilityException] if the Host version is outside the SDK's supported
  ///     range.
  Future<HelloResult> authenticate(Uri uri) =>
      _authenticationService.authenticate(uri);

  /// Starts, or queries the status of, a pairing challenge. Valid only on an
  /// [DovahLinkTrustState.unpaired] session.
  /// [PairingChallengeStatus.availability] being [PairingAvailability.otherDevicePairing] means a
  /// different clientId currently owns the active challenge or pending credential.
  Future<PairingChallengeStatus> requestPairing() =>
      _pairingService.requestPairing();

  /// Requests redisplay of the active pairing challenge's code the caller owns. Never generates a
  /// new code and never sends the code itself over the wire -- redisplay occurs through the
  /// in-game notification, not the connection. Valid only on a
  /// [DovahLinkTrustState.unpaired] session.
  Future<PairingRenotifyResult> requestPairingRenotify() =>
      _pairingService.requestPairingRenotify();

  /// Gives up an owned active challenge or pending credential, freeing the slot for a fresh
  /// [DovahLinkClient.requestPairing]. Never touches persisted trust or an already-committed
  /// credential. Valid only on an [DovahLinkTrustState.unpaired] session.
  Future<PairingCancelOutcome> cancelPairing() =>
      _pairingService.cancelPairing();

  /// Submits the six-digit code the user read from Skyrim. Durably persists the issued credential
  /// and a [PairingRecoveryState.confirming] recovery state before returning it, so an interrupted
  /// final confirmation can be resumed safely.
  /// @return The issued credential, already persisted.
  /// @throws [DovahLinkPairingException] if the code was expired, invalid, paced too soon, or
  ///     hit the hard wrong-attempt limit.
  Future<String> confirmPairingCode({
    required String code,
    String? displayName,
  }) =>
      _pairingService.confirmPairingCode(code: code, displayName: displayName);

  /// Echoes back a [credential] durably saved from [DovahLinkClient.confirmPairingCode],
  /// completing pairing. [DovahLinkClient.trustState] becomes
  /// [DovahLinkTrustState.trusted] on success, and the persisted recovery state clears back to
  /// [PairingRecoveryState.none] while keeping the credential.
  /// @throws [DovahLinkPairingException] if the Host has no matching pending confirmation or
  ///     an administrative mutation invalidated the pending credential.
  Future<void> acknowledgeTrustedCredential(String credential) =>
      _pairingService.acknowledgeTrustedCredential(credential);

  /// Resumes an interrupted pairing confirmation after a crash or relaunch. Call after
  /// [DovahLinkClient.hello] admits a [DovahLinkTrustState.unpaired] session.
  ///
  /// A no-op returning [DovahLinkTrustState.unpaired] when no confirmation is outstanding. When
  /// one is, retries [DovahLinkClient.acknowledgeTrustedCredential] with the stored credential: a
  /// `pending_not_found` outcome (the Host restarted and lost the pending credential) or
  /// `pairing_invalidated` outcome (an administrative mutation rejected the pending credential)
  /// discards the local credential and resets to [DovahLinkTrustState.unpaired] rather than
  /// treating that as a fatal error; any other failure leaves [PairingRecoveryState.confirming]
  /// untouched so a later relaunch can retry.
  Future<DovahLinkTrustState> recoverPendingPairing() =>
      _pairingService.recoverPendingPairing();

  /// Closes the connection and resets in-memory session state. Idempotent, and never throws: this
  /// is a best-effort cleanup operation matching the transport's idempotent close contract.
  /// In-memory state resets even when the underlying transport cannot be closed
  /// cleanly -- a broken close must not leave [DovahLinkClient.connectionState],
  /// [DovahLinkClient.trustState], or [DovahLinkClient.sessionId] lying
  /// about a session that no longer exists. Persisted identity, credential, and recovery state are
  /// untouched -- trust survives a disconnect. Fails any operation still awaiting a reply, and any
  /// operation an earlier transport loss orphaned for retry, instead of leaving it to hang
  /// forever: unlike an unexpected transport loss, a deliberate disconnect never retries. Also
  /// cancels bounded automatic recovery already in progress from an earlier transport loss --
  /// [DovahLinkClient.connectionState] moves directly to
  /// [DovahLinkConnectionState.disconnected] rather than
  /// letting that recovery keep running. Repeated calls remain safe because transport close and
  /// pending-operation failure are idempotent; an administrative invalidation's typed reason is
  /// preserved, not reset to generic disconnect.
  Future<void> disconnect() => _sessionService.disconnect();

  /// Discards the persisted pairing credential and recovery state while preserving
  /// [DovahLinkClient.clientId], so the next [DovahLinkClient.hello] presents
  /// [AuthMethod.unpaired] instead of a credential the Host has
  /// already rejected. Call after a `trusted_device_credential` hello is rejected
  /// (`unauthenticated`/`revoked`) and before retrying -- this installation's identity is not
  /// itself invalid, only its stored credential. Does not touch the transport or in-memory
  /// connection state; call [DovahLinkClient.disconnect] separately if the connection also needs
  /// resetting.
  Future<void> forgetCredential() => _authenticationService.forgetCredential();
}

/// Creates a client with controllable infrastructure for SDK tests.
/// @param transport The fake or real transport used by this client.
/// @param storage The SDK-owned storage boundary for this client's identity and credential.
/// @param timeoutDurations The per-class request timeouts used by the client.
/// @param reconnectAttemptDelays The bounded reconnect attempt schedule.
/// @param reconnectDeadline The overall limit for one reconnect cycle.
/// @param now The clock used to measure the reconnect deadline.
/// @return A client wired to the supplied transport and timing controls.
@visibleForTesting
DovahLinkClient buildDovahLinkClientForTesting({
  required IDovahLinkTransport transport,
  required IClientStorage storage,
  Map<TimeoutClass, Duration> timeoutDurations = kTimeoutClassDurations,
  List<Duration> reconnectAttemptDelays = kReconnectAttemptDelays,
  Duration reconnectDeadline = kReconnectDeadline,
  DateTime Function() now = DateTime.now,
}) => DovahLinkClient._build(
  transport: transport,
  storage: storage,
  timeoutDurations: timeoutDurations,
  attemptDelays: reconnectAttemptDelays,
  reconnectDeadline: reconnectDeadline,
  reconnectNow: now,
);
