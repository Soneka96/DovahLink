import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/viewdata/host_card.viewdata.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Static selectors over [AppState] for connection presentation state.
abstract final class ConnectionSelectors {
  /// The secondary line every Host card shows.
  static const String hostCardSubtitle = 'DovahLink Host';

  /// Returns the Hosts available to select.
  static List<Host> hostsSelector(AppState state) => state.connection.hosts;

  /// Returns the Host the user most recently selected, or `null` before any selection.
  static Host? selectedHostSelector(AppState state) =>
      state.connection.selectedHost;

  /// Returns the display name of the Host the user most recently selected, or `null` before any
  /// selection.
  static String? selectedHostNameSelector(AppState state) =>
      selectedHostSelector(state)?.displayName;

  /// Returns one card's display data per Host, in Host order. Reachability is not known on the
  /// connections screen, so every card is [DovahConnectionCardState.unknown]; its detail is the
  /// Host endpoint's authority (host and port), or the whole endpoint when it has none.
  static List<HostCardViewData> hostCardsSelector(AppState state) => [
    for (final Host host in hostsSelector(state))
      HostCardViewData(
        host: host,
        title: host.displayName,
        subtitle: hostCardSubtitle,
        detail: host.uri.authority.isEmpty
            ? host.uri.toString()
            : host.uri.authority,
        state: DovahConnectionCardState.unknown,
      ),
  ];
}
