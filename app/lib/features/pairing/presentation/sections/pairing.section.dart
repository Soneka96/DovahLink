import 'package:flutter/material.dart';

import 'package:flutter_redux/flutter_redux.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_section.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_blocked.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_code_entry.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_failure.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_progress.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_repair.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_success.widget.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// The content of the pairing dialog: starts pairing when it appears, ends it when it goes away,
/// and shows the state matching the current [PairingPhase]. An unpaired session waits for the
/// user only when a trusted credential was rejected for repair; a blocked credential can only be
/// closed, and otherwise the code is already being requested. Leaving while a code is being
/// confirmed is blocked; every other exit -- the close button, Escape, the barrier -- ends
/// pairing, keeping any trust already established.
class PairingSection extends StatelessWidget {
  /// Creates the pairing section.
  const PairingSection({super.key});

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    return StoreConnector<AppState, PairingSectionViewModel>(
      distinct: true,
      onInit: (Store<AppState> store) =>
          sl<PairingSectionViewModel>(param1: store).onStart(),
      onDispose: (Store<AppState> store) =>
          sl<PairingSectionViewModel>(param1: store).onDispose(),
      converter: (Store<AppState> store) =>
          sl<PairingSectionViewModel>(param1: store),
      builder: (BuildContext context, PairingSectionViewModel viewModel) {
        void close() => Navigator.of(context).maybePop();

        return PopScope(
          canPop: viewModel.canDismiss,
          child: switch (viewModel.phase) {
            PairingPhase.unpaired when viewModel.isBlocked => PairingBlocked(
              onClose: close,
            ),
            PairingPhase.unpaired when viewModel.isRepair => PairingRepair(
              hostName: viewModel.hostName,
              message: viewModel.error,
              onCancel: close,
              onRequestCode: viewModel.onRequestCode,
            ),
            PairingPhase.none ||
            PairingPhase.connecting ||
            PairingPhase.disconnected ||
            PairingPhase.unpaired ||
            PairingPhase.requestingCode ||
            PairingPhase.confirming => PairingProgress(
              phase: viewModel.phase,
              hostName: viewModel.hostName,
              onClose: close,
            ),
            PairingPhase.awaitingCode => PairingCodeEntry(
              hostName: viewModel.hostName,
              message: viewModel.error,
              onSubmit: viewModel.onSubmitCode,
            ),
            PairingPhase.trusted => PairingSuccess(
              hostName: viewModel.hostName,
              onDone: close,
            ),
            PairingPhase.failed => PairingFailure(
              message: viewModel.error ?? 'Pairing could not be completed.',
              onClose: close,
              onRetry: viewModel.onStart,
            ),
          },
        );
      },
    );
  }
}
