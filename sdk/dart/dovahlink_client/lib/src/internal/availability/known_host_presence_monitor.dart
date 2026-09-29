import 'dart:async';
import 'dart:collection';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host_id.dart';
import 'package:dovahlink_client_sdk/src/host_presence_probe.dart';
import 'package:dovahlink_client_sdk/src/internal/availability/host_availability_service.dart';
import 'package:dovahlink_client_sdk/src/internal/persistence/client_state_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/shared/constants.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Defines the lifecycle of SDK-owned Known Host presence monitoring.
abstract interface class IKnownHostPresenceMonitor {
  /// Starts startup and periodic probes once.
  void start();

  /// Cancels scheduled work, in-flight probes, and the durable-state subscription.
  Future<void> close();
}

/// Probes durable Known Hosts without creating protocol sessions or selecting credentials.
class KnownHostPresenceMonitor implements IKnownHostPresenceMonitor {
  /// The committed Host collection this monitor follows.
  final IClientStateService _clientStateService;

  /// Performs bounded, sessionless metadata probes.
  final IHostPresenceProbe _probe;

  /// The single owner that publishes runtime reachability evidence.
  final IHostAvailabilityService _availabilityService;

  /// Provides the stronger authenticated-session evidence for a Known Host.
  final ISessionService _sessionService;

  /// The event source that drives periodic refreshes.
  final Stream<void> _refreshTicks;

  /// The maximum number of distinct Hosts probed concurrently.
  final int _maxConcurrentProbes;

  /// The current probe endpoints keyed by stable Host ID.
  final Map<String, Uri> _endpoints = <String, Uri>{};

  /// The latest collection generation for each Host.
  final Map<String, int> _generations = <String, int>{};

  /// Hosts queued once for a probe, in FIFO order.
  final Queue<String> _pendingHosts = Queue<String>();

  /// Host IDs currently in the pending queue.
  final Set<String> _pendingHostIds = <String>{};

  /// Cancellation signals for current per-Host probes.
  final Map<String, Completer<void>> _cancellations =
      <String, Completer<void>>{};

  /// Every active probe task, retained until cleanup completes.
  final Set<Future<void>> _workers = <Future<void>>{};

  /// The subscription to committed Known Host snapshots.
  StreamSubscription<List<DovahLinkHost>>? _knownHostsSubscription;

  /// The refresh tick subscription, once monitoring starts.
  StreamSubscription<void>? _refreshSubscription;

  /// Number of probes currently occupying the bounded worker pool.
  int _activeProbeCount = 0;

  /// Whether startup has already begun.
  bool _started = false;

  /// Whether terminal cleanup has begun.
  bool _isClosed = false;

  /// The shared completion of terminal cleanup.
  Future<void>? _closeFuture;

  /// Creates a monitor over the SDK-owned Host and session authorities.
  /// @param clientStateService Supplies committed Known Host snapshots.
  /// @param probe Performs sessionless reachability checks.
  /// @param availabilityService Publishes current presence evidence.
  /// @param sessionService reports the current authenticated Known Host session.
  /// @param refreshInterval The periodic presence refresh cadence.
  /// @param refreshTicks A deterministic refresh source, or the SDK's periodic source by default.
  /// @param maxConcurrentProbes The finite global probe concurrency bound.
  KnownHostPresenceMonitor({
    required IClientStateService clientStateService,
    required IHostPresenceProbe probe,
    required IHostAvailabilityService availabilityService,
    required ISessionService sessionService,
    Duration refreshInterval = kKnownHostPresenceRefreshInterval,
    Stream<void>? refreshTicks,
    int maxConcurrentProbes = kKnownHostPresenceMaxConcurrentProbes,
  }) : _clientStateService = clientStateService,
       _probe = probe,
       _availabilityService = availabilityService,
       _sessionService = sessionService,
       _refreshTicks =
           refreshTicks ?? Stream<void>.periodic(refreshInterval, (_) {}),
       _maxConcurrentProbes = maxConcurrentProbes {
    if (refreshInterval <= Duration.zero) {
      throw ArgumentError.value(refreshInterval, 'refreshInterval');
    }
    if (maxConcurrentProbes <= 0) {
      throw ArgumentError.value(maxConcurrentProbes, 'maxConcurrentProbes');
    }
  }

  /// Implements [IKnownHostPresenceMonitor.start].
  @override
  void start() {
    if (_started || _isClosed) {
      return;
    }
    _started = true;
    _knownHostsSubscription = _clientStateService.knownHostsChanges.listen(
      handleKnownHostsChanged,
      onError: (Object _, StackTrace __) {},
    );
    _refreshSubscription = _refreshTicks.listen((_) => refreshPresence());
  }

  /// Reconciles one committed Known Host collection and schedules immediate checks as needed.
  /// @param hosts The latest complete, durable Known Host snapshot.
  void handleKnownHostsChanged(List<DovahLinkHost> hosts) {
    if (!_started || _isClosed) {
      return;
    }
    final Set<String> hostIds = hosts
        .map((DovahLinkHost host) => host.hostId)
        .toSet();
    for (final String removedHostId
        in _endpoints.keys
            .where((String hostId) => !hostIds.contains(hostId))
            .toList(growable: false)) {
      _endpoints.remove(removedHostId);
      _generations[removedHostId] = (_generations[removedHostId] ?? 0) + 1;
      _pendingHosts.remove(removedHostId);
      _pendingHostIds.remove(removedHostId);
      cancelProbe(removedHostId);
      if (!_cancellations.containsKey(removedHostId)) {
        _generations.remove(removedHostId);
      }
    }

    for (final DovahLinkHost host in hosts) {
      final String hostId = host.hostId;
      final Uri? previousEndpoint = _endpoints[hostId];
      _endpoints[hostId] = host.endpoint;
      if (previousEndpoint == null || previousEndpoint != host.endpoint) {
        _generations[hostId] = (_generations[hostId] ?? 0) + 1;
        _pendingHosts.remove(hostId);
        _pendingHostIds.remove(hostId);
        cancelProbe(hostId);
        scheduleProbe(
          hostId,
          showChecking: true,
          queueBehindCurrentProbe: true,
        );
      }
    }
    pumpQueue();
  }

  /// Schedules a bounded refresh for every current Host without overlapping its own probe.
  void refreshPresence() {
    if (!_started || _isClosed) {
      return;
    }
    for (final String hostId in _endpoints.keys) {
      scheduleProbe(hostId, showChecking: true);
    }
    pumpQueue();
  }

  /// Adds one Host to the FIFO worker queue unless it is already queued or being probed.
  /// @param hostId The Host whose endpoint should be checked.
  /// @param showChecking Whether to publish the transient checking state before the request.
  /// @param queueBehindCurrentProbe Whether a fresh endpoint should wait for an older probe to end.
  void scheduleProbe(
    String hostId, {
    required bool showChecking,
    bool queueBehindCurrentProbe = false,
  }) {
    if (_isClosed) {
      return;
    }
    if (!_endpoints.containsKey(hostId)) {
      return;
    }
    if (hasAuthenticatedSession(hostId)) {
      _availabilityService.setAvailability(
        DovahLinkHostId(hostId),
        DovahLinkHostAvailability.online,
      );
      return;
    }
    if (_pendingHostIds.contains(hostId)) {
      return;
    }
    if (_cancellations.containsKey(hostId) && !queueBehindCurrentProbe) {
      return;
    }
    if (showChecking) {
      _availabilityService.setAvailability(
        DovahLinkHostId(hostId),
        DovahLinkHostAvailability.checking,
      );
    }
    _pendingHosts.addLast(hostId);
    _pendingHostIds.add(hostId);
  }

  /// Starts FIFO work while bounded capacity is available.
  void pumpQueue() {
    int queuedAtStart = _pendingHosts.length;
    while (!_isClosed &&
        _activeProbeCount < _maxConcurrentProbes &&
        _pendingHosts.isNotEmpty &&
        queuedAtStart > 0) {
      queuedAtStart--;
      final String hostId = _pendingHosts.removeFirst();
      _pendingHostIds.remove(hostId);
      if (_cancellations.containsKey(hostId)) {
        _pendingHosts.addLast(hostId);
        _pendingHostIds.add(hostId);
        continue;
      }
      final Uri? endpoint = _endpoints[hostId];
      if (endpoint == null) {
        continue;
      }
      if (hasAuthenticatedSession(hostId)) {
        _availabilityService.setAvailability(
          DovahLinkHostId(hostId),
          DovahLinkHostAvailability.online,
        );
        continue;
      }
      final int generation = _generations[hostId] ?? 0;
      final Completer<void> cancellation = Completer<void>();
      _cancellations[hostId] = cancellation;
      _activeProbeCount++;
      final Future<void> worker = probeKnownHost(
        hostId,
        endpoint,
        generation,
        cancellation,
      );
      _workers.add(worker);
      unawaited(worker.whenComplete(() => _workers.remove(worker)));
    }
  }

  /// Performs one probe and publishes only evidence for the current Host endpoint generation.
  /// @param hostId The stable Host identity this probe was started for.
  /// @param endpoint The endpoint this probe was started for.
  /// @param generation The snapshot generation captured at start.
  /// @param cancellation The signal used to abort this probe when its Host changes or closes.
  Future<void> probeKnownHost(
    String hostId,
    Uri endpoint,
    int generation,
    Completer<void> cancellation,
  ) async {
    try {
      final DovahLinkHost claim = await _probe.probe(
        endpoint,
        cancel: cancellation.future,
      );
      if (!isCurrentProbe(hostId, endpoint, generation, cancellation)) {
        return;
      }
      _availabilityService.setAvailability(
        DovahLinkHostId(hostId),
        hasAuthenticatedSession(hostId)
            ? DovahLinkHostAvailability.online
            : claim.hostId == hostId
            ? DovahLinkHostAvailability.online
            : DovahLinkHostAvailability.unknown,
      );
    } on DovahLinkConnectionException catch (error) {
      if (!isCurrentProbe(hostId, endpoint, generation, cancellation)) {
        return;
      }
      _availabilityService.setAvailability(
        DovahLinkHostId(hostId),
        hasAuthenticatedSession(hostId)
            ? DovahLinkHostAvailability.online
            : error.httpStatusCode == null
            ? DovahLinkHostAvailability.offline
            : DovahLinkHostAvailability.unknown,
      );
    } on Object {
      if (isCurrentProbe(hostId, endpoint, generation, cancellation) &&
          !hasAuthenticatedSession(hostId)) {
        _availabilityService.setAvailability(
          DovahLinkHostId(hostId),
          DovahLinkHostAvailability.unknown,
        );
      }
    } finally {
      if (identical(_cancellations[hostId], cancellation)) {
        _cancellations.remove(hostId);
      }
      if (!_endpoints.containsKey(hostId) &&
          !_cancellations.containsKey(hostId)) {
        _generations.remove(hostId);
      }
      _activeProbeCount--;
      pumpQueue();
    }
  }

  /// Implements [IKnownHostPresenceMonitor.close].
  @override
  Future<void> close() {
    final Future<void>? closing = _closeFuture;
    if (closing != null) {
      return closing;
    }
    _isClosed = true;
    final StreamSubscription<void>? refreshSubscription = _refreshSubscription;
    _refreshSubscription = null;
    final StreamSubscription<List<DovahLinkHost>>? subscription =
        _knownHostsSubscription;
    _knownHostsSubscription = null;
    for (final Completer<void> cancellation in _cancellations.values) {
      if (!cancellation.isCompleted) {
        cancellation.complete();
      }
    }
    _pendingHosts.clear();
    _pendingHostIds.clear();
    final List<Future<void>> subscriptionsClosed = <Future<void>>[
      if (subscription != null) subscription.cancel(),
      if (refreshSubscription != null) refreshSubscription.cancel(),
    ];
    _closeFuture = Future.wait<void>(subscriptionsClosed).then((_) async {
      await Future.wait<void>(_workers.toList(growable: false));
    });
    return _closeFuture!;
  }

  /// Reports whether a trusted session is currently admitted for this exact Known Host.
  /// @param hostId The stable Host identity whose session evidence is checked.
  /// @return Whether the current session is trusted and associated with [hostId].
  bool hasAuthenticatedSession(String hostId) =>
      _sessionService.connectionState == DovahLinkConnectionState.connected &&
      _sessionService.currentTrustState == DovahLinkTrustState.trusted &&
      _sessionService.currentKnownHostId == DovahLinkHostId(hostId);

  /// Reports whether a probe still targets its current Host endpoint generation.
  /// @param hostId The stable Host identity the probe used.
  /// @param endpoint The endpoint the probe used.
  /// @param generation The generation captured at probe start.
  /// @param cancellation The probe's own active cancellation signal.
  /// @return Whether the result is still allowed to update availability.
  bool isCurrentProbe(
    String hostId,
    Uri endpoint,
    int generation,
    Completer<void> cancellation,
  ) =>
      !_isClosed &&
      !cancellation.isCompleted &&
      identical(_cancellations[hostId], cancellation) &&
      _generations[hostId] == generation &&
      _endpoints[hostId] == endpoint;

  /// Cancels the active probe for [hostId] when it has one.
  /// @param hostId The Host whose probe should stop.
  void cancelProbe(String hostId) {
    final Completer<void>? cancellation = _cancellations[hostId];
    if (cancellation != null && !cancellation.isCompleted) {
      cancellation.complete();
    }
  }
}
