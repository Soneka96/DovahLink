import 'package:flutter/material.dart';

/// A button retrying a failed pairing attempt.
class PairingRetryButton extends StatelessWidget {
  /// Creates a pairing retry button.
  const PairingRetryButton({required this.onRetry, super.key});

  /// Called when the button is pressed.
  final VoidCallback onRetry;

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) => ElevatedButton(
    key: const Key('pairing-retry-button'),
    onPressed: onRetry,
    child: const Text('Try Again'),
  );
}
