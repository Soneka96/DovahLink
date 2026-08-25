import 'package:flutter/material.dart';
import 'package:flutter_redux/flutter_redux.dart';

import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.selectors.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Button to cancel the active pairing challenge, enabled only during code entry.
class PairingCancelButton extends StatelessWidget {
  const PairingCancelButton({
    this.label = 'Cancel',
    this.style = _defaultButtonStyle,
    super.key,
  });

  /// Label displayed on the button.
  final String label;

  /// Optional button style override.
  final ButtonStyle? style;

  static const ButtonStyle? _defaultButtonStyle = null;

  @override
  Widget build(BuildContext context) {
    return StoreConnector<AppState, _CancelButtonViewModel>(
      converter: (store) {
        final phase = PairingSelectors.phaseSelector(store.state);
        final isEnabled = phase == PairingPhase.awaitingCode;
        return _CancelButtonViewModel(
          isEnabled: isEnabled,
          onPressed: isEnabled
              ? () => store.dispatch(const PairingCancelRequestedAction())
              : null,
        );
      },
      builder: (context, viewModel) {
        return ElevatedButton(
          style: style,
          onPressed: viewModel.onPressed,
          child: Text(label),
        );
      },
    );
  }
}

class _CancelButtonViewModel {
  _CancelButtonViewModel({required this.isEnabled, required this.onPressed});

  final bool isEnabled;
  final VoidCallback? onPressed;
}
