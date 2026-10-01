import 'package:equatable/equatable.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.actions.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.selectors.dart';
import 'package:dovahlink_client/features/connection/presentation/viewdata/host_card.viewdata.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// ViewModel representing the real discovery state shown by [DiscoverDialog].
class DiscoverDialogViewModel extends Equatable {
  /// The current discovery operation state.
  final ConnectionDiscoveryStatus status;

  /// The current ephemeral candidates, mapped to the prototype's Local Host cards.
  final List<HostCardViewData> candidates;

  /// The semantic reason the latest discovery operation failed, or `null` when it did not fail.
  final ConnectionFailureReason? failure;

  /// Whether the user may request discovery in the current state.
  final bool canDiscover;

  /// Called when the dialog opens or the user requests another search.
  final void Function() onDiscover;

  /// Creates the discovery dialog ViewModel.
  const DiscoverDialogViewModel({
    required this.status,
    required this.candidates,
    required this.failure,
    required this.canDiscover,
    required this.onDiscover,
  });

  /// Builds a ViewModel from the Redux [store].
  factory DiscoverDialogViewModel.fromStore(Store<AppState> store) {
    final AppState state = store.state;
    return DiscoverDialogViewModel(
      status: ConnectionSelectors.discoveryStatusSelector(state),
      candidates: [
        for (final Host host in ConnectionSelectors.hostsSelector(state))
          HostCardViewData(
            host: host,
            source: ConnectionHostSelectionSource.candidate,
            title: 'Local Host',
            subtitle: 'DovahLink · Ready to connect',
            detail: host.uri.authority.isEmpty
                ? host.uri.toString()
                : host.uri.authority,
            state: DovahConnectionCardState.unknown,
          ),
      ],
      failure: ConnectionSelectors.discoveryFailureSelector(state),
      canDiscover: ConnectionSelectors.canDiscoverSelector(state),
      onDiscover: () =>
          store.dispatch(const ConnectionDiscoveryRequestedAction()),
    );
  }

  /// See [Equatable.props].
  @override
  List<Object?> get props => [status, candidates, failure, canDiscover];
}
