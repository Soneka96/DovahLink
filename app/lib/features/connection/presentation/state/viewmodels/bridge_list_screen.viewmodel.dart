import 'package:equatable/equatable.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/domain/entities/bridge.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/screens/bridge_list.screen.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.selectors.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// ViewModel representing the data required by [BridgeListScreen].
class BridgeListScreenViewModel extends Equatable {
  /// Creates a Bridge-list screen ViewModel.
  const BridgeListScreenViewModel({
    required this.bridges,
    required this.onSelectBridge,
  });

  /// Builds a ViewModel from the Redux [store].
  factory BridgeListScreenViewModel.fromStore(Store<AppState> store) {
    final AppState state = store.state;
    return BridgeListScreenViewModel(
      bridges: ConnectionSelectors.bridgesSelector(state),
      onSelectBridge: (BridgeEntity bridge) =>
          store.dispatch(ConnectionBridgeSelectedAction(bridge)),
    );
  }

  /// The Bridges available to select.
  final List<BridgeEntity> bridges;

  /// Called when the user selects [bridge] to pair or connect with.
  final void Function(BridgeEntity bridge) onSelectBridge;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [bridges];
}
