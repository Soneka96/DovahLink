import 'package:equatable/equatable.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/screens/host_list.screen.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.selectors.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// ViewModel representing the data required by [HostListScreen].
class HostListScreenViewModel extends Equatable {
  /// Creates a Host-list screen ViewModel.
  const HostListScreenViewModel({
    required this.hosts,
    required this.onSelectHost,
  });

  /// Builds a ViewModel from the Redux [store].
  factory HostListScreenViewModel.fromStore(Store<AppState> store) {
    final AppState state = store.state;
    return HostListScreenViewModel(
      hosts: ConnectionSelectors.hostsSelector(state),
      onSelectHost: (HostEntity host) =>
          store.dispatch(ConnectionHostSelectedAction(host)),
    );
  }

  /// The Hosts available to select.
  final List<HostEntity> hosts;

  /// Called when the user selects [host] to pair or connect with.
  final void Function(HostEntity host) onSelectHost;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [hosts];
}
