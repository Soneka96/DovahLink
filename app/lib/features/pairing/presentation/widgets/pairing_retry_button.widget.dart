import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/widgets/dovah_button.widget.dart';

/// The primary action that restarts pairing after it failed.
class PairingRetryButton extends StatelessWidget {
  /// Called when the button is pressed.
  final VoidCallback onRetry;

  /// Creates a retry button.
  const PairingRetryButton({required this.onRetry, super.key});

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) => DovahButton(
    key: const Key('pairing-retry-button'),
    label: 'Try Again',
    onPressed: onRetry,
  );
}
