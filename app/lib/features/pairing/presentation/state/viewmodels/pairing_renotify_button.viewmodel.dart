import 'package:flutter/material.dart';

import 'package:equatable/equatable.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.selectors.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Redux-backed presentation values for requesting pairing code redisplay.
class PairingRenotifyButtonViewModel extends Equatable {
  /// Whether the pairing code may be redisplayed now, with no request pending.
  final bool isAvailable;

  /// Whether the Host is waiting for Skyrim to acknowledge a redisplay request.
  final bool isPending;

  /// Remaining cooldown seconds, or null when the host did not report a cooldown.
  final int? cooldownSeconds;

  /// Dispatches a redisplay request, or is null during cooldown or while one is pending.
  final VoidCallback? onPressed;

  /// Creates a pairing code redisplay button ViewModel.
  const PairingRenotifyButtonViewModel({
    required this.isAvailable,
    required this.isPending,
    required this.cooldownSeconds,
    required this.onPressed,
  });

  /// Creates the ViewModel from the current Redux [store].
  factory PairingRenotifyButtonViewModel.fromStore(Store<AppState> store) {
    final int? cooldownSeconds =
        PairingSelectors.renotifyCooldownSecondsSelector(store.state);
    final bool isPending = PairingSelectors.renotifyPendingSelector(
      store.state,
    );
    final bool isAvailable =
        !isPending && (cooldownSeconds == null || cooldownSeconds == 0);
    return PairingRenotifyButtonViewModel(
      isAvailable: isAvailable,
      isPending: isPending,
      cooldownSeconds: cooldownSeconds,
      onPressed: isAvailable
          ? () {
              final AppState currentState = store.state;
              final int? currentCooldownSeconds =
                  PairingSelectors.renotifyCooldownSecondsSelector(
                    currentState,
                  );
              if (!PairingSelectors.renotifyPendingSelector(currentState) &&
                  (currentCooldownSeconds == null ||
                      currentCooldownSeconds == 0)) {
                store.dispatch(const PairingRenotifyRequestedAction());
              }
            }
          : null,
    );
  }

  /// Builds the button label from its pending, cooldown, and available state.
  ///
  /// [label] is shown when redisplay is available.
  /// [cooldownLabel] overrides the generated label during cooldown.
  String displayLabel({required String label, String? cooldownLabel}) {
    if (isPending) {
      return 'Sending to Skyrim…';
    }
    if (isAvailable) {
      return label;
    }
    if (cooldownLabel != null) {
      return cooldownLabel;
    }
    return '$label (${cooldownSeconds}s)';
  }

  /// See [Equatable.props].
  @override
  List<Object?> get props => [isAvailable, isPending, cooldownSeconds];
}
