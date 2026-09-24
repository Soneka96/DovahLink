import 'package:flutter/material.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_mark.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_message.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_request_code_button.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_state_layout.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_dialog_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_button.widget.dart';

/// The pairing state where a Host no longer accepts this device's trust, the approved prototype's
/// "Pair again" step: it explains that the connection changed and waits for the user to confirm,
/// or to cancel, before a new code is requested. Trust that was removed is never re-established
/// without that confirmation.
class PairingRepair extends StatelessWidget {
  /// The Host's name, shown in the heading.
  final String hostName;

  /// Why the device has to pair again, or `null` when the reason is not known.
  final String? message;

  /// Called when the user cancels and leaves pairing.
  final VoidCallback onCancel;

  /// Called when the user confirms pairing again.
  final VoidCallback onRequestCode;

  /// Creates a pair-again state for [hostName].
  const PairingRepair({
    required this.hostName,
    required this.message,
    required this.onCancel,
    required this.onRequestCode,
    super.key,
  });

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    return PairingStateLayout(
      mark: const PairingMark(icon: Icons.refresh),
      heading: 'Pair $hostName again',
      body:
          'Your connection changed in Skyrim. Pair again to restore automatic connections.',
      children: [
        PairingMessage(key: const Key('pairing-message'), message: message),
        SizedBox(height: context.dovahDialogMetrics.actionsTopGap),
        Wrap(
          alignment: WrapAlignment.center,
          spacing: DovahDialogMetrics.actionGap,
          runSpacing: DovahDialogMetrics.actionGap,
          children: [
            UnconstrainedBox(
              child: DovahButton(
                key: const Key('pairing-repair-cancel-button'),
                label: 'Cancel',
                variant: DovahButtonVariant.secondary,
                onPressed: onCancel,
              ),
            ),
            UnconstrainedBox(
              child: PairingRequestCodeButton(onRequestCode: onRequestCode),
            ),
          ],
        ),
      ],
    );
  }
}
