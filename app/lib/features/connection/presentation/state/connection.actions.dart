import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/features/connection/domain/entities/connection_session.entity.dart';

/// Requests a new bridge connection.
class ConnectionStartedAction extends Equatable {
  /// Creates a connection-start request.
  const ConnectionStartedAction();

  @override
  List<Object?> get props => [];
}

/// Marks protocol negotiation as active.
class ConnectionNegotiatingAction extends Equatable {
  /// Creates a negotiation-state action.
  const ConnectionNegotiatingAction();

  @override
  List<Object?> get props => [];
}

/// Carries an accepted bridge session.
class ConnectionEstablishedAction extends Equatable {
  /// Creates a connection-established action.
  const ConnectionEstablishedAction(this.session);

  /// The newly accepted session identity.
  final ConnectionSessionEntity session;

  @override
  List<Object?> get props => [session];
}

/// Marks the active connection as rebuilding its state.
class ConnectionRecoveringAction extends Equatable {
  /// Creates a recovery-state action.
  const ConnectionRecoveringAction();

  @override
  List<Object?> get props => [];
}

/// Carries a retryable unavailable-connection message.
class ConnectionUnavailableAction extends Equatable {
  /// Creates an unavailable-state action.
  const ConnectionUnavailableAction(this.message);

  /// A user-safe explanation of why the bridge is unavailable.
  final String message;

  @override
  List<Object?> get props => [message];
}

/// Carries a protocol incompatibility message.
class ConnectionIncompatibleAction extends Equatable {
  /// Creates an incompatible-state action.
  const ConnectionIncompatibleAction(this.message);

  /// A user-safe explanation of the version mismatch.
  final String message;

  @override
  List<Object?> get props => [message];
}

/// Clears the active connection and session.
class ConnectionDisconnectedAction extends Equatable {
  /// Creates a disconnected-state action.
  const ConnectionDisconnectedAction();

  @override
  List<Object?> get props => [];
}
