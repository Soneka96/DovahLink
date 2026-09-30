import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_known_host_state.dart';
import 'package:dovahlink_client_sdk/src/internal/availability/host_availability_service.dart';
import 'package:dovahlink_client_sdk/src/internal/persistence/client_state_service.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_known_host.dart';

/// Exposes the client's durable Known Hosts and their runtime projection.
abstract interface class IDovahLinkHosts {
  /// Loads the complete immutable Known Host metadata collection.
  /// @return Hosts ordered by stable Host ID, without credentials.
  Future<List<DovahLinkHost>> loadKnownHosts();

  /// Emits complete durable Known Host metadata snapshots and storage errors.
  Stream<List<DovahLinkHost>> get knownHostsChanges;

  /// Emits complete Known Host metadata, runtime availability, and session snapshots.
  Stream<List<DovahLinkKnownHostState>> get knownHostStatesChanges;
}

/// Implements [IDovahLinkHosts] over the existing client state owners.
class DovahLinkHosts implements IDovahLinkHosts {
  /// Owns durable Known Host metadata.
  final IClientStateService _clientStateService;

  /// Owns the runtime Known Host projection.
  final IHostAvailabilityService _hostAvailabilityService;

  /// Creates the Known Host view over the client's existing state owners.
  /// @param clientStateService The single owner of persisted client state.
  /// @param hostAvailabilityService The owner of runtime Known Host projection.
  DovahLinkHosts({
    required IClientStateService clientStateService,
    required IHostAvailabilityService hostAvailabilityService,
  }) : _clientStateService = clientStateService,
       _hostAvailabilityService = hostAvailabilityService;

  /// Implements [IDovahLinkHosts.loadKnownHosts].
  @override
  Future<List<DovahLinkHost>> loadKnownHosts() async {
    final PersistedClientState state = await _clientStateService.load();
    final List<DovahLinkHost> hosts =
        state.knownHosts.values
            .map((PersistedKnownHost relationship) => relationship.host)
            .toList()
          ..sort((left, right) => left.hostId.compareTo(right.hostId));
    return List<DovahLinkHost>.unmodifiable(hosts);
  }

  /// Implements [IDovahLinkHosts.knownHostsChanges].
  @override
  Stream<List<DovahLinkHost>> get knownHostsChanges =>
      _clientStateService.knownHostsChanges;

  /// Implements [IDovahLinkHosts.knownHostStatesChanges].
  @override
  Stream<List<DovahLinkKnownHostState>> get knownHostStatesChanges =>
      _hostAvailabilityService.knownHostStatesChanges;
}
