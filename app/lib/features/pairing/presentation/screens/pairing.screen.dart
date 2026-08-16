import 'package:flutter/material.dart';
import 'package:flutter_redux/flutter_redux.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/pairing/presentation/state/pairing.actions.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/viewmodels/pairing_screen.viewmodel.dart';
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
      onDispose: (Store<AppState> store) =>
          store.dispatch(const PairingDisposedAction()),
      converter: (Store<AppState> store) =>
          sl<PairingScreenViewModel>(param1: store),
      builder: (BuildContext context, PairingScreenViewModel viewModel) {
        final String? error = viewModel.error;
        return Scaffold(
          body: Center(
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
                  ..._body(viewModel),
                  if (error != null) ...[
                    const SizedBox(height: 16),
                    Text(
                      error,
                      key: const Key('pairing-error'),
                      style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                        color: Theme.of(context).colorScheme.error,
                      ),
                    ),
                  ],
                ],
              ),
            ),
          ),
        );
      },
    );
  }

  /// Returns the widgets specific to [viewModel]'s current phase.
  List<Widget> _body(PairingScreenViewModel viewModel) {
    switch (viewModel.phase) {
      case PairingPhase.none:
      case PairingPhase.connecting:
      case PairingPhase.requestingCode:
      case PairingPhase.confirming:
        return const [CircularProgressIndicator(key: Key('pairing-loading'))];
      case PairingPhase.unpaired:
        return [
          ElevatedButton(
            key: const Key('pairing-request-code-button'),
            onPressed: viewModel.onRequestCode,
            child: const Text('Request Pairing Code'),
          ),
        ];
      case PairingPhase.awaitingCode:
        return [_PairingCodeForm(onSubmit: viewModel.onSubmitCode)];
      case PairingPhase.trusted:
        return const [Text('Paired', key: Key('pairing-trusted'))];
      case PairingPhase.failed:
        return [
          ElevatedButton(
            key: const Key('pairing-retry-button'),
            onPressed: viewModel.onStart,
            child: const Text('Try Again'),
          ),
        ];
    }
  }
}

/// The six-digit code and optional display-name entry form shown while
/// [PairingPhase.awaitingCode].
class _PairingCodeForm extends StatefulWidget {
  /// Creates a pairing code form.
  const _PairingCodeForm({required this.onSubmit});

  /// Called with the entered code and optional display name.
  final void Function(String code, String? displayName) onSubmit;

  /// See [StatefulWidget.createState].
  @override
  State<_PairingCodeForm> createState() => _PairingCodeFormState();
}

/// State for [_PairingCodeForm].
class _PairingCodeFormState extends State<_PairingCodeForm> {
  /// Controls the six-digit code field.
  final TextEditingController _codeController = TextEditingController();

  /// Controls the optional display-name field.
  final TextEditingController _displayNameController =
      TextEditingController();

  /// Owns focus for the code field, fixing its place first in traversal
  /// order ahead of the display-name field.
  final FocusNode _codeFocusNode = FocusNode();

  /// Owns focus for the display-name field.
  final FocusNode _displayNameFocusNode = FocusNode();

  /// See [State.dispose].
  @override
  void dispose() {
    _codeController.dispose();
    _displayNameController.dispose();
    _codeFocusNode.dispose();
    _displayNameFocusNode.dispose();
    super.dispose();
  }

  /// See [State.build].
  @override
  Widget build(BuildContext context) {
    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        const Text('Enter the code shown in Skyrim.'),
        const SizedBox(height: 8),
        TextField(
          key: const Key('pairing-code-field'),
          controller: _codeController,
          focusNode: _codeFocusNode,
          maxLength: 6,
          keyboardType: TextInputType.number,
          decoration: const InputDecoration(labelText: 'Pairing code'),
        ),
        TextField(
          key: const Key('pairing-display-name-field'),
          controller: _displayNameController,
          focusNode: _displayNameFocusNode,
          decoration: const InputDecoration(
            labelText: 'Device name (optional)',
          ),
        ),
        const SizedBox(height: 8),
        ElevatedButton(
          key: const Key('pairing-confirm-button'),
          onPressed: () {
            final String code = _codeController.text.trim();
            final String displayName = _displayNameController.text.trim();
            widget.onSubmit(code, displayName.isEmpty ? null : displayName);
          },
          child: const Text('Confirm'),
        ),
      ],
    );
  }
}
