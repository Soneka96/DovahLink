import 'package:equatable/equatable.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/pairing/presentation/state/pairing.selectors.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Redux-backed presentation value for the pairing code countdown.
class PairingCountdownViewModel extends Equatable {
  /// Remaining seconds until the pairing code expires, or null when no code is active.
  final int? remainingSeconds;

  /// Creates a pairing countdown ViewModel.
  const PairingCountdownViewModel({required this.remainingSeconds});

  /// Creates the ViewModel from the current Redux [store].
  factory PairingCountdownViewModel.fromStore(Store<AppState> store) =>
      PairingCountdownViewModel(
        remainingSeconds: PairingSelectors.codeCountdownSecondsSelector(
          store.state,
        ),
      );

  /// See [Equatable.props].
  @override
  List<Object?> get props => [remainingSeconds];
}
