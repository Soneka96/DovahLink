import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/widgets/dovah_button.widget.dart';

/// The primary action that confirms pairing again, asking the Host to show a pairing code in
/// Skyrim.
class PairingRequestCodeButton extends StatelessWidget {
  /// Called when the button is pressed.
  final VoidCallback onRequestCode;

  /// Creates a request-code button.
  const PairingRequestCodeButton({required this.onRequestCode, super.key});

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) => DovahButton(
    key: const Key('pairing-request-code-button'),
    label: 'Pair again',
    onPressed: onRequestCode,
  );
}
