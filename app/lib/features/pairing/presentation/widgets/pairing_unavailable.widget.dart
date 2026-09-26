import 'package:flutter/material.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_mark.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_state_layout.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_button.widget.dart';

/// The pairing state shown when the current platform has no secure client storage.
class PairingUnavailable extends StatelessWidget {
  /// Called when the user closes the pairing dialog.
  final VoidCallback onClose;

  /// Creates the unavailable state with a safe way to close the dialog.
  const PairingUnavailable({required this.onClose, super.key});

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) => PairingStateLayout(
    mark: const PairingMark(icon: Icons.lock_outline),
    heading: 'Pairing unavailable',
    body:
        'This platform does not have secure storage for the device identity and pairing '
        'credentials yet.',
    children: [
      UnconstrainedBox(
        child: DovahButton(
          key: const Key('pairing-close-button'),
          label: 'Close',
          variant: DovahButtonVariant.secondary,
          onPressed: onClose,
        ),
      ),
    ],
  );
}
