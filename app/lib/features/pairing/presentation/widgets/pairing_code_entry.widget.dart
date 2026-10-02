import 'package:flutter/material.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_cancel_button.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_code_form.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_countdown.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_mark.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_renotify_button.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_state_layout.widget.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
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

  /// The typed Host code-submission result, when the field contains an outcome-specific message.
  final PairingFailureOutcome? failureOutcome;

  /// Host-reported wrong-code attempts remaining, when present.
  final int? attemptsRemaining;

  /// Called with the entered code.
  final void Function(String code) onSubmit;

  /// Creates a code-entry state.
  const PairingCodeEntry({
    required this.hostName,
    required this.message,
    this.failureOutcome,
    this.attemptsRemaining,
    required this.onSubmit,
    super.key,
  });

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final DovahThemeTokens tokens = context.dovahTokens;
    final DovahDialogMetrics metrics = context.dovahDialogMetrics;
    final String? errorMessage =
        failureOutcome?.message(attemptsRemaining: attemptsRemaining) ??
        message;

    return PairingStateLayout(
      mark: const PairingMark(icon: Icons.desktop_windows_outlined),
      heading: 'Check Skyrim',
      body:
          'A $pairingCodeLength-digit code has appeared inside the game. Enter it below to connect this device to ',
      highlight: hostName,
      bodyEnd: '.',
      children: [
        PairingCountdown(
          key: const Key('pairing-code-countdown'),
          label: 'Code expires in ',
          textStyle: TextStyle(
            color: tokens.textMuted,
            fontSize: DovahThemeTokens.compactFontSize,
          ),
          remainingStyle: TextStyle(
            color: tokens.textPrimary,
            fontWeight: FontWeight.w700,
          ),
        ),
        SizedBox(height: metrics.codeRowTopGap),
        PairingCodeForm(
          onSubmit: onSubmit,
          errorMessage: errorMessage,
          clearCodeOnError:
              failureOutcome == PairingFailureOutcome.invalid ||
              failureOutcome == PairingFailureOutcome.pacingLimited,
          secondaryActions: const [PairingCancelButton()],
          renotifyAction: const PairingRenotifyButton(),
        ),
        SizedBox(height: metrics.noteTopGap),
        Text(
          'You’ll only need to do this once.',
          style: TextStyle(
            color: tokens.textMuted,
            fontSize: DovahDialogMetrics.noteFontSize,
          ),
        ),
      ],
    );
  }
}
