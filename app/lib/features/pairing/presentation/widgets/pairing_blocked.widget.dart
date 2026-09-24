import 'package:flutter/material.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_mark.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_state_layout.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_button.widget.dart';

/// The non-repairable pairing state shown when the Host has blocked this device.
class PairingBlocked extends StatelessWidget {
  /// Called when the user closes the dialog.
  final VoidCallback onClose;

  /// Creates the blocked state with a safe way to close the dialog.
  const PairingBlocked({
    /// Callback used to close the pairing dialog.
    required this.onClose,
    super.key,
  });

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) => PairingStateLayout(
    mark: const PairingMark(icon: Icons.block),
    heading: 'Pairing unavailable',
    body:
        'This device is blocked by the Host and cannot pair again until an administrator '
        'unblocks it.',
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
