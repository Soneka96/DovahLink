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
    required this.hostVersion,
    required this.error,
    required this.onStart,
    required this.onRequestCode,
    required this.onSubmitCode,
    required this.onBack,
    required this.onDispose,
  });

  /// Current pairing lifecycle phase.
  final PairingPhase phase;

  /// User-visible label for [phase].
  final String statusLabel;

  /// Reported host version, or `null` when unknown.
  final String? hostVersion;

  /// User-safe pairing error, or `null`.
  final String? error;

  /// Dispatches [PairingStartedAction].
  final void Function() onStart;

  /// Dispatches [PairingCodeRequestedAction].
  final void Function() onRequestCode;

  /// Dispatches [PairingCodeSubmittedAction].
  final void Function(String code, String? displayName) onSubmitCode;

  /// Dispatches [PairingBackRequestedAction].
  final void Function() onBack;

  /// Dispatches [PairingDisposedAction] with trust captured from the current store state.
  final void Function() onDispose;

  /// Builds a ViewModel from the Redux [store].
  factory PairingScreenViewModel.fromStore(Store<AppState> store) {
    final AppState state = store.state;
    return PairingScreenViewModel(
      phase: PairingSelectors.phaseSelector(state),
      statusLabel: PairingSelectors.statusLabelSelector(state),
      hostVersion: PairingSelectors.hostVersionSelector(state),
      error: PairingSelectors.errorSelector(state),
      onStart: () => store.dispatch(const PairingStartedAction()),
      onRequestCode: () => store.dispatch(const PairingCodeRequestedAction()),
      onSubmitCode: (String code, String? displayName) => store.dispatch(
        PairingCodeSubmittedAction(code: code, displayName: displayName),
      ),
      onBack: () => store.dispatch(const PairingBackRequestedAction()),
      onDispose: () => store.dispatch(
        PairingDisposedAction(
          wasTrusted:
              PairingSelectors.phaseSelector(store.state) ==
              PairingPhase.trusted,
        ),
      ),
    );
  }

  /// See [Equatable.props].
  @override
  List<Object?> get props => [phase, statusLabel, hostVersion, error];
}
