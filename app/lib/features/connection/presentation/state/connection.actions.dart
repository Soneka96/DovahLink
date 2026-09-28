import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// Records the Host the user selected to pair or connect with.
class ConnectionHostSelectedAction extends Equatable {
  /// The Host the user selected.
  final Host host;

  /// Creates a Host-selection action.
  const ConnectionHostSelectedAction(this.host);

  /// See [Equatable.props].
  @override
  List<Object?> get props => [host];
}

/// Carries the complete SDK-owned Known Hosts projection into Redux.
class ConnectionKnownHostsChangedAction extends Equatable {
  /// The complete app-mapped Known Hosts collection.
  final List<Host> knownHosts;

  /// Creates an immutable Known Hosts observation action.
  /// @param knownHosts The complete SDK-reported collection after app-boundary mapping.
  ConnectionKnownHostsChangedAction(List<Host> knownHosts)
    : knownHosts = List<Host>.unmodifiable(knownHosts);

  /// See [Equatable.props].
  @override
  List<Object?> get props => [knownHosts];
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

/// Carries every Host candidate returned by discovery.
class ConnectionDiscoverySucceededAction extends Equatable {
  /// The candidates returned by discovery.
  final List<Host> hosts;

  /// Creates a discovery-success action with [hosts].
  const ConnectionDiscoverySucceededAction(this.hosts);

  /// See [Equatable.props].
  @override
  List<Object?> get props => [hosts];
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
