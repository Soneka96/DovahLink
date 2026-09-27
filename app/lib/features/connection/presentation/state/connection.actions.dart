import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';

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

/// Requests a fresh Host discovery operation.
class ConnectionDiscoveryRequestedAction extends Equatable {
  /// Creates a discovery request action.
  const ConnectionDiscoveryRequestedAction();

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

/// Carries the discovery error without processing its diagnostic text.
class ConnectionDiscoveryFailedAction extends Equatable {
  /// The SDK error raised during discovery.
  final Object error;

  /// Creates a discovery-failure action with [error].
  const ConnectionDiscoveryFailedAction(this.error);

  /// See [Equatable.props].
  @override
  List<Object?> get props => [error];
}
