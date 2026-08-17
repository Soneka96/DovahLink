import 'package:flutter/material.dart';

/// A button requesting a new pairing challenge from the bridge.
class PairingRequestCodeButton extends StatelessWidget {
  /// Creates a pairing request-code button.
  const PairingRequestCodeButton({required this.onRequestCode, super.key});

  /// Called when the button is pressed.
  final VoidCallback onRequestCode;

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) => ElevatedButton(
    key: const Key('pairing-request-code-button'),
    onPressed: onRequestCode,
    child: const Text('Request Pairing Code'),
  );
}
