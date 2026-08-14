import 'package:equatable/equatable.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/screens/connection_status.screen.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.selectors.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// ViewModel representing the data required by [ConnectionStatusScreen].
class ConnectionStatusScreenViewModel extends Equatable {
  /// Creates a connection status ViewModel.
  const ConnectionStatusScreenViewModel({
    required this.phase,
    required this.statusLabel,
    required this.sessionId,
    required this.error,
  });

  /// Current connection lifecycle phase.
  final ConnectionPhase phase;

  /// User-visible label for [phase].
  final String statusLabel;

  /// Active session identifier, or `null` when disconnected.
  final String? sessionId;

  /// User-safe connection error, or `null`.
  final String? error;

  /// Builds a ViewModel from the Redux [store].
  factory ConnectionStatusScreenViewModel.fromStore(Store<AppState> store) {
    final AppState state = store.state;
    return ConnectionStatusScreenViewModel(
      phase: ConnectionSelectors.phaseSelector(state),
      statusLabel: ConnectionSelectors.statusLabelSelector(state),
      sessionId: ConnectionSelectors.sessionIdSelector(state),
      error: ConnectionSelectors.errorSelector(state),
    );
  }

  /// See [Equatable.props].
  @override
  List<Object?> get props => [phase, statusLabel, sessionId, error];
}
