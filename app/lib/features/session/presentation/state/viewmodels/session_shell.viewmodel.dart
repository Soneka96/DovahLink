import 'package:equatable/equatable.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.selectors.dart';
import 'package:dovahlink_client/features/connection/presentation/viewdata/host_card.viewdata.dart';
import 'package:dovahlink_client/features/session/presentation/state/session_shell.actions.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// ViewModel representing the real Known Host shown by the Session Shell.
class SessionShellViewModel extends Equatable {
  /// The Host card projection for this route, or `null` if its Known Host was removed.
  final HostCardViewData? host;

  /// Called when the user returns to Connections, leaving the session active.
  final void Function() onBack;

  /// Creates a Session Shell ViewModel.
  const SessionShellViewModel({required this.host, required this.onBack});

  /// Builds the presentation projection for [hostId] from Redux state.
  factory SessionShellViewModel.fromStore(
    Store<AppState> store, {
    required String hostId,
  }) {
    final List<HostCardViewData> cards = ConnectionSelectors.hostCardsSelector(
      store.state,
    );
    HostCardViewData? host;
    for (final HostCardViewData card in cards) {
      if (card.host.hostId == hostId) {
        host = card;
        break;
      }
    }
    return SessionShellViewModel(
      host: host,
      onBack: () => store.dispatch(const SessionShellBackRequestedAction()),
    );
  }

  /// See [Equatable.props].
  @override
  List<Object?> get props => [host];
}
