import 'package:flutter/material.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_cancel_button.viewmodel.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_button.widget.dart';

/// Button to cancel the active pairing challenge, enabled only during code entry.
class PairingCancelButton extends StatelessWidget {
  /// Label displayed on the button.
  final String label;

  const PairingCancelButton({this.label = 'Cancel', super.key});

  @override
  Widget build(BuildContext context) {
    return StoreConnector<AppState, PairingCancelButtonViewModel>(
      distinct: true,
      converter: (Store<AppState> store) =>
          sl<PairingCancelButtonViewModel>(param1: store),
      builder: (context, viewModel) {
        return DovahButton(
          key: const Key('pairing-cancel-button'),
          label: label,
          variant: DovahButtonVariant.secondary,
          onPressed: viewModel.onPressed,
        );
      },
    );
  }
}
