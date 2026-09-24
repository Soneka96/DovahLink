import 'package:flutter/material.dart';

import 'package:equatable/equatable.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.selectors.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Redux-backed presentation values for the pairing cancellation button.
class PairingCancelButtonViewModel extends Equatable {
  /// Whether the active pairing challenge can be cancelled.
  final bool isEnabled;

  /// Dispatches a cancellation request, or is null when cancellation is unavailable.
  final VoidCallback? onPressed;

  /// Creates a pairing cancellation button ViewModel.
  const PairingCancelButtonViewModel({
    required this.isEnabled,
    required this.onPressed,
  });

  /// Creates the ViewModel from the current Redux [store].
  factory PairingCancelButtonViewModel.fromStore(Store<AppState> store) {
    final bool isEnabled =
        PairingSelectors.phaseSelector(store.state) ==
        PairingPhase.awaitingCode;
    return PairingCancelButtonViewModel(
      isEnabled: isEnabled,
      onPressed: isEnabled
          ? () => store.dispatch(const PairingCancelRequestedAction())
          : null,
    );
  }

  /// See [Equatable.props].
  @override
  List<Object?> get props => [isEnabled];
}
