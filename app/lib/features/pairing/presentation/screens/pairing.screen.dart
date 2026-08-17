import 'package:flutter/material.dart';
import 'package:flutter_redux/flutter_redux.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_screen.viewmodel.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_code_form.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_loading.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_request_code_button.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_retry_button.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_trusted.widget.dart';
import 'package:dovahlink_client/injection_container.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Displays the local device pairing flow: authenticating, requesting a
/// code, entering it, and confirming trust.
class PairingScreen extends StatelessWidget {
  /// Creates the pairing screen.
  const PairingScreen({super.key});

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    return StoreConnector<AppState, PairingScreenViewModel>(
      distinct: true,
      onInit: (Store<AppState> store) =>
          store.dispatch(const PairingStartedAction()),
      onDispose: (Store<AppState> store) => store.dispatch(
        PairingDisposedAction(
          wasTrusted: store.state.pairing.phase == PairingPhase.trusted,
        ),
      ),
      converter: (Store<AppState> store) =>
          sl<PairingScreenViewModel>(param1: store),
      builder: (BuildContext context, PairingScreenViewModel viewModel) {
        final String? error = viewModel.error;
        return Scaffold(
          // LayoutBuilder + a min-height ConstrainedBox keeps the normal case centered exactly as
          // before, while letting content that's taller than the viewport at a large text scale
          // scroll instead of overflowing.
          body: LayoutBuilder(
            builder: (BuildContext context, BoxConstraints constraints) {
              return SingleChildScrollView(
                child: ConstrainedBox(
                  constraints: BoxConstraints(minHeight: constraints.maxHeight),
                  child: Center(
                    child: Padding(
                      padding: const EdgeInsets.all(24),
                      child: Column(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          Text(
                            'DovahLink Pairing',
                            style: Theme.of(context).textTheme.headlineSmall,
                          ),
                          const SizedBox(height: 16),
                          Text(
                            viewModel.statusLabel,
                            key: const Key('pairing-status'),
                            style: Theme.of(context).textTheme.titleMedium,
                          ),
                          const SizedBox(height: 16),
                          switch (viewModel.phase) {
                            PairingPhase.none ||
                            PairingPhase.connecting ||
                            PairingPhase.disconnected ||
                            PairingPhase.requestingCode ||
                            PairingPhase.confirming =>
                              const PairingLoadingIndicator(),
                            PairingPhase.unpaired => PairingRequestCodeButton(
                              onRequestCode: viewModel.onRequestCode,
                            ),
                            PairingPhase.awaitingCode => PairingCodeForm(
                              onSubmit: viewModel.onSubmitCode,
                            ),
                            PairingPhase.trusted =>
                              const PairingTrustedIndicator(),
                            PairingPhase.failed => PairingRetryButton(
                              onRetry: viewModel.onStart,
                            ),
                          },
                          if (error != null) ...[
                            const SizedBox(height: 16),
                            Text(
                              error,
                              key: const Key('pairing-error'),
                              style: Theme.of(
                                context,
                              ).textTheme.bodyMedium?.copyWith(
                                color: Theme.of(context).colorScheme.error,
                              ),
                            ),
                          ],
                        ],
                      ),
                    ),
                  ),
                ),
              );
            },
          ),
        );
      },
    );
  }
}
