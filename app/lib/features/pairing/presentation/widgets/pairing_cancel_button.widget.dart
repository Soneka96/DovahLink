import 'package:flutter/material.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_cancel_button.viewmodel.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Button to cancel the active pairing challenge, enabled only during code entry.
class PairingCancelButton extends StatelessWidget {
  static const ButtonStyle? _defaultButtonStyle = null;

  /// Label displayed on the button.
  final String label;

  /// Optional button style override.
  final ButtonStyle? style;
  const PairingCancelButton({
    this.label = 'Cancel',
    this.style = _defaultButtonStyle,
    super.key,
  });

  @override
  Widget build(BuildContext context) {
    return StoreConnector<AppState, PairingCancelButtonViewModel>(
      distinct: true,
      converter: (Store<AppState> store) =>
          sl<PairingCancelButtonViewModel>(param1: store),
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
