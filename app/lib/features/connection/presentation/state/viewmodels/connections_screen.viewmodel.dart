import 'package:equatable/equatable.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/screens/connections.screen.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.selectors.dart';
import 'package:dovahlink_client/features/connection/presentation/viewdata/host_card.viewdata.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// ViewModel representing the data required by [ConnectionsScreen].
class ConnectionsScreenViewModel extends Equatable {
  /// The display data for each Host available to select.
  final List<HostCardViewData> hostCards;

  /// The latest Host discovery operation's state.
  final ConnectionDiscoveryStatus discoveryStatus;

  /// Whether the user may begin Host discovery in the current state.
  final bool canDiscover;

  /// The semantic reason the latest discovery operation failed, or `null` when it did not fail.
  final ConnectionFailureReason? discoveryFailure;

  /// Called when the user requests Host discovery.
  final void Function() onDiscover;

  /// Called when the user selects a card to pair or connect with.
  final void Function(HostCardViewData card) onSelectHost;

  /// Creates a connections screen ViewModel.
  const ConnectionsScreenViewModel({
    required this.hostCards,
    required this.discoveryStatus,
    required this.canDiscover,
    required this.discoveryFailure,
    required this.onDiscover,
    required this.onSelectHost,
  });

  /// Builds a ViewModel from the Redux [store].
  factory ConnectionsScreenViewModel.fromStore(Store<AppState> store) {
    final AppState state = store.state;
    return ConnectionsScreenViewModel(
      hostCards: ConnectionSelectors.hostCardsSelector(state),
      discoveryStatus: ConnectionSelectors.discoveryStatusSelector(state),
      canDiscover: ConnectionSelectors.canDiscoverSelector(state),
      discoveryFailure: ConnectionSelectors.discoveryFailureSelector(state),
      onDiscover: () =>
          store.dispatch(const ConnectionDiscoveryRequestedAction()),
      onSelectHost: (HostCardViewData card) => store.dispatch(
        ConnectionHostSelectedAction(card.host, source: card.source),
      ),
    );
  }

  /// See [Equatable.props].
  @override
  List<Object?> get props => [
    hostCards,
    discoveryStatus,
    canDiscover,
    discoveryFailure,
  ];
}
