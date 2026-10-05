import 'dart:async';

import 'package:meta/meta.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_connections.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_current_host.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_discovery_service.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host_id.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_hosts.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_known_host_invalidation.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_pairing.dart';
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
import 'package:dovahlink_client_sdk/src/internal/state/character_state_module.dart';
import 'package:dovahlink_client_sdk/src/internal/state/game_time_state_module.dart';
import 'package:dovahlink_client_sdk/src/internal/state/player_location_state_module.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_domain_definition.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_message_handler.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_recovery_service.dart';
import 'package:dovahlink_client_sdk/src/internal/state/subscription_service.dart';
import 'package:dovahlink_client_sdk/src/internal/state/tracked_quests_state_module.dart';
import 'package:dovahlink_client_sdk/src/persistence/client_storage.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_known_host.dart';
import 'package:dovahlink_client_sdk/src/shared/constants.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/state/character_level_state.dart';
import 'package:dovahlink_client_sdk/src/transport/websocket_transport.dart';

/// A single Flutter/Redux-independent DovahLink client engine, exposed through grouped Host,
/// connection, pairing, and current-Host views. Owns its local [DovahLinkClient.clientId],
/// Host-scoped credentials, pairing recovery, and Known Hosts through its SDK-managed
/// [IClientStorage] boundary, so a consumer never threads credentials through this API.
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

  /// Signals the test builder when candidate reconciliation starts observing Known Hosts.
  final List<bool>? _candidateKnownHostsObservation;

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
    Duration initialConnectionRetryDelay = kInitialConnectionRetryDelay,
    Duration hostPresenceRefreshInterval = kKnownHostPresenceRefreshInterval,
    Stream<void>? hostPresenceRefreshTicks,
    int hostPresenceMaxConcurrentProbes = kKnownHostPresenceMaxConcurrentProbes,
    List<bool>? candidateKnownHostsObservation,
  }) : _clientStateService = ClientStateService(storage: storage),
       _candidateKnownHostsObservation = candidateKnownHostsObservation {
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
    final SessionState state = _sessionState = SessionState();
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

    final ICharacterStateModule characterState = CharacterStateModule();
    final IGameTimeStateModule gameTimeState = GameTimeStateModule();
    final IPlayerLocationStateModule playerLocationState =
        PlayerLocationStateModule();
    final ITrackedQuestsStateModule trackedQuestsState =
        TrackedQuestsStateModule();
    final IStateMessageHandler stateMessageHandler = StateMessageHandler(
      sessionService: _sessionService,
      domains: <IStateDomainDefinition<Object?>>[
        ...characterState.domains,
        playerLocationState.domain,
        gameTimeState.domain,
        trackedQuestsState.domain,
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
          domain: characterState.levelDomain,
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
    _knownHostInvalidationSubscription = state.knownHostInvalidations.listen(
      _handleKnownHostInvalidation,
    );
    _sessionService.onTeardown =
        (Exception reason, {required bool orphanRetrySafeOperations}) {
          _requestService.failAll(
            reason,
            orphanRetrySafeOperations: orphanRetrySafeOperations,
          );
          _subscriptionService.onSessionEnded();
        };
    _reconnectService = ReconnectService(
      sessionService: _sessionService,
      authenticationService: _authenticationService,
      hostAvailabilityService: _hostAvailabilityService,
      attemptDelays: attemptDelays,
      deadline: reconnectDeadline,
      now: reconnectNow,
      initialConnectionRetryDelay: initialConnectionRetryDelay,
    );
    _pairingService = PairingService(
      authenticationService: _authenticationService,
      reconnectService: _reconnectService,
      sessionService: _sessionService,
      sessionTrustService: sessionTrustService,
      requestService: _requestService,
      clientStateService: _clientStateService,
      hostAvailabilityService: _hostAvailabilityService,
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
    connections = DovahLinkConnections(
      sessionService: _sessionService,
      authenticationService: _authenticationService,
      reconnectService: _reconnectService,
      subscriptionService: _subscriptionService,
      requestService: _requestService,
    );
    currentHost = DovahLinkCurrentHost(
      sessionService: _sessionService,
      character: characterState.character,
      playerLocationChanges: playerLocationState.changes,
      gameTimeChanges: gameTimeState.changes,
      trackedQuestsChanges: trackedQuestsState.changes,
      subscriptionService: _subscriptionService,
    );
    pairing = DovahLinkPairing(
      discoverHosts: _discoverHosts,
      candidates: _candidateHostsChanges,
      pairingService: _pairingService,
      subscriptionService: _subscriptionService,
    );
  }

  /// Owns transport lifecycle, connection state, and stream ownership. This façade reads its
  /// state but leaves transitions to [SessionService]; it keeps the concrete type to assign the
  /// session's late-bound callbacks.
  late final SessionService _sessionService;

  /// Owns session state and immutable Known Host invalidation events.
  late final SessionState _sessionState;

  /// Owns the runtime availability map and complete Known Host projection.
  late final IHostAvailabilityService _hostAvailabilityService;

  /// Exposes this client's Known Host views over the existing state owners.
  late final IDovahLinkHosts hosts;

  /// Exposes connection operations over this client's existing session engine.
  late final IDovahLinkConnections connections;

  /// Exposes the current admitted Host context and existing typed state streams.
  late final IDovahLinkCurrentHost currentHost;

  /// Exposes SDK-owned candidate discovery and pairing operations.
  late final IDovahLinkPairing pairing;

  /// Mirrors the session owner's exact Known Host projection into complete Known Host snapshots.
  late final StreamSubscription<KnownHostSessionSnapshot>
  _knownHostSessionSubscription;

  /// Applies credential cleanup from the exact invalidated Host event.
  late final StreamSubscription<DovahLinkKnownHostInvalidation>
  _knownHostInvalidationSubscription;

  /// Tracks authoritative Known Host changes so newly known Hosts leave candidates immediately.
  StreamSubscription<List<PersistedKnownHost>>? _knownHostCandidateSubscription;

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

  /// This installation's stable client ID, or `null` before authentication resolves it.
  String? get clientId => _authenticationService.clientId;

  /// Discovers Hosts and reconciles their claims with this client's committed Known Hosts.
  ///
  /// Candidate state is runtime-only. The SDK reconciles it with committed Known Host state, and
  /// a later Known Host update removes a matching candidate without a consumer mutation.
  /// @return The complete immutable candidate collection, sorted by normalized Host ID.
  /// @throws DovahLinkStorageException if authoritative Known Host state cannot be read safely.
  Future<List<DovahLinkHost>> _discoverHosts() async {
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

  /// Provides the replaying candidate projection to the grouped pairing view.
  Stream<List<DovahLinkHost>> get _candidateHostsChanges =>
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

  /// Removes the credential using the Host ID and reason captured by the invalidation event.
  /// @param event The exact invalidated Host and Host-reported reason.
  void _handleKnownHostInvalidation(DovahLinkKnownHostInvalidation event) {
    unawaited(
      _authenticationService
          .forgetCredential(
            event.hostId,
            pairingRequired: switch (event.reason) {
              AdministrativeInvalidationReason.revoked ||
              AdministrativeInvalidationReason.trustReset ||
              AdministrativeInvalidationReason.factoryReset => true,
              AdministrativeInvalidationReason.blocked => false,
            },
          )
          .catchError((Object _, StackTrace __) {
            // Invalidation is terminal; a later authentication can retry cleanup.
          }),
    );
  }

  /// Starts observing committed Known Hosts once their initial state has loaded.
  void _observeCandidateKnownHosts() {
    if (_isClosed || _knownHostCandidateSubscription != null) {
      return;
    }
    _knownHostCandidateSubscription = _clientStateService.knownHostsChanges
        .listen(
          (List<PersistedKnownHost> relationships) {
            if (!_isClosed) {
              _reconcileCandidateHosts(
                relationships
                    .map((PersistedKnownHost relationship) => relationship.host)
                    .toList(growable: false),
              );
            }
          },
          onError: (Object error, StackTrace stackTrace) {
            if (!_isClosed && !_candidateHostsController.isClosed) {
              _candidateHostsController.addError(error, stackTrace);
            }
          },
        );
    _candidateKnownHostsObservation?.add(true);
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

  /// Permanently closes this client. This terminal, idempotent, best-effort operation stops
  /// Known Host presence monitoring, disconnects any active session through the SDK lifecycle,
  /// and cleans up SDK-owned subscriptions and resources. The client must not be reused after
  /// closing begins; connection and authentication operations then fail.
  /// @return A future completing after monitoring, session teardown, and resource cleanup finish.
  Future<void> close() => _closeFuture ??= (() async {
    _isClosed = true;
    final Future<void> monitorCleanup = Future<void>.sync(
      _knownHostPresenceMonitor.close,
    ).catchError((Object _, StackTrace __) {});
    final Future<void> sessionCleanup = Future<void>.sync(() {
      _authenticationService.cancelPendingAuthentication();
      _reconnectService.stopInitialConnectionRetry();
      _reconnectService.stopRecovery();
      _subscriptionService.clearDesiredStateAreas();
      return _sessionService.close();
    }).catchError((Object _, StackTrace __) {});
    await Future.wait<void>(<Future<void>>[monitorCleanup, sessionCleanup]);
    await _knownHostSessionSubscription.cancel().catchError(
      (Object _, StackTrace __) {},
    );
    await _knownHostInvalidationSubscription.cancel().catchError(
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
    await _sessionState.close().catchError((Object _, StackTrace __) {});
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
/// @param initialConnectionRetryDelay The wait between initial connection retries.
/// @param hostPresenceProbe The probe used when monitoring is enabled.
/// @param discoveryService The discovery contract used by this composed client.
/// @param knownHostPresenceMonitoringEnabled Whether this test client starts the monitor.
/// @param hostPresenceRefreshInterval The injected Known Host refresh cadence.
/// @param hostPresenceRefreshTicks The injected deterministic refresh event stream.
/// @param hostPresenceMaxConcurrentProbes The injected global probe concurrency bound.
/// @param candidateKnownHostsObservation Optional test records for candidate observation startup.
/// @return A client wired to the supplied transport and timing controls.
@visibleForTesting
DovahLinkClient buildDovahLinkClientForTesting({
  required IDovahLinkTransport transport,
  required IClientStorage storage,
  Map<TimeoutClass, Duration> timeoutDurations = kTimeoutClassDurations,
  List<Duration> reconnectAttemptDelays = kReconnectAttemptDelays,
  Duration reconnectDeadline = kReconnectDeadline,
  DateTime Function() now = DateTime.now,
  Duration initialConnectionRetryDelay = kInitialConnectionRetryDelay,
  bool reconnectEnabled = true,
  IHostPresenceProbe? hostPresenceProbe,
  IDovahLinkDiscoveryService? discoveryService,
  bool knownHostPresenceMonitoringEnabled = false,
  Duration hostPresenceRefreshInterval = kKnownHostPresenceRefreshInterval,
  Stream<void>? hostPresenceRefreshTicks,
  int hostPresenceMaxConcurrentProbes = kKnownHostPresenceMaxConcurrentProbes,
  List<bool>? candidateKnownHostsObservation,
}) => DovahLinkClient._build(
  transport: transport,
  storage: storage,
  timeoutDurations: timeoutDurations,
  hostPresenceProbe: hostPresenceProbe ?? HostPresenceProbe(),
  discoveryService: discoveryService,
  attemptDelays: reconnectAttemptDelays,
  reconnectDeadline: reconnectDeadline,
  reconnectNow: now,
  initialConnectionRetryDelay: initialConnectionRetryDelay,
  reconnectEnabled: reconnectEnabled,
  knownHostPresenceMonitoringEnabled: knownHostPresenceMonitoringEnabled,
  hostPresenceRefreshInterval: hostPresenceRefreshInterval,
  hostPresenceRefreshTicks: hostPresenceRefreshTicks,
  hostPresenceMaxConcurrentProbes: hostPresenceMaxConcurrentProbes,
  candidateKnownHostsObservation: candidateKnownHostsObservation,
);
