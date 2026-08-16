import 'package:equatable/equatable.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/pairing/presentation/screens/pairing.screen.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.selectors.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// ViewModel representing the data required by [PairingScreen].
class PairingScreenViewModel extends Equatable {
  /// Creates a pairing screen ViewModel.
  const PairingScreenViewModel({
    required this.phase,
    required this.statusLabel,
    required this.bridgeVersion,
    required this.error,
    required this.onStart,
    required this.onRequestCode,
    required this.onSubmitCode,
  });

  /// Current pairing lifecycle phase.
  final PairingPhase phase;

  /// User-visible label for [phase].
  final String statusLabel;

  /// Reported bridge version, or `null` when unknown.
  final String? bridgeVersion;

  /// User-safe pairing error, or `null`.
  final String? error;

  /// Dispatches [PairingStartedAction].
  final void Function() onStart;

  /// Dispatches [PairingCodeRequestedAction].
  final void Function() onRequestCode;

  /// Dispatches [PairingCodeSubmittedAction].
  final void Function(String code, String? displayName) onSubmitCode;

  /// Builds a ViewModel from the Redux [store].
  factory PairingScreenViewModel.fromStore(Store<AppState> store) {
    final AppState state = store.state;
    return PairingScreenViewModel(
      phase: PairingSelectors.phaseSelector(state),
      statusLabel: PairingSelectors.statusLabelSelector(state),
      bridgeVersion: PairingSelectors.bridgeVersionSelector(state),
      error: PairingSelectors.errorSelector(state),
      onStart: () => store.dispatch(const PairingStartedAction()),
      onRequestCode: () => store.dispatch(const PairingCodeRequestedAction()),
      onSubmitCode: (String code, String? displayName) => store.dispatch(
        PairingCodeSubmittedAction(code: code, displayName: displayName),
      ),
    );
  }

  /// See [Equatable.props].
  @override
  List<Object?> get props => [phase, statusLabel, bridgeVersion, error];
}
