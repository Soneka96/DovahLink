import 'package:fpdart/fpdart.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// Reduces connection actions into [ConnectionState].
Reducer<ConnectionState> connectionReducer = combineReducers<ConnectionState>([
  /// Handles [ConnectionStartedAction].
  /// Updates [ConnectionState.phase], [ConnectionState.session], [ConnectionState.error].
  TypedReducer<ConnectionState, ConnectionStartedAction>(
    connectionStartedReducer,
  ).call,

  /// Handles [ConnectionNegotiatingAction].
  /// Updates [ConnectionState.phase], [ConnectionState.error].
  TypedReducer<ConnectionState, ConnectionNegotiatingAction>(
    connectionNegotiatingReducer,
  ).call,

  /// Handles [ConnectionEstablishedAction].
  /// Updates [ConnectionState.phase], [ConnectionState.session], [ConnectionState.error].
  TypedReducer<ConnectionState, ConnectionEstablishedAction>(
    connectionEstablishedReducer,
  ).call,

  /// Handles [ConnectionRecoveringAction].
  /// Updates [ConnectionState.phase], [ConnectionState.error].
  TypedReducer<ConnectionState, ConnectionRecoveringAction>(
    connectionRecoveringReducer,
  ).call,

  /// Handles [ConnectionUnavailableAction].
  /// Updates [ConnectionState.phase], [ConnectionState.error].
  TypedReducer<ConnectionState, ConnectionUnavailableAction>(
    connectionUnavailableReducer,
  ).call,

  /// Handles [ConnectionIncompatibleAction].
  /// Updates [ConnectionState.phase], [ConnectionState.error].
  TypedReducer<ConnectionState, ConnectionIncompatibleAction>(
    connectionIncompatibleReducer,
  ).call,

  /// Handles [ConnectionDisconnectedAction].
  /// Updates [ConnectionState.phase], [ConnectionState.session], [ConnectionState.error].
  TypedReducer<ConnectionState, ConnectionDisconnectedAction>(
    connectionDisconnectedReducer,
  ).call,
]);

/// Handles [ConnectionStartedAction].
/// Updates [ConnectionState.phase], [ConnectionState.session], [ConnectionState.error].
ConnectionState connectionStartedReducer(
  ConnectionState state,
  ConnectionStartedAction action,
) => state.copyWith(
  phase: ConnectionPhase.connecting,
  session: const None(),
  error: const None(),
);

/// Handles [ConnectionNegotiatingAction].
/// Updates [ConnectionState.phase], [ConnectionState.error].
ConnectionState connectionNegotiatingReducer(
  ConnectionState state,
  ConnectionNegotiatingAction action,
) => state.copyWith(phase: ConnectionPhase.negotiating, error: const None());

/// Handles [ConnectionEstablishedAction].
/// Updates [ConnectionState.phase], [ConnectionState.session], [ConnectionState.error].
ConnectionState connectionEstablishedReducer(
  ConnectionState state,
  ConnectionEstablishedAction action,
) => state.copyWith(
  phase: ConnectionPhase.connected,
  session: Some(action.session),
  error: const None(),
);

/// Handles [ConnectionRecoveringAction].
/// Updates [ConnectionState.phase], [ConnectionState.error].
ConnectionState connectionRecoveringReducer(
  ConnectionState state,
  ConnectionRecoveringAction action,
) => state.copyWith(phase: ConnectionPhase.recovering, error: const None());

/// Handles [ConnectionUnavailableAction].
/// Updates [ConnectionState.phase], [ConnectionState.error].
ConnectionState connectionUnavailableReducer(
  ConnectionState state,
  ConnectionUnavailableAction action,
) => state.copyWith(
  phase: ConnectionPhase.unavailable,
  error: Some(action.message),
);

/// Handles [ConnectionIncompatibleAction].
/// Updates [ConnectionState.phase], [ConnectionState.error].
ConnectionState connectionIncompatibleReducer(
  ConnectionState state,
  ConnectionIncompatibleAction action,
) => state.copyWith(
  phase: ConnectionPhase.incompatible,
  error: Some(action.message),
);

/// Handles [ConnectionDisconnectedAction].
/// Updates [ConnectionState.phase], [ConnectionState.session], [ConnectionState.error].
ConnectionState connectionDisconnectedReducer(
  ConnectionState state,
  ConnectionDisconnectedAction action,
) => state.copyWith(
  phase: ConnectionPhase.disconnected,
  session: const None(),
  error: const None(),
);
