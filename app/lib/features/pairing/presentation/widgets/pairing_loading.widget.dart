import 'package:flutter/material.dart';

/// Shows that a pairing operation (connecting, waiting for the bridge,
/// requesting a code, or confirming) is in progress.
class PairingLoadingIndicator extends StatelessWidget {
  /// Creates a pairing loading indicator.
  const PairingLoadingIndicator({super.key});

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) =>
      const CircularProgressIndicator(key: Key('pairing-loading'));
}
