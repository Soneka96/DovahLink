import 'package:flutter/material.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_cancel_button.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_code_form.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_countdown.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_mark.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_renotify_button.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_state_layout.widget.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/theme/dovah_dialog_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';

/// The pairing state where a code is showing in Skyrim and the user enters it: the prototype's
/// "Check Skyrim" step, with the code's remaining time and the actions to cancel or have it
/// shown again.
class PairingCodeEntry extends StatelessWidget {
  /// The Host's name, shown in the copy.
  final String hostName;

  /// Why the last code was not accepted, or `null` when none was rejected.
  final String? message;

  /// Called with the entered code.
  final void Function(String code) onSubmit;

  /// Creates a code-entry state.
  const PairingCodeEntry({
    required this.hostName,
    required this.message,
    required this.onSubmit,
    super.key,
  });

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final DovahThemeTokens tokens = context.dovahTokens;
    final DovahDialogMetrics metrics = context.dovahDialogMetrics;

    return PairingStateLayout(
      mark: const PairingMark(icon: Icons.desktop_windows_outlined),
      heading: 'Check Skyrim',
      body:
          'A $pairingCodeLength-digit code has appeared inside the game. Enter it below to connect this device to ',
      highlight: hostName,
      bodyEnd: '.',
      children: [
        PairingCountdown(
          label: 'Code expires in ',
          textStyle: TextStyle(
            color: tokens.textMuted,
            fontSize: DovahThemeTokens.compactFontSize,
          ),
        ),
        SizedBox(height: metrics.codeRowTopGap),
        PairingCodeForm(
          onSubmit: onSubmit,
          errorMessage: message,
          secondaryActions: const [
            PairingCancelButton(),
            PairingRenotifyButton(),
          ],
        ),
        SizedBox(height: metrics.noteTopGap),
        Text(
          'You’ll only need to do this once.',
          style: TextStyle(
            color: tokens.textMuted,
            fontSize: DovahThemeTokens.pairingNoteFontSize,
          ),
        ),
      ],
    );
  }
}
