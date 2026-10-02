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
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_unavailable.widget.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Standalone pairing content that starts a lifecycle unless authentication already started and
/// ends it when removed. [PairingSection.parentOwned] lets an enclosing flow own both actions. This
/// section shows the state matching the current [PairingPhase]. An unpaired session waits for the
/// user only when a trusted credential was rejected for repair; a blocked credential can only be
/// closed, and otherwise the code is already being requested. A Connections card that already got
/// explicit repair confirmation proceeds directly to code request after the SDK reports the
/// rejected credential. Leaving while a code is being confirmed is blocked; every other standalone
/// exit keeps any trust already established.
class PairingSection extends StatefulWidget {
  /// Whether this section starts a new authentication when it is mounted.
  final bool startOnInit;

  /// Whether this section ends pairing through [PairingSectionViewModel.onDispose] when it is
  /// removed. `false` only for [PairingSection.parentOwned], whose parent ends the lifecycle itself.
  final bool disposeOnRemove;

  /// Whether the Connections card already obtained explicit confirmation to repair the Host.
  final bool requestCodeAfterConfirmedRepair;

  /// Creates a standalone pairing section that owns the end of the pairing lifecycle.
  /// @param startOnInit Whether to start authentication when the section appears.
  const PairingSection({
    this.startOnInit = true,
    this.requestCodeAfterConfirmedRepair = false,
    super.key,
  }) : disposeOnRemove = true;

  /// Creates a pairing section embedded in a parent that owns the whole pairing lifecycle: the
  /// section neither starts authentication when mounted nor disposes pairing when removed.
  const PairingSection.parentOwned({super.key})
    : startOnInit = false,
      disposeOnRemove = false,
      requestCodeAfterConfirmedRepair = false;

  /// Creates the state that follows a previously confirmed repair through authentication.
  @override
  State<PairingSection> createState() => _PairingSectionState();
}

/// Tracks whether the user-confirmed repair has requested its new code.
class _PairingSectionState extends State<PairingSection> {
  /// Prevents duplicate code requests if the repair state is reported more than once.
  bool _repairCodeRequestStarted = false;

  /// Requests the code once the SDK reports the credential rejected after user confirmation.
  void _requestCodeForConfirmedRepair(PairingSectionViewModel viewModel) {
    if (!widget.requestCodeAfterConfirmedRepair) return;
    if (viewModel.phase == PairingPhase.connecting) {
      _repairCodeRequestStarted = false;
      return;
    }
    if (!viewModel.isRepair || _repairCodeRequestStarted) return;
    _repairCodeRequestStarted = true;
    viewModel.onRequestCode();
  }

  /// See [State.build].
  @override
  Widget build(BuildContext context) {
    return StoreConnector<AppState, PairingSectionViewModel>(
      distinct: true,
      onInit: (Store<AppState> store) {
        if (widget.startOnInit) {
          sl<PairingSectionViewModel>(param1: store).onStart();
        }
      },
      onDispose: (Store<AppState> store) {
        if (widget.disposeOnRemove) {
          sl<PairingSectionViewModel>(param1: store).onDispose();
        }
      },
      converter: (Store<AppState> store) =>
          sl<PairingSectionViewModel>(param1: store),
      onInitialBuild: _requestCodeForConfirmedRepair,
      onDidChange: (_, PairingSectionViewModel viewModel) =>
          _requestCodeForConfirmedRepair(viewModel),
      builder: (BuildContext context, PairingSectionViewModel viewModel) {
        void close() => Navigator.of(context).maybePop();

        if (viewModel.support == PairingSupport.secureStorageUnavailable) {
          return PopScope(
            canPop: viewModel.canDismiss,
            child: PairingUnavailable(onClose: close),
          );
        }

        return PopScope(
          canPop: viewModel.canDismiss,
          child: viewModel.isReconnecting
              ? PairingProgress(
                  phase: PairingPhase.disconnected,
                  hostName: viewModel.hostName,
                  isReconnecting: true,
                  onClose: close,
                )
              : widget.requestCodeAfterConfirmedRepair && viewModel.isRepair
              ? PairingProgress(
                  phase: PairingPhase.requestingCode,
                  hostName: viewModel.hostName,
                  onClose: close,
                )
              : switch (viewModel.phase) {
                  PairingPhase.unpaired when viewModel.isBlocked =>
                    PairingBlocked(onClose: close),
                  PairingPhase.unpaired when viewModel.isRepair =>
                    PairingRepair(
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
                    isReconnecting: viewModel.isReconnecting,
                    onClose: close,
                  ),
                  PairingPhase.awaitingCode => PairingCodeEntry(
                    hostName: viewModel.hostName,
                    message: viewModel.error,
                    failureOutcome: viewModel.pairingOutcome,
                    attemptsRemaining: viewModel.attemptsRemaining,
                    onSubmit: viewModel.onSubmitCode,
                  ),
                  PairingPhase.trusted => PairingSuccess(
                    hostName: viewModel.hostName,
                    onDone: close,
                  ),
                  PairingPhase.failed => PairingFailure(
                    message:
                        viewModel.error ?? 'Pairing could not be completed.',
                    outcome: viewModel.pairingOutcome,
                    attemptsRemaining: viewModel.attemptsRemaining,
                    renotifyOutcome: viewModel.renotifyOutcome,
                    onClose: close,
                    onRetry: viewModel.onStart,
                  ),
                },
        );
      },
    );
  }
}
