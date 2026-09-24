import 'package:equatable/equatable.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/connection/presentation/state/connection.selectors.dart';
import 'package:dovahlink_client/features/pairing/presentation/sections/pairing.section.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.selectors.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// ViewModel representing the data required by [PairingSection].
class PairingSectionViewModel extends Equatable {
  /// The name shown for a Host when none is selected.
  static const String unknownHostName = 'this PC';

  /// Current pairing lifecycle phase.
  final PairingPhase phase;

  /// The name of the Host being paired with, or [unknownHostName].
  final String hostName;

  /// User-safe pairing error, or `null`.
  final String? error;

  /// Whether the section may be dismissed right now.
  final bool canDismiss;

  /// Dispatches [PairingStartedAction].
  final void Function() onStart;

  /// Dispatches [PairingCodeRequestedAction].
  final void Function() onRequestCode;

  /// Dispatches [PairingCodeSubmittedAction].
  final void Function(String code, String? displayName) onSubmitCode;

  /// Dispatches [PairingDisposedAction] with trust captured from the current store state.
  final void Function() onDispose;

  /// Creates a pairing section ViewModel.
  const PairingSectionViewModel({
    required this.phase,
    required this.hostName,
    required this.error,
    required this.canDismiss,
    required this.onStart,
    required this.onRequestCode,
    required this.onSubmitCode,
    required this.onDispose,
  });

  /// Builds a ViewModel from the Redux [store].
  factory PairingSectionViewModel.fromStore(Store<AppState> store) {
    final AppState state = store.state;
    return PairingSectionViewModel(
      phase: PairingSelectors.phaseSelector(state),
      hostName:
          ConnectionSelectors.selectedHostNameSelector(state) ??
          unknownHostName,
      error: PairingSelectors.errorSelector(state),
      canDismiss: PairingSelectors.canDismissSelector(state),
      onStart: () => store.dispatch(const PairingStartedAction()),
      onRequestCode: () => store.dispatch(const PairingCodeRequestedAction()),
      onSubmitCode: (String code, String? displayName) => store.dispatch(
        PairingCodeSubmittedAction(code: code, displayName: displayName),
      ),
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
  List<Object?> get props => [phase, hostName, error, canDismiss];
}
