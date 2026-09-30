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
  TypedReducer<ConnectionState, ConnectionCandidatePairingStartedAction>(
    connectionCandidatePairingStartedReducer,
  ).call,
  TypedReducer<ConnectionState, ConnectionCandidatePairingEndedAction>(
    connectionCandidatePairingEndedReducer,
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
  ConnectionHostSelectionSource? selectedHostSource;
  final Host? selected = state.selectedHost;
  final String? pendingPairingHostId = state.pendingPairingHostId;
  KnownHost? confirmedPairingHost;
  if (pendingPairingHostId != null) {
    for (final KnownHost knownHost in action.knownHosts) {
      if (knownHost.host.hostId == pendingPairingHostId) {
        confirmedPairingHost = knownHost;
        break;
      }
    }
  }
  if (selected != null &&
      state.selectedHostSource == ConnectionHostSelectionSource.candidate) {
    for (final KnownHost knownHost in action.knownHosts) {
      if (knownHost.host.hostId == selected.hostId) {
        selectedHost = Some(knownHost.host);
        selectedHostSource = ConnectionHostSelectionSource.knownHost;
        break;
      }
    }
  } else if (selected != null) {
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
    selectedHostSource: selectedHostSource,
    pendingPairingHostId: confirmedPairingHost == null ? null : const None(),
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
  ConnectionHostSelectionSource? selectedHostSource;
  final Host? selected = state.selectedHost;
  if (selected != null &&
      state.selectedHostSource == ConnectionHostSelectionSource.candidate) {
    for (final KnownHost knownHost in state.knownHosts) {
      if (knownHost.host.hostId == selected.hostId) {
        selectedHost = Some(knownHost.host);
        selectedHostSource = ConnectionHostSelectionSource.knownHost;
        break;
      }
    }
  }
  if (selected != null &&
      selectedHost == null &&
      state.selectedHostSource == ConnectionHostSelectionSource.candidate) {
    Host? current;
    for (final Host candidate in action.hosts) {
      if (candidate.hostId == selected.hostId) {
        current = candidate;
        break;
      }
    }
    selectedHost =
        current == null && state.pendingPairingHostId != selected.hostId
        ? const None()
        : Some(current ?? selected);
  }
  final String? pendingPairingHostId = state.pendingPairingHostId;
  final bool pairingHostBecameKnown =
      pendingPairingHostId != null &&
      state.knownHosts.any(
        (KnownHost knownHost) => knownHost.host.hostId == pendingPairingHostId,
      );
  return state.copyWith(
    hosts: action.hosts,
    selectedHost: selectedHost,
    selectedHostSource: selectedHostSource,
    pendingPairingHostId: pairingHostBecameKnown ? const None() : null,
  );
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

/// Retains the selected candidate until the SDK confirms its Known Host state.
/// @param state The current connection projection.
/// @param action The candidate pairing confirmation that started.
ConnectionState connectionCandidatePairingStartedReducer(
  ConnectionState state,
  ConnectionCandidatePairingStartedAction action,
) => state.copyWith(pendingPairingHostId: Some(action.hostId));

/// Releases a pending selection when its untrusted pairing operation ends.
/// @param state The current connection projection.
/// @param action The candidate pairing confirmation that ended.
ConnectionState connectionCandidatePairingEndedReducer(
  ConnectionState state,
  ConnectionCandidatePairingEndedAction action,
) {
  if (state.pendingPairingHostId != action.hostId) {
    return state;
  }
  final Host? selected = state.selectedHost;
  final bool selectedCandidateVanished =
      selected?.hostId == action.hostId &&
      state.selectedHostSource == ConnectionHostSelectionSource.candidate &&
      !state.hosts.any((Host host) => host.hostId == action.hostId);
  return state.copyWith(
    selectedHost: selectedCandidateVanished ? const None() : null,
    pendingPairingHostId: const None(),
  );
}

/// Handles [ConnectionDiscoveryStartedAction].
/// Clears prior candidates and records that discovery is in progress.
ConnectionState connectionDiscoveryStartedReducer(
  ConnectionState state,
  ConnectionDiscoveryStartedAction action,
) => state.copyWith(
  discoveryStatus: ConnectionDiscoveryStatus.discovering,
  discoveryFailure: const None(),
);

/// Handles [ConnectionDiscoverySucceededAction] without changing SDK-owned candidates.
/// @param state The current connection projection.
/// @param action The discovery operation's candidate-presence result.
/// @return The projection with its discovery status updated.
ConnectionState connectionDiscoverySucceededReducer(
  ConnectionState state,
  ConnectionDiscoverySucceededAction action,
) => state.copyWith(
  discoveryStatus: action.hasCandidates
      ? ConnectionDiscoveryStatus.available
      : ConnectionDiscoveryStatus.empty,
  discoveryFailure: const None(),
);

/// Handles [ConnectionDiscoveryFailedAction].
/// Clears candidates and preserves the semantic failure reason for presentation.
ConnectionState connectionDiscoveryFailedReducer(
  ConnectionState state,
  ConnectionDiscoveryFailedAction action,
) => state.copyWith(
  discoveryStatus: ConnectionDiscoveryStatus.failed,
  discoveryFailure: Some(action.failure),
);
