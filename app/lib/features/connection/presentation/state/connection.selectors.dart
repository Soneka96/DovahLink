import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/domain/entities/known_host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/viewdata/host_card.viewdata.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Static selectors over [AppState] for connection presentation state.
abstract final class ConnectionSelectors {
  /// The secondary line shown for a Known Host.
  static const String knownHostCardSubtitle = 'Known Host';

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

  /// Returns the candidate whose selection is retained while pairing awaits SDK confirmation.
  /// @param state The current application state.
  /// @return The pending candidate Host ID, or `null` when none is retained.
  static String? pendingPairingHostIdSelector(AppState state) =>
      state.connection.pendingPairingHostId;

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

  /// Returns cards for SDK-mapped Known Hosts only. Discovery candidates remain ephemeral and are
  /// presented by the Discover flow. SDK recovery phases override reachability; automatic attempts
  /// follow it while pairing is disconnected.
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
          pairingRequired: knownHost.pairingRequired,
          state: switch (knownHost.sessionState) {
            KnownHostSessionState.connecting
                when state.pairing.phase != PairingPhase.disconnected =>
              DovahConnectionCardState.connecting,
            KnownHostSessionState.connected =>
              DovahConnectionCardState.connected,
            KnownHostSessionState.reconnecting ||
            KnownHostSessionState.reauthenticating =>
              DovahConnectionCardState.reconnecting,
            KnownHostSessionState.disconnected
                when knownHost.pairingRequired &&
                    knownHost.availability == HostAvailability.online =>
              DovahConnectionCardState.repair,
            KnownHostSessionState.connecting ||
            KnownHostSessionState.disconnected =>
              switch (knownHost.availability) {
                HostAvailability.checking => DovahConnectionCardState.checking,
                HostAvailability.unknown => DovahConnectionCardState.unknown,
                HostAvailability.online => DovahConnectionCardState.available,
                HostAvailability.offline => DovahConnectionCardState.offline,
              },
          },
        ),
    ];
  }
}
