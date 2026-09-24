import 'package:equatable/equatable.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/screens/connections.screen.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.selectors.dart';
import 'package:dovahlink_client/features/connection/presentation/viewdata/host_card.viewdata.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// ViewModel representing the data required by [ConnectionsScreen].
class ConnectionsScreenViewModel extends Equatable {
  /// The display data for each Host available to select.
  final List<HostCardViewData> hostCards;

  /// Called when the user selects [host] to pair or connect with.
  final void Function(Host host) onSelectHost;

  /// Creates a connections screen ViewModel.
  const ConnectionsScreenViewModel({
    required this.hostCards,
    required this.onSelectHost,
  });

  /// Builds a ViewModel from the Redux [store].
  factory ConnectionsScreenViewModel.fromStore(Store<AppState> store) {
    final AppState state = store.state;
    return ConnectionsScreenViewModel(
      hostCards: ConnectionSelectors.hostCardsSelector(state),
      onSelectHost: (Host host) =>
          store.dispatch(ConnectionHostSelectedAction(host)),
    );
  }

  /// See [Equatable.props].
  @override
  List<Object?> get props => [hostCards];
}
