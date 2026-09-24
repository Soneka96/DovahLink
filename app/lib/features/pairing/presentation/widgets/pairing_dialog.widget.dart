import 'package:flutter/material.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/pairing/presentation/sections/pairing.section.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_dialog.viewmodel.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_dialog.widget.dart';

/// The themed dialog pairing happens in: a [DovahDialog] whose title follows the pairing state
/// (the approved prototype changes its modal header per step), around the [PairingSection].
class PairingDialog extends StatelessWidget {
  /// Creates the pairing dialog card.
  const PairingDialog({super.key});

  /// Shows the pairing dialog over [context]'s route, behind the shared blurred backdrop.
  static Future<void> show(BuildContext context) =>
      DovahDialog.showBuilder<void>(
        context,
        builder: (BuildContext dialogContext) => const PairingDialog(),
      );

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    return StoreConnector<AppState, PairingDialogViewModel>(
      distinct: true,
      converter: (Store<AppState> store) =>
          sl<PairingDialogViewModel>(param1: store),
      builder: (BuildContext context, PairingDialogViewModel viewModel) =>
          DovahDialog(title: viewModel.title, child: const PairingSection()),
    );
  }
}
