import 'package:flutter/material.dart';

/// A button leaving the pairing screen and returning to home.
class PairingBackButton extends StatelessWidget {
  /// Creates a pairing back button.
  const PairingBackButton({required this.onBack, super.key});

  /// Called when the button is pressed.
  final VoidCallback onBack;

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) => IconButton(
    key: const Key('pairing-back-button'),
    onPressed: onBack,
    icon: const Icon(Icons.arrow_back),
    tooltip: 'Back',
  );
}
