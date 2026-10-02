import 'package:equatable/equatable.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/screens/connections.screen.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.selectors.dart';
import 'package:dovahlink_client/features/connection/presentation/viewdata/host_card.viewdata.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// ViewModel representing the data required by [ConnectionsScreen].
class ConnectionsScreenViewModel extends Equatable {
  /// The display data for each durable Known Host available to select.
  final List<HostCardViewData> hostCards;

  /// Whether the user may begin Host discovery in the current state.
  final bool canDiscover;

  /// Called when the user selects a card to pair or connect with.
  final void Function(HostCardViewData card) onSelectHost;

  /// Called when the user re-enters the selected Host's admitted session.
  final void Function(HostCardViewData card) onReenterConnectedHost;

  /// Creates a connections screen ViewModel.
  const ConnectionsScreenViewModel({
    required this.hostCards,
    required this.canDiscover,
    required this.onSelectHost,
    required this.onReenterConnectedHost,
  });

  /// Builds a ViewModel from the Redux [store].
  factory ConnectionsScreenViewModel.fromStore(Store<AppState> store) {
    final AppState state = store.state;
    return ConnectionsScreenViewModel(
      hostCards: ConnectionSelectors.hostCardsSelector(state),
      canDiscover: ConnectionSelectors.canDiscoverSelector(state),
      onSelectHost: (HostCardViewData card) => store.dispatch(
        ConnectionHostSelectedAction(card.host, source: card.source),
      ),
      onReenterConnectedHost: (HostCardViewData card) => store.dispatch(
        ConnectionHostReentryRequestedAction(card.host.hostId),
      ),
    );
  }

  /// See [Equatable.props].
  @override
  List<Object?> get props => [hostCards, canDiscover];
}
