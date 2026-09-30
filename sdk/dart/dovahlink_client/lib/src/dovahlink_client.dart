import 'dart:async';

import 'package:meta/meta.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_compatibility_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_discovery_service.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host_id.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host_identity_mismatch_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_hosts.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_known_host_not_found_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_known_host_state.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_pairing_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_storage_exception.dart';
import 'package:dovahlink_client_sdk/src/hello_result.dart';
import 'package:dovahlink_client_sdk/src/host_presence_probe.dart';
import 'package:dovahlink_client_sdk/src/internal/authentication/authentication_service.dart';
import 'package:dovahlink_client_sdk/src/internal/authentication/client_id_cache.dart';
import 'package:dovahlink_client_sdk/src/internal/authentication/client_id_resolver.dart';
import 'package:dovahlink_client_sdk/src/internal/availability/host_availability_service.dart';
import 'package:dovahlink_client_sdk/src/internal/availability/known_host_presence_monitor.dart';
import 'package:dovahlink_client_sdk/src/internal/pairing/pairing_service.dart';
import 'package:dovahlink_client_sdk/src/internal/persistence/client_state_service.dart';
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
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_known_host.dart';
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

/// A real, Flutter/Redux-independent DovahLink protocol client: connect, authenticate, pair,
/// disconnect, and close. Owns its local [DovahLinkClient.clientId], Host-scoped credentials, pairing
/// recovery, and Known Hosts through its SDK-managed [IClientStorage] boundary, so a consumer never threads
/// credentials through this API.
///
/// Never exposes raw JSON or transport details: every method takes and returns typed values.
class DovahLinkClient {
  /// Owns persisted client state and its semantic change streams.
  final IClientStateService _clientStateService;

  /// Performs the SDK's sessionless local Host discovery.
  late final IDovahLinkDiscoveryService _discoveryService;

  /// Publishes current runtime candidates, replaying the current list to each subscriber.
  final StreamController<List<DovahLinkHost>> _candidateHostsController =
      StreamController<List<DovahLinkHost>>.broadcast();

  /// The latest raw discovery result, retained only for Known Host reconciliation.
  List<DovahLinkHost> _discoveredHosts = const <DovahLinkHost>[];

  /// The current immutable runtime candidate projection.
  List<DovahLinkHost> _candidateHosts = const <DovahLinkHost>[];

  /// The latest discovery generation allowed to publish.
  int _discoveryGeneration = 0;

  /// Whether terminal close has begun.
  bool _isClosed = false;

  /// The terminal cleanup operation shared by repeated callers.
  Future<void>? _closeFuture;

  /// Creates a client. [storage] is required so every consumer makes its persistence choice
  /// explicit.
  /// @param storage The SDK-owned storage boundary for this client's identity and credential.
  DovahLinkClient({required IClientStorage storage})
    : this._build(
        transport: WebSocketTransport(),
        storage: storage,
        timeoutDurations: kTimeoutClassDurations,
        hostPresenceProbe: HostPresenceProbe(),
      );

  /// Assembles the client services over [transport], applies [timeoutDurations] and the supplied
  /// reconnect policy, and passes each collaborator to its owner. Public and test factories share
  /// this composition root.
  DovahLinkClient._build({
    required IDovahLinkTransport transport,
    required IClientStorage storage,
    required Map<TimeoutClass, Duration> timeoutDurations,
    required IHostPresenceProbe hostPresenceProbe,
    bool reconnectEnabled = true,
    bool knownHostPresenceMonitoringEnabled = true,
    IDovahLinkDiscoveryService? discoveryService,
    List<Duration> attemptDelays = kReconnectAttemptDelays,
    Duration reconnectDeadline = kReconnectDeadline,
    DateTime Function() reconnectNow = DateTime.now,
    Duration hostPresenceRefreshInterval = kKnownHostPresenceRefreshInterval,
    Stream<void>? hostPresenceRefreshTicks,
    int hostPresenceMaxConcurrentProbes = kKnownHostPresenceMaxConcurrentProbes,
  }) : _clientStateService = ClientStateService(storage: storage) {
    _discoveryService =
        discoveryService ??
        DovahLinkDiscoveryService(hostPresenceProbe: hostPresenceProbe);
    _hostAvailabilityService = HostAvailabilityService(
      clientStateService: _clientStateService,
    );
    hosts = DovahLinkHosts(
      clientStateService: _clientStateService,
      hostAvailabilityService: _hostAvailabilityService,
    );
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
    _knownHostSessionSubscription = _sessionService.knownHostSessionChanges
        .listen((KnownHostSessionSnapshot snapshot) {
          _hostAvailabilityService.setSessionState(
            snapshot.hostId,
            snapshot.state,
          );
        });

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
        SessionAdmissionService(
          state: state,
          requestService: _requestService,
          subscriptionService: _subscriptionService,
        );
    final SessionTrustService sessionTrustService = SessionTrustService(
      state: state,
    );
    final ClientIdResolver clientIdResolver = ClientIdResolver(
      clientStateService: _clientStateService,
      randomIdGenerator: RandomIdGenerator(),
    );
    _authenticationService = AuthenticationService(
      sessionService: _sessionService,
      sessionAdmissionService: sessionAdmissionService,
      requestService: _requestService,
      clientStateService: _clientStateService,
      hostAvailabilityService: _hostAvailabilityService,
      clientIdResolver: clientIdResolver,
      clientIdCache: clientIdCache,
    );
    _sessionService
        .onTeardown = (Exception reason, {required bool orphanRetrySafeOperations}) {
      _requestService.failAll(
        reason,
        orphanRetrySafeOperations: orphanRetrySafeOperations,
      );
      _subscriptionService.onSessionEnded();
      if (_sessionService.invalidationReason != null) {
        unawaited(
          _authenticationService.forgetLastKnownCredential().catchError((
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
      sessionService: _sessionService,
      sessionTrustService: sessionTrustService,
      requestService: _requestService,
      clientStateService: _clientStateService,
      hostAvailabilityService: _hostAvailabilityService,
    );
    _reconnectService = ReconnectService(
      sessionService: _sessionService,
      authenticationService: _authenticationService,
      hostAvailabilityService: _hostAvailabilityService,
      attemptDelays: attemptDelays,
      deadline: reconnectDeadline,
      now: reconnectNow,
    );
    if (reconnectEnabled) {
      _sessionService.onOrdinaryTransportLoss =
          _reconnectService.onOrdinaryTransportLoss;
    }
    _knownHostPresenceMonitor = KnownHostPresenceMonitor(
      clientStateService: _clientStateService,
      probe: hostPresenceProbe,
      availabilityService: _hostAvailabilityService,
      sessionService: _sessionService,
      refreshInterval: hostPresenceRefreshInterval,
      refreshTicks: hostPresenceRefreshTicks,
      maxConcurrentProbes: hostPresenceMaxConcurrentProbes,
    );
    if (knownHostPresenceMonitoringEnabled) {
      _knownHostPresenceMonitor.start();
    }
  }

  /// Owns transport lifecycle, connection state, and stream ownership. This façade reads its
  /// state but leaves transitions to [SessionService]; it keeps the concrete type to assign the
  /// session's late-bound callbacks.
  late final SessionService _sessionService;

  /// Owns the runtime availability map and complete Known Host projection.
  late final IHostAvailabilityService _hostAvailabilityService;

  /// Exposes this client's Known Host views over the existing state owners.
  late final IDovahLinkHosts hosts;

  /// Mirrors the session owner's exact Known Host projection into complete Known Host snapshots.
  late final StreamSubscription<KnownHostSessionSnapshot>
  _knownHostSessionSubscription;

  /// Tracks authoritative Known Host changes so newly known Hosts leave candidates immediately.
  StreamSubscription<List<DovahLinkHost>>? _knownHostCandidateSubscription;

  /// Owns sessionless startup and periodic presence checks until [close].
  late final IKnownHostPresenceMonitor _knownHostPresenceMonitor;

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

  /// Loads this client's persisted Known Hosts without exposing credentials or storage details.
  /// @return An immutable, Host-ID-sorted collection, empty when no Host is known.
  /// @throws [DovahLinkStorageException] if persisted state cannot be read safely.
  Future<List<DovahLinkHost>> loadKnownHosts() async {
    return hosts.loadKnownHosts();
  }

  /// Discovers Hosts and returns only identities that are not currently Known Hosts.
  ///
  /// Candidate state is runtime-only. The SDK reconciles it with committed Known Host state, and
  /// a later Known Host update removes a matching candidate without a consumer mutation.
  /// @return The complete immutable candidate collection, sorted by normalized Host ID.
  /// @throws DovahLinkStorageException if authoritative Known Host state cannot be read safely.
  Future<List<DovahLinkHost>> discoverHosts() async {
    if (_isClosed) {
      throw StateError('A closed DovahLinkClient cannot discover Hosts.');
    }
    final int generation = ++_discoveryGeneration;
    try {
      await _clientStateService.load();
    } on Object {
      if (_isClosed) {
        return _candidateHosts;
      }
      _observeCandidateKnownHosts();
      rethrow;
    }
    if (_isClosed) {
      return _candidateHosts;
    }
    _observeCandidateKnownHosts();
    late final List<DovahLinkHost> discovered;
    try {
      discovered = await _discoveryService.discover();
    } on Object {
      if (_isClosed) {
        return _candidateHosts;
      }
      rethrow;
    }
    if (_isClosed) {
      return _candidateHosts;
    }
    late final PersistedClientState state;
    try {
      state = await _clientStateService.load();
    } on Object {
      if (_isClosed) {
        return _candidateHosts;
      }
      rethrow;
    }
    if (!_isClosed && generation == _discoveryGeneration) {
      _discoveredHosts = discovered;
      _reconcileCandidateHosts(
        state.knownHosts.values
            .map((PersistedKnownHost relationship) => relationship.host)
            .toList(growable: false),
      );
    }
    return _candidateHosts;
  }

  /// Emits the current runtime-only discovery candidates and each reconciled replacement.
  /// Storage failures are forwarded as stream errors; the subscription stays active for recovery.
  /// @return A replaying stream of immutable candidates ordered by normalized Host ID.
  Stream<List<DovahLinkHost>> get candidateHostsChanges =>
      Stream<List<DovahLinkHost>>.multi((
        MultiStreamController<List<DovahLinkHost>> sink,
      ) {
        final StreamSubscription<List<DovahLinkHost>> subscription =
            _candidateHostsController.stream.listen(
              sink.add,
              onError: sink.addError,
            );
        sink.add(_candidateHosts);
        sink.onCancel = subscription.cancel;
      }, isBroadcast: true);

  /// Whether candidate membership currently observes committed Known Hosts.
  @visibleForTesting
  bool get isObservingCandidateKnownHosts =>
      _knownHostCandidateSubscription != null;

  /// Starts observing committed Known Hosts once their initial state has loaded.
  void _observeCandidateKnownHosts() {
    if (_isClosed || _knownHostCandidateSubscription != null) {
      return;
    }
    _knownHostCandidateSubscription = _clientStateService.knownHostsChanges
        .listen(
          (List<DovahLinkHost> hosts) {
            if (!_isClosed) {
              _reconcileCandidateHosts(hosts);
            }
          },
          onError: (Object error, StackTrace stackTrace) {
            if (!_isClosed && !_candidateHostsController.isClosed) {
              _candidateHostsController.addError(error, stackTrace);
            }
          },
        );
  }

  /// Reconciles the last discovery result with one authoritative Known Host snapshot.
  /// @param knownHosts The complete committed Known Host collection.
  void _reconcileCandidateHosts(List<DovahLinkHost> knownHosts) {
    final Set<DovahLinkHostId> knownHostIds = knownHosts
        .map((DovahLinkHost host) => DovahLinkHostId(host.hostId))
        .toSet();
    final Map<DovahLinkHostId, DovahLinkHost> candidates =
        <DovahLinkHostId, DovahLinkHost>{};
    for (final DovahLinkHost host in _discoveredHosts) {
      final DovahLinkHostId hostId = DovahLinkHostId(host.hostId);
      if (!knownHostIds.contains(hostId)) {
        candidates.putIfAbsent(hostId, () => host);
      }
    }
    final List<MapEntry<DovahLinkHostId, DovahLinkHost>> entries =
        candidates.entries.toList()
          ..sort((left, right) => left.key.value.compareTo(right.key.value));
    final List<DovahLinkHost> next = List<DovahLinkHost>.unmodifiable(
      entries.map(
        (MapEntry<DovahLinkHostId, DovahLinkHost> entry) => entry.value,
      ),
    );
    if (next.length == _candidateHosts.length &&
        next.indexed.every((entry) => entry.$2 == _candidateHosts[entry.$1])) {
      return;
    }
    _candidateHosts = next;
    _candidateHostsController.add(next);
  }

  /// Emits the complete Known Hosts view immediately on listen and after committed changes.
  /// A failed initial load is reported and the same subscription remains available for recovery
  /// after a later successful SDK state operation.
  /// @return A broadcast stream of immutable, Host-ID-sorted collections.
  Stream<List<DovahLinkHost>> get knownHostsChanges => hosts.knownHostsChanges;

  /// Emits complete runtime Known Host snapshots, replaying the current projection to each
  /// subscriber. Storage failures are reported while the listener remains available for recovery.
  /// @return An immutable, Host-ID-sorted projection of durable Known Hosts, runtime availability,
  /// and exact-relationship session state.
  Stream<List<DovahLinkKnownHostState>> get knownHostStatesChanges =>
      hosts.knownHostStatesChanges;

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

  /// Adds [area] to this client's local desired state-area set before synchronizing it with the
  /// current Host session. A synchronization failure does not necessarily roll back that intent;
  /// it may be synchronized on a later trusted session. [DovahLinkClient.disconnect] clears it.
  /// @param area The state domain to request.
  /// @return The areas the Host rejected from the resulting desired set.
  /// @throws [DovahLinkConnectionException] if no trusted session is active.
  /// @throws [DovahLinkProtocolException] if the Host returns a malformed acknowledgement.
  Future<Set<DovahLinkStateArea>> subscribeStateArea(DovahLinkStateArea area) =>
      _subscriptionService.subscribeStateArea(area);

  /// Removes [area] from this client's local desired state-area set before synchronizing that
  /// change with the current Host session. A synchronization failure does not necessarily roll
  /// back the removal; later trusted sessions will not restore [area].
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
  /// installation's [DovahLinkClient.clientId] on first use. It does not select a Known Host
  /// credential; use [DovahLinkClient.authenticateKnownHost] for that operation. Once the session
  /// is admitted, it retries orphaned requests whose trust requirements the new session satisfies.
  /// A trusted admission also starts restoring this client's desired state subscriptions.
  /// @return The current Host handshake and trust result.
  /// @throws [DovahLinkProtocolException] if the Host rejects authentication.
  /// @throws [DovahLinkHostIdentityMismatchException] if a trusted session or an outstanding
  ///     pairing recovery reports a different Host ID from the stored Known Host.
  /// @throws [DovahLinkCompatibilityException] if the Host version is outside the SDK's supported
  ///     range.
  /// @throws [DovahLinkConnectionException] if disconnect interrupts authentication.
  Future<HelloResult> hello() => _authenticationService.hello();

  /// Connects to an untrusted candidate without selecting Known Host credentials. Pair it through
  /// the SDK, or use [DovahLinkClient.authenticateKnownHost] for an existing relationship.
  /// @param uri The candidate endpoint to connect to.
  /// @return The Host identity claim and trust outcome reported by the candidate.
  /// @throws [DovahLinkConnectionException] if the socket cannot be established.
  /// @throws [DovahLinkProtocolException] if hello is rejected for a non-recoverable reason.
  /// @throws [DovahLinkHostIdentityMismatchException] if a trusted session or an outstanding
  ///     pairing recovery reports a different Host ID from the stored Known Host.
  /// @throws [DovahLinkCompatibilityException] if the Host version is outside the SDK's supported
  ///     range.
  Future<HelloResult> authenticateCandidate(Uri uri) =>
      _authenticationService.authenticateCandidate(uri);

  /// Authenticates a Known Host using its SDK-owned current endpoint and credential.
  /// @param hostId The stable identifier of the Host to authenticate.
  /// @throws [DovahLinkKnownHostNotFoundException] if [hostId] is not known.
  /// @throws [DovahLinkHostIdentityMismatchException] if the peer claim or pending recovery names
  ///     a different Host.
  /// @throws [DovahLinkConnectionException] if the socket cannot be established.
  /// @throws [DovahLinkProtocolException] if the Host rejects or malforms the handshake.
  /// @throws [DovahLinkCompatibilityException] if the Host version is unsupported.
  /// @return The result of the authenticated Known Host handshake.
  Future<HelloResult> authenticateKnownHost(DovahLinkHostId hostId) =>
      _authenticationService.authenticateKnownHost(hostId);

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

  /// Submits the six-digit code the user read from Skyrim. The SDK durably stores the issued
  /// credential with the current Host and its [PairingRecoveryState.confirming] recovery state
  /// before returning; the credential stays inside the SDK.
  /// @param code The six-digit code shown by Skyrim.
  /// @param displayName The optional Client display name for Host pairing metadata.
  /// @throws [DovahLinkPairingException] if the code was expired, invalid, paced too soon, or
  ///     hit the hard wrong-attempt limit.
  Future<void> confirmPairingCode({
    required String code,
    String? displayName,
  }) =>
      _pairingService.confirmPairingCode(code: code, displayName: displayName);

  /// Echoes the pending Host-scoped credential internally, completing pairing.
  /// [DovahLinkClient.trustState] becomes
  /// [DovahLinkTrustState.trusted] on success, and the persisted recovery state clears back to
  /// `null` while keeping the credential. Starts best-effort restoration of
  /// desired state-area subscriptions after pairing succeeds.
  /// @throws [DovahLinkPairingException] if the Host has no matching pending confirmation or
  ///     an administrative mutation invalidated the pending credential.
  Future<void> acknowledgeTrustedCredential() async {
    await _pairingService.acknowledgeTrustedCredential();
    _subscriptionService.restoreDesiredStateAreas();
  }

  /// Resumes an interrupted pairing confirmation after a crash or relaunch. Call after
  /// [DovahLinkClient.hello] admits a [DovahLinkTrustState.unpaired] session.
  ///
  /// A no-op returning [DovahLinkTrustState.unpaired] when no confirmation is outstanding. When
  /// one is, retries [DovahLinkClient.acknowledgeTrustedCredential] with the stored credential: a
  /// `pending_not_found` outcome (the Host restarted and lost the pending credential) or
  /// `pairing_invalidated` outcome (an administrative mutation rejected the pending credential)
  /// discards that Host's credential and recovery record and returns
  /// [DovahLinkTrustState.unpaired] rather than treating it as fatal; any other failure leaves
  /// [PairingRecoveryState.confirming]
  /// untouched so a later relaunch can retry. Invalidated confirmation preserves Known Host
  /// metadata. A recovered trusted session starts restoring desired state subscriptions.
  Future<DovahLinkTrustState> recoverPendingPairing() async {
    final DovahLinkTrustState trustState = await _pairingService
        .recoverPendingPairing();
    if (trustState == DovahLinkTrustState.trusted) {
      _subscriptionService.restoreDesiredStateAreas();
    }
    return trustState;
  }

  /// Closes the connection and resets in-memory session state. Idempotent, and never throws: this
  /// is a best-effort cleanup operation matching the transport's idempotent close contract.
  /// In-memory state resets even when the underlying transport cannot be closed
  /// cleanly -- a broken close must not leave [DovahLinkClient.connectionState],
  /// [DovahLinkClient.trustState], or [DovahLinkClient.sessionId] lying
  /// about a session that no longer exists. Persisted identity, credential, and recovery state are
  /// untouched -- trust survives a disconnect. Clears local desired subscription intent, then
  /// fails any operation still awaiting a reply and any operation an earlier transport loss
  /// orphaned for retry, instead of leaving it to hang
  /// forever: unlike an unexpected transport loss, a deliberate disconnect never retries. It also
  /// cancels in-flight authentication recovery and bounded automatic recovery from an earlier loss --
  /// [DovahLinkClient.connectionState] moves directly to
  /// [DovahLinkConnectionState.disconnected] rather than
  /// letting that recovery keep running. Repeated calls remain safe because transport close and
  /// pending-operation failure are idempotent; an administrative invalidation's typed reason is
  /// preserved, not reset to generic disconnect. A Known Host session deliberately disconnected
  /// by the client ends its session without clearing current reachability evidence.
  Future<void> disconnect() async {
    _authenticationService.cancelPendingAuthentication();
    _reconnectService.stopRecovery();
    _subscriptionService.clearDesiredStateAreas();
    await _sessionService.disconnect();
  }

  /// Permanently closes this client and releases its monitoring and stream resources.
  ///
  /// Unlike [disconnect], this terminal operation stops Known Host presence monitoring. The
  /// client must not be reused after [close] completes.
  /// @return A future completing after independent monitoring and session teardown, subscriptions,
  /// and client state streams have each finished best-effort cleanup.
  Future<void> close() => _closeFuture ??= (() async {
    _isClosed = true;
    final Future<void> monitorCleanup = Future<void>.sync(
      _knownHostPresenceMonitor.close,
    ).catchError((Object _, StackTrace __) {});
    final Future<void> sessionCleanup = Future<void>.sync(
      disconnect,
    ).catchError((Object _, StackTrace __) {});
    await Future.wait<void>(<Future<void>>[monitorCleanup, sessionCleanup]);
    await _knownHostSessionSubscription.cancel().catchError(
      (Object _, StackTrace __) {},
    );
    await _hostAvailabilityService.close().catchError(
      (Object _, StackTrace __) {},
    );
    await _knownHostCandidateSubscription?.cancel().catchError(
      (Object _, StackTrace __) {},
    );
    await _candidateHostsController.close().catchError(
      (Object _, StackTrace __) {},
    );
  })();

  /// Removes one Known Host's credential while preserving its metadata and the local client ID.
  /// @param hostId The stable identifier of the Host whose credential is removed.
  Future<void> forgetCredential(DovahLinkHostId hostId) =>
      _authenticationService.forgetCredential(hostId);
}

/// Creates a client with controllable infrastructure for SDK tests.
/// @param transport The fake or real transport used by this client.
/// @param storage The SDK-owned storage boundary for this client's identity and credential.
/// @param timeoutDurations The per-class request timeouts used by the client.
/// @param reconnectAttemptDelays The bounded reconnect attempt schedule.
/// @param reconnectDeadline The overall limit for one reconnect cycle.
/// @param now The clock used to measure the reconnect deadline.
/// @param hostPresenceProbe The probe used when monitoring is enabled.
/// @param discoveryService The discovery contract used by this composed client.
/// @param knownHostPresenceMonitoringEnabled Whether this test client starts the monitor.
/// @param hostPresenceRefreshInterval The injected Known Host refresh cadence.
/// @param hostPresenceRefreshTicks The injected deterministic refresh event stream.
/// @param hostPresenceMaxConcurrentProbes The injected global probe concurrency bound.
/// @return A client wired to the supplied transport and timing controls.
@visibleForTesting
DovahLinkClient buildDovahLinkClientForTesting({
  required IDovahLinkTransport transport,
  required IClientStorage storage,
  Map<TimeoutClass, Duration> timeoutDurations = kTimeoutClassDurations,
  List<Duration> reconnectAttemptDelays = kReconnectAttemptDelays,
  Duration reconnectDeadline = kReconnectDeadline,
  DateTime Function() now = DateTime.now,
  bool reconnectEnabled = true,
  IHostPresenceProbe? hostPresenceProbe,
  IDovahLinkDiscoveryService? discoveryService,
  bool knownHostPresenceMonitoringEnabled = false,
  Duration hostPresenceRefreshInterval = kKnownHostPresenceRefreshInterval,
  Stream<void>? hostPresenceRefreshTicks,
  int hostPresenceMaxConcurrentProbes = kKnownHostPresenceMaxConcurrentProbes,
}) => DovahLinkClient._build(
  transport: transport,
  storage: storage,
  timeoutDurations: timeoutDurations,
  hostPresenceProbe: hostPresenceProbe ?? HostPresenceProbe(),
  discoveryService: discoveryService,
  attemptDelays: reconnectAttemptDelays,
  reconnectDeadline: reconnectDeadline,
  reconnectNow: now,
  reconnectEnabled: reconnectEnabled,
  knownHostPresenceMonitoringEnabled: knownHostPresenceMonitoringEnabled,
  hostPresenceRefreshInterval: hostPresenceRefreshInterval,
  hostPresenceRefreshTicks: hostPresenceRefreshTicks,
  hostPresenceMaxConcurrentProbes: hostPresenceMaxConcurrentProbes,
);
