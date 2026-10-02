import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/domain/entities/known_host.entity.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// Records the Host the user selected to pair or connect with.
class ConnectionHostSelectedAction extends Equatable {
  /// The Host the user selected.
  final Host host;

  /// Whether this selection is an ephemeral candidate or a Known Host relationship.
  final ConnectionHostSelectionSource source;

  /// Creates a Host-selection action. Omitted [source] means a discovered candidate.
  const ConnectionHostSelectedAction(
    this.host, {
    this.source = ConnectionHostSelectionSource.candidate,
  });

  /// See [Equatable.props].
  @override
  List<Object?> get props => [host, source];
}

/// Retains a candidate selection while its pairing confirmation is pending.
class ConnectionCandidatePairingStartedAction extends Equatable {
  /// The candidate whose durable Known Host projection is expected from the SDK.
  final String hostId;

  /// Creates a pending candidate-pairing selection action.
  /// @param hostId The selected candidate's stable Host ID.
  const ConnectionCandidatePairingStartedAction(this.hostId);

  /// See [Equatable.props].
  @override
  List<Object?> get props => [hostId];
}

/// Releases a candidate selection retained while an untrusted pairing flow is active.
class ConnectionCandidatePairingEndedAction extends Equatable {
  /// The candidate whose pairing confirmation ended.
  final String hostId;

  /// Creates an ended candidate-pairing action.
  /// @param hostId The candidate whose pending selection should be released.
  const ConnectionCandidatePairingEndedAction(this.hostId);

  /// See [Equatable.props].
  @override
  List<Object?> get props => [hostId];
}

/// Carries the complete SDK-owned Known Hosts projection into Redux.
class ConnectionKnownHostsChangedAction extends Equatable {
  /// The complete app-mapped Known Hosts collection.
  final List<KnownHost> knownHosts;

  /// Creates an immutable Known Hosts observation action.
  /// @param knownHosts The complete SDK-reported collection after app-boundary mapping.
  ConnectionKnownHostsChangedAction(List<KnownHost> knownHosts)
    : knownHosts = List<KnownHost>.unmodifiable(knownHosts);

  /// See [Equatable.props].
  @override
  List<Object?> get props => [knownHosts];
}

/// Reports that the SDK Known Hosts stream failed to provide its latest observation.
class ConnectionKnownHostsObservationFailedAction extends Equatable {
  /// Creates a semantic Known Hosts observation failure action.
  const ConnectionKnownHostsObservationFailedAction();

  /// See [Equatable.props].
  @override
  List<Object?> get props => [];
}

/// Carries the complete candidate collection reported by the SDK.
class ConnectionCandidatesChangedAction extends Equatable {
  /// The complete app-mapped candidate collection.
  final List<Host> hosts;

  /// Creates an immutable candidate projection action.
  /// @param hosts The complete SDK-reported candidate collection.
  ConnectionCandidatesChangedAction(List<Host> hosts)
    : hosts = List<Host>.unmodifiable(hosts);

  /// See [Equatable.props].
  @override
  List<Object?> get props => [hosts];
}

/// Requests a fresh Host discovery operation.
class ConnectionDiscoveryRequestedAction extends Equatable {
  /// Creates a discovery request action.
  const ConnectionDiscoveryRequestedAction();

  /// See [Equatable.props].
  @override
  List<Object?> get props => [];
}

/// Marks an accepted Host discovery operation as started.
class ConnectionDiscoveryStartedAction extends Equatable {
  /// Creates a discovery-started action.
  const ConnectionDiscoveryStartedAction();

  /// See [Equatable.props].
  @override
  List<Object?> get props => [];
}

/// Reports a successful discovery operation without carrying candidate membership.
class ConnectionDiscoverySucceededAction extends Equatable {
  /// Whether the completed discovery result contained any SDK candidate.
  final bool hasCandidates;

  /// Creates a discovery-success action with its empty-results status.
  /// @param hasCandidates Whether the completed SDK discovery result had candidates.
  const ConnectionDiscoverySucceededAction({required this.hasCandidates});

  /// See [Equatable.props].
  @override
  List<Object?> get props => [hasCandidates];
}

/// Carries the app-owned meaning of a discovery failure.
class ConnectionDiscoveryFailedAction extends Equatable {
  /// The semantic reason discovery failed.
  final ConnectionFailureReason failure;

  /// Creates a discovery-failure action with [failure].
  const ConnectionDiscoveryFailedAction(this.failure);

  /// See [Equatable.props].
  @override
  List<Object?> get props => [failure];
}

/// Requests re-entry to a Known Host's already-admitted session.
class ConnectionHostReentryRequestedAction extends Equatable {
  /// The stable identity of the Host whose connected session should open.
  final String hostId;

  /// Creates a re-entry request for [hostId].
  /// @param hostId The stable Host ID shown by the Connected card.
  const ConnectionHostReentryRequestedAction(this.hostId);

  /// See [Equatable.props].
  @override
  List<Object?> get props => [hostId];
}
