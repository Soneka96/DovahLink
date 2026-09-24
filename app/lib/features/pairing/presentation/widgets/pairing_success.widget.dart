import 'package:flutter/material.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_state_layout.widget.dart';
import 'package:dovahlink_client/shared/theme/dovah_dialog_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_button.widget.dart';

/// The pairing state after trust is established, the prototype's "You're connected" step: a
/// success mark, the Host that is now paired, and the action that finishes. Finishing only closes
/// the dialog: there is no game session to enter yet, so the prototype's "Enter game" action is
/// not offered.
class PairingSuccess extends StatelessWidget {
  /// The Host's name, shown in the copy.
  final String hostName;

  /// Called when the user finishes.
  final VoidCallback onDone;

  /// Creates a success state for [hostName].
  const PairingSuccess({
    required this.hostName,
    required this.onDone,
    super.key,
  });

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final DovahThemeTokens tokens = context.dovahTokens;

    return PairingStateLayout(
      mark: ExcludeSemantics(
        child: Container(
          key: const Key('pairing-success-mark'),
          width: DovahDialogMetrics.successMarkSize,
          height: DovahDialogMetrics.successMarkSize,
          decoration: BoxDecoration(
            shape: BoxShape.circle,
            color: tokens.success.withValues(
              alpha: DovahDialogMetrics.statusMarkFillOpacity,
            ),
            border: Border.all(
              color: tokens.success.withValues(
                alpha: DovahDialogMetrics.statusMarkBorderOpacity,
              ),
            ),
          ),
          child: Icon(
            Icons.check,
            size: DovahDialogMetrics.successGlyphSize,
            color: tokens.success,
          ),
        ),
      ),
      markBottomGap: DovahDialogMetrics.successMarkBottomGap,
      heading: 'You’re connected',
      body: '',
      highlight: hostName,
      bodyEnd:
          ' is ready. This device will reconnect automatically from now on.',
      children: [
        DovahButton(
          key: const Key('pairing-done-button'),
          label: 'Done',
          onPressed: onDone,
        ),
      ],
    );
  }
}
