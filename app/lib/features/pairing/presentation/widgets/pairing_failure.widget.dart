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

  /// The typed Host outcome, when pairing ended with a known pairing result.
  final PairingFailureOutcome? outcome;

  /// The Host-reported attempts remaining, when the outcome was a wrong code.
  final int? attemptsRemaining;

  /// A typed code-redisplay result that ended the active challenge, when present.
  final PairingRenotifyOutcome? renotifyOutcome;

  /// Called when the user closes the dialog.
  final VoidCallback onClose;

  /// Called when the user starts pairing over.
  final VoidCallback onRetry;

  /// Creates a failure state explaining [message].
  const PairingFailure({
    required this.message,
    this.outcome,
    this.attemptsRemaining,
    this.renotifyOutcome,
    required this.onClose,
    required this.onRetry,
    super.key,
  });

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final bool isExpired = outcome == PairingFailureOutcome.expired;
    final bool reachedAttemptLimit =
        outcome == PairingFailureOutcome.hardLimitReached;
    final String displayMessage =
        outcome?.message(attemptsRemaining: attemptsRemaining) ??
        renotifyOutcome?.message() ??
        message;
    return PairingStateLayout(
      mark: const PairingMark(icon: Icons.refresh),
      heading: isExpired
          ? 'Code expired'
          : reachedAttemptLimit
          ? 'Too many incorrect attempts'
          : 'Pairing didn’t finish',
      body: displayMessage,
      children: [
        Wrap(
          alignment: WrapAlignment.center,
          spacing: DovahDialogMetrics.actionGap,
          runSpacing: DovahDialogMetrics.actionGap,
          children: [
            UnconstrainedBox(
              child: DovahButton(
                key: const Key('pairing-close-button'),
                label: isExpired || reachedAttemptLimit ? 'Cancel' : 'Close',
                variant: DovahButtonVariant.secondary,
                onPressed: onClose,
              ),
            ),
            UnconstrainedBox(
              child: PairingRetryButton(
                onRetry: onRetry,
                label: isExpired || reachedAttemptLimit
                    ? 'Get a new code'
                    : 'Try Again',
              ),
            ),
          ],
        ),
      ],
    );
  }
}
