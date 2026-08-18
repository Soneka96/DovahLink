import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/features/connection/domain/entities/bridge.entity.dart';
import 'package:dovahlink_client/features/connection/domain/entities/connection_session.entity.dart';

/// Requests a new bridge connection.
class ConnectionStartedAction extends Equatable {
  /// Creates a connection-start request.
  const ConnectionStartedAction();

  /// See [Equatable.props].
  @override
  List<Object?> get props => [];
}

/// Marks protocol negotiation as active.
class ConnectionNegotiatingAction extends Equatable {
  /// Creates a negotiation-state action.
  const ConnectionNegotiatingAction();

  /// See [Equatable.props].
  @override
  List<Object?> get props => [];
}

/// Carries an accepted bridge session.
class ConnectionEstablishedAction extends Equatable {
  /// Creates a connection-established action.
  const ConnectionEstablishedAction(this.session);

  /// The newly accepted session identity.
  final ConnectionSessionEntity session;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [session];
}

/// Marks the active connection as rebuilding its state.
class ConnectionRecoveringAction extends Equatable {
  /// Creates a recovery-state action.
  const ConnectionRecoveringAction();

  /// See [Equatable.props].
  @override
  List<Object?> get props => [];
}

/// Carries a retryable unavailable-connection message.
class ConnectionUnavailableAction extends Equatable {
  /// Creates an unavailable-state action.
  const ConnectionUnavailableAction(this.message);

  /// A user-safe explanation of why the bridge is unavailable.
  final String message;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [message];
}

/// Carries a protocol incompatibility message.
class ConnectionIncompatibleAction extends Equatable {
  /// Creates an incompatible-state action.
  const ConnectionIncompatibleAction(this.message);

  /// A user-safe explanation of the version mismatch.
  final String message;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [message];
}

/// Requests navigating to pairing for the selected Bridge.
class ConnectionBridgeSelectedAction extends Equatable {
  /// Creates a Bridge-selection action.
  const ConnectionBridgeSelectedAction(this.bridge);

  /// The Bridge the user selected.
  final BridgeEntity bridge;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [bridge];
}

/// Clears the active connection and session.
class ConnectionDisconnectedAction extends Equatable {
  /// Creates a disconnected-state action.
  const ConnectionDisconnectedAction();

  /// See [Equatable.props].
  @override
  List<Object?> get props => [];
}
