import 'package:flutter/material.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_mark.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_retry_button.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_state_layout.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_dialog_metrics.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_button.widget.dart';

/// The pairing state after an attempt ended without pairing: a code that expired or hit its
/// attempt limit, a cancelled challenge, a revoked device, or a failed connection. Shows the
/// real reason and offers to close or start over; the prototype's "Pair again" step.
class PairingFailure extends StatelessWidget {
  /// The user-safe reason pairing ended.
  final String message;

  /// Called when the user closes the dialog.
  final VoidCallback onClose;

  /// Called when the user starts pairing over.
  final VoidCallback onRetry;

  /// Creates a failure state explaining [message].
  const PairingFailure({
    required this.message,
    required this.onClose,
    required this.onRetry,
    super.key,
  });

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    return PairingStateLayout(
      mark: const PairingMark(icon: Icons.refresh),
      heading: 'Pairing didn’t finish',
      body: message,
      children: [
        Wrap(
          alignment: WrapAlignment.center,
          spacing: DovahDialogMetrics.actionGap,
          runSpacing: DovahDialogMetrics.actionGap,
          children: [
            UnconstrainedBox(
              child: DovahButton(
                key: const Key('pairing-close-button'),
                label: 'Close',
                variant: DovahButtonVariant.secondary,
                onPressed: onClose,
              ),
            ),
            UnconstrainedBox(child: PairingRetryButton(onRetry: onRetry)),
          ],
        ),
      ],
    );
  }
}
