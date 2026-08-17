import 'package:flutter/material.dart';

/// Shows that this installation holds a trusted credential for the bridge.
class PairingTrustedIndicator extends StatelessWidget {
  /// Creates a pairing trusted indicator.
  const PairingTrustedIndicator({super.key});

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) =>
      const Text('Paired', key: Key('pairing-trusted'));
}
