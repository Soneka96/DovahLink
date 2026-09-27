import 'package:fpdart/fpdart.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// Reduces connection actions into [ConnectionState].
Reducer<ConnectionState> connectionReducer = combineReducers<ConnectionState>([
  TypedReducer<ConnectionState, ConnectionHostSelectedAction>(
    connectionHostSelectedReducer,
  ).call,
  TypedReducer<ConnectionState, ConnectionDiscoveryRequestedAction>(
    connectionDiscoveryRequestedReducer,
  ).call,
  TypedReducer<ConnectionState, ConnectionDiscoverySucceededAction>(
    connectionDiscoverySucceededReducer,
  ).call,
  TypedReducer<ConnectionState, ConnectionDiscoveryFailedAction>(
    connectionDiscoveryFailedReducer,
  ).call,
]);

/// Handles [ConnectionHostSelectedAction].
/// Updates [ConnectionState.selectedHost] to the Host the user selected, replacing any earlier
/// selection.
ConnectionState connectionHostSelectedReducer(
  ConnectionState state,
  ConnectionHostSelectedAction action,
) => state.copyWith(selectedHost: Some(action.host));

/// Handles [ConnectionDiscoveryRequestedAction].
/// Clears prior candidates and records that discovery is in progress.
ConnectionState connectionDiscoveryRequestedReducer(
  ConnectionState state,
  ConnectionDiscoveryRequestedAction action,
) => state.copyWith(
  hosts: const <Host>[],
  discoveryStatus: ConnectionDiscoveryStatus.discovering,
  discoveryFailure: const None(),
);

/// Handles [ConnectionDiscoverySucceededAction].
/// Stores all candidates and distinguishes available from empty results.
ConnectionState connectionDiscoverySucceededReducer(
  ConnectionState state,
  ConnectionDiscoverySucceededAction action,
) => state.copyWith(
  hosts: action.hosts,
  discoveryStatus: action.hosts.isEmpty
      ? ConnectionDiscoveryStatus.empty
      : ConnectionDiscoveryStatus.available,
  discoveryFailure: const None(),
);

/// Handles [ConnectionDiscoveryFailedAction].
/// Clears candidates and preserves the semantic failure reason for presentation.
ConnectionState connectionDiscoveryFailedReducer(
  ConnectionState state,
  ConnectionDiscoveryFailedAction action,
) => state.copyWith(
  hosts: const <Host>[],
  discoveryStatus: ConnectionDiscoveryStatus.failed,
  discoveryFailure: Some(action.failure),
);
