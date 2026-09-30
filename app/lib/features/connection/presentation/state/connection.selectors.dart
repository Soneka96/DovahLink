import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/domain/entities/known_host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/viewdata/host_card.viewdata.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Static selectors over [AppState] for connection presentation state.
abstract final class ConnectionSelectors {
  /// The secondary line shown for a Known Host.
  static const String knownHostCardSubtitle = 'Known Host';

  /// The secondary line shown for an untrusted discovery result.
  static const String candidateCardSubtitle = 'Discovered candidate';

  /// Returns the Hosts available to select.
  static List<Host> hostsSelector(AppState state) => state.connection.hosts;

  /// Returns the latest Host discovery operation's state.
  static ConnectionDiscoveryStatus discoveryStatusSelector(AppState state) =>
      state.connection.discoveryStatus;

  /// Returns whether a Host discovery operation may begin in the current state.
  static bool canDiscoverSelector(AppState state) =>
      discoveryStatusSelector(state) != ConnectionDiscoveryStatus.discovering;

  /// Returns the semantic reason the latest discovery operation failed, or `null` when it did not.
  static ConnectionFailureReason? discoveryFailureSelector(AppState state) =>
      state.connection.discoveryFailure;

  /// Returns the Host the user most recently selected, or `null` before any selection.
  static Host? selectedHostSelector(AppState state) =>
      state.connection.selectedHost;

  /// Returns whether the selected Host is a candidate or a durable Known Host relationship.
  static ConnectionHostSelectionSource selectedHostSourceSelector(
    AppState state,
  ) => state.connection.selectedHostSource;

  /// Returns whether the selected Known Host is in SDK-reported bounded recovery.
  static bool selectedHostIsRecoveringSelector(AppState state) {
    final Host? selectedHost = selectedHostSelector(state);
    if (selectedHost == null ||
        selectedHostSourceSelector(state) !=
            ConnectionHostSelectionSource.knownHost) {
      return false;
    }
    for (final KnownHost knownHost in state.connection.knownHosts) {
      if (knownHost.host.hostId == selectedHost.hostId) {
        return knownHost.sessionState == KnownHostSessionState.reconnecting ||
            knownHost.sessionState == KnownHostSessionState.reauthenticating;
      }
    }
    return false;
  }

  /// Returns the display name of the Host the user most recently selected, or `null` before any
  /// selection.
  static String? selectedHostNameSelector(AppState state) =>
      selectedHostSelector(state)?.displayName;

  /// Returns the SDK-mapped Known Host cards followed by its candidate cards.
  /// Session phases override weaker reachability evidence.
  static List<HostCardViewData> hostCardsSelector(AppState state) {
    final List<KnownHost> knownHosts = state.connection.knownHosts;
    return [
      for (final KnownHost knownHost in knownHosts)
        HostCardViewData(
          host: knownHost.host,
          source: ConnectionHostSelectionSource.knownHost,
          title: knownHost.host.displayName,
          subtitle: knownHostCardSubtitle,
          detail: knownHost.host.uri.authority.isEmpty
              ? knownHost.host.uri.toString()
              : knownHost.host.uri.authority,
          state: switch (knownHost.sessionState) {
            KnownHostSessionState.connecting =>
              DovahConnectionCardState.connecting,
            KnownHostSessionState.connected =>
              DovahConnectionCardState.connected,
            KnownHostSessionState.reconnecting ||
            KnownHostSessionState.reauthenticating =>
              DovahConnectionCardState.reconnecting,
            KnownHostSessionState.disconnected =>
              switch (knownHost.availability) {
                HostAvailability.checking => DovahConnectionCardState.checking,
                HostAvailability.unknown => DovahConnectionCardState.unknown,
                HostAvailability.online => DovahConnectionCardState.available,
                HostAvailability.offline => DovahConnectionCardState.offline,
              },
          },
        ),
      for (final Host host in hostsSelector(state))
        HostCardViewData(
          host: host,
          source: ConnectionHostSelectionSource.candidate,
          title: host.displayName,
          subtitle: candidateCardSubtitle,
          detail: host.uri.authority.isEmpty
              ? host.uri.toString()
              : host.uri.authority,
          state: DovahConnectionCardState.unknown,
        ),
    ];
  }
}
