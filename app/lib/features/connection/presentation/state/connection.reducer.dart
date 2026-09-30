import 'package:fpdart/fpdart.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/domain/entities/known_host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// Reduces connection actions into [ConnectionState].
Reducer<ConnectionState> connectionReducer = combineReducers<ConnectionState>([
  TypedReducer<ConnectionState, ConnectionHostSelectedAction>(
    connectionHostSelectedReducer,
  ).call,
  TypedReducer<ConnectionState, ConnectionKnownHostsChangedAction>(
    connectionKnownHostChangedReducer,
  ).call,
  TypedReducer<ConnectionState, ConnectionCandidatesChangedAction>(
    connectionCandidatesChangedReducer,
  ).call,
  TypedReducer<ConnectionState, ConnectionKnownHostsObservationFailedAction>(
    connectionKnownHostsObservationFailedReducer,
  ).call,
  TypedReducer<ConnectionState, ConnectionDiscoveryStartedAction>(
    connectionDiscoveryStartedReducer,
  ).call,
  TypedReducer<ConnectionState, ConnectionDiscoverySucceededAction>(
    connectionDiscoverySucceededReducer,
  ).call,
  TypedReducer<ConnectionState, ConnectionDiscoveryFailedAction>(
    connectionDiscoveryFailedReducer,
  ).call,
]);

/// Replaces the Redux projection with the complete Known Hosts collection reported by the SDK.
/// @param state The current connection projection.
/// @param action The complete SDK-observed collection to mirror.
ConnectionState connectionKnownHostChangedReducer(
  ConnectionState state,
  ConnectionKnownHostsChangedAction action,
) {
  Option<Host>? selectedHost;
  final Host? selected = state.selectedHost;
  if (selected != null &&
      state.selectedHostSource == ConnectionHostSelectionSource.knownHost) {
    Host? current;
    for (final KnownHost knownHost in action.knownHosts) {
      if (knownHost.host.hostId == selected.hostId) {
        current = knownHost.host;
        break;
      }
    }
    selectedHost = current == null ? const None() : Some(current);
  }
  return state.copyWith(
    knownHosts: action.knownHosts,
    knownHostsStatus: KnownHostsObservationStatus.ready,
    selectedHost: selectedHost,
  );
}

/// Replaces candidates with the SDK projection and resolves candidate selection by Host ID.
/// @param state The current connection projection.
/// @param action The complete candidate collection reported by the SDK.
ConnectionState connectionCandidatesChangedReducer(
  ConnectionState state,
  ConnectionCandidatesChangedAction action,
) {
  Option<Host>? selectedHost;
  final Host? selected = state.selectedHost;
  if (selected != null &&
      state.selectedHostSource == ConnectionHostSelectionSource.candidate) {
    Host? current;
    for (final Host candidate in action.hosts) {
      if (candidate.hostId == selected.hostId) {
        current = candidate;
        break;
      }
    }
    selectedHost = current == null ? const None() : Some(current);
  }
  return state.copyWith(hosts: action.hosts, selectedHost: selectedHost);
}

/// Marks the SDK Known Hosts observation unhealthy without discarding its last complete snapshot.
/// @param state The current connection projection.
/// @param action The semantic stream-observation failure.
ConnectionState connectionKnownHostsObservationFailedReducer(
  ConnectionState state,
  ConnectionKnownHostsObservationFailedAction action,
) => state.copyWith(knownHostsStatus: KnownHostsObservationStatus.failed);

/// Handles [ConnectionHostSelectedAction].
/// Updates [ConnectionState.selectedHost] to the Host the user selected, replacing any earlier
/// selection.
ConnectionState connectionHostSelectedReducer(
  ConnectionState state,
  ConnectionHostSelectedAction action,
) => state.copyWith(
  selectedHost: Some(action.host),
  selectedHostSource: action.source,
);

/// Handles [ConnectionDiscoveryStartedAction].
/// Clears prior candidates and records that discovery is in progress.
ConnectionState connectionDiscoveryStartedReducer(
  ConnectionState state,
  ConnectionDiscoveryStartedAction action,
) => state.copyWith(
  discoveryStatus: ConnectionDiscoveryStatus.discovering,
  discoveryFailure: const None(),
);

/// Handles [ConnectionDiscoverySucceededAction].
/// Stores all candidates and distinguishes available from empty results.
ConnectionState connectionDiscoverySucceededReducer(
  ConnectionState state,
  ConnectionDiscoverySucceededAction action,
) {
  final ConnectionState withCandidates = connectionCandidatesChangedReducer(
    state,
    ConnectionCandidatesChangedAction(action.hosts),
  );
  return withCandidates.copyWith(
    discoveryStatus: withCandidates.hosts.isEmpty
        ? ConnectionDiscoveryStatus.empty
        : ConnectionDiscoveryStatus.available,
    discoveryFailure: const None(),
  );
}

/// Handles [ConnectionDiscoveryFailedAction].
/// Clears candidates and preserves the semantic failure reason for presentation.
ConnectionState connectionDiscoveryFailedReducer(
  ConnectionState state,
  ConnectionDiscoveryFailedAction action,
) => state.copyWith(
  discoveryStatus: ConnectionDiscoveryStatus.failed,
  discoveryFailure: Some(action.failure),
);
