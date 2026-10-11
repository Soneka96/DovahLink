import 'dart:async';

import 'package:dovahlink_client_sdk/src/dovahlink_discovery_service.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host_id.dart';
import 'package:dovahlink_client_sdk/src/internal/persistence/client_state_service.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_known_host.dart';

/// Owns the runtime candidate projection: discovered Hosts reconciled against the committed Known
/// Host collection, per `ai/context/sdk/architecture.md`'s "Local Host discovery and presence".
abstract interface class IHostCandidateService {
  /// The current runtime-only candidate collection, replayed immediately to each subscriber.
  Stream<List<DovahLinkHost>> get candidates;

  /// Discovers local Hosts and reconciles their claims with the committed Known Hosts by
  /// normalized Host ID. Candidate state is runtime-only; a later Known Host update removes a
  /// matching candidate without a consumer mutation.
  /// @return The complete immutable candidate collection, sorted by normalized Host ID.
  /// @throws DovahLinkStorageException if authoritative Known Host state cannot be read safely.
  Future<List<DovahLinkHost>> discoverHosts();

  /// Permanently stops discovery and observation and closes [candidates]. Idempotent.
  Future<void> close();
}

/// Implements [IHostCandidateService] over the shared Known Host owner and discovery contract.
/// Every collaborator is supplied by the caller per `ai/context/sdk/architecture.md`'s
/// "Dependency injection".
class HostCandidateService implements IHostCandidateService {
  /// The sole owner of committed Known Hosts.
  final IClientStateService _clientStateService;

  /// Performs the sessionless local Host discovery.
  final IDovahLinkDiscoveryService _discoveryService;

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

  /// Tracks authoritative Known Host changes so newly known Hosts leave candidates immediately.
  StreamSubscription<List<PersistedKnownHost>>? _knownHostCandidateSubscription;

  /// Creates a candidate service over [clientStateService] and [discoveryService].
  /// @param clientStateService Supplies and announces the committed Known Hosts.
  /// @param discoveryService Discovers the sessionless Host claims.
  HostCandidateService({
    required IClientStateService clientStateService,
    required IDovahLinkDiscoveryService discoveryService,
  }) : _clientStateService = clientStateService,
       _discoveryService = discoveryService;

  /// Implements [IHostCandidateService.candidates].
  @override
  Stream<List<DovahLinkHost>> get candidates =>
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

  /// Implements [IHostCandidateService.discoverHosts].
  @override
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
      _observeKnownHosts();
      rethrow;
    }
    if (_isClosed) {
      return _candidateHosts;
    }
    _observeKnownHosts();
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
      _reconcile(
        state.knownHosts.values
            .map((PersistedKnownHost relationship) => relationship.host)
            .toList(growable: false),
      );
    }
    return _candidateHosts;
  }

  /// Implements [IHostCandidateService.close].
  @override
  Future<void> close() async {
    _isClosed = true;
    await _knownHostCandidateSubscription?.cancel().catchError(
      (Object _, StackTrace __) {},
    );
    if (!_candidateHostsController.isClosed) {
      await _candidateHostsController.close().catchError(
        (Object _, StackTrace __) {},
      );
    }
  }

  /// Starts observing committed Known Hosts once their initial state has loaded.
  void _observeKnownHosts() {
    if (_isClosed || _knownHostCandidateSubscription != null) {
      return;
    }
    _knownHostCandidateSubscription = _clientStateService.knownHostsChanges
        .listen(
          (List<PersistedKnownHost> relationships) {
            if (!_isClosed) {
              _reconcile(
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
  }

  /// Reconciles the last discovery result with one authoritative Known Host snapshot.
  /// @param knownHosts The complete committed Known Host collection.
  void _reconcile(List<DovahLinkHost> knownHosts) {
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
}
