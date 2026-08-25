import 'package:flutter/material.dart';

/// The six-digit code and optional display-name entry form shown while
/// pairing is awaiting the user to enter a code.
class PairingCodeForm extends StatefulWidget {
  /// Creates a pairing code form.
  const PairingCodeForm({required this.onSubmit, super.key});

  /// Called with the entered code and optional display name.
  final void Function(String code, String? displayName) onSubmit;

  /// See [StatefulWidget.createState].
  @override
  State<PairingCodeForm> createState() => _PairingCodeFormState();
}

/// State for [PairingCodeForm].
class _PairingCodeFormState extends State<PairingCodeForm> {
  /// Controls the six-digit code field.
  final TextEditingController _codeController = TextEditingController();

  /// Controls the optional display-name field.
  final TextEditingController _displayNameController = TextEditingController();

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
