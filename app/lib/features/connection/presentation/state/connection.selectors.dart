import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/viewmodels/host_card.viewmodel.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Static selectors over [AppState] for connection presentation state.
abstract final class ConnectionSelectors {
  /// The secondary line every Host card shows.
  static const String hostCardSubtitle = 'DovahLink Host';

  /// Returns the Hosts available to select.
  static List<HostEntity> hostsSelector(AppState state) =>
      state.connection.hosts;

  /// Returns one card's display data per Host, in Host order. Reachability is not known on the
  /// connections screen, so every card is [DovahConnectionCardState.unknown]; its detail is the
  /// Host endpoint's authority (host and port), or the whole endpoint when it has none.
  static List<HostCardViewModel> hostCardsSelector(AppState state) => [
    for (final HostEntity host in hostsSelector(state))
      HostCardViewModel(
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
