import 'package:flutter/material.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_loading.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_mark.widget.dart';
import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_state_layout.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_dialog_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_button.widget.dart';

/// A pairing state where the client is working or waiting and the user has nothing to enter:
/// connecting, waiting for the Host to come back, requesting a code (including the moment between
/// authenticating and the request starting), and confirming one. Each
/// shows its own copy with a spinner; only the waiting state, which retries silently until the
/// user leaves, offers a close button.
class PairingProgress extends StatelessWidget {
  /// The phase this state presents; one of the phases that only make the user wait.
  final PairingPhase phase;

  /// The Host's name, shown in the heading of the waiting state.
  final String hostName;

  /// Called when the user closes the waiting state.
  final VoidCallback onClose;

  /// Creates a progress state for [phase].
  const PairingProgress({
    required this.phase,
    required this.hostName,
    required this.onClose,
    super.key,
  });

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final DovahThemeTokens tokens = context.dovahTokens;
    final DovahDialogMetrics metrics = context.dovahDialogMetrics;
    final bool isWaiting = phase == PairingPhase.disconnected;
    final (String heading, String body, String status) = switch (phase) {
      PairingPhase.disconnected => (
        '$hostName is offline',
        'Start Skyrim and DovahLink will reconnect automatically when the game becomes available.',
        'Waiting for Skyrim…',
      ),
      PairingPhase.requestingCode || PairingPhase.unpaired => (
        'Requesting a code',
        'Asking Skyrim to show a pairing code for this device.',
        'Requesting code…',
      ),
      PairingPhase.confirming => (
        'Confirming',
        'Checking the code with Skyrim.',
        'Confirming…',
      ),
      _ => (
        'Connecting',
        'Reaching the DovahLink Host on the selected PC.',
        'Connecting…',
      ),
    };

    return PairingStateLayout(
      mark: PairingMark(
        icon: isWaiting
            ? Icons.radio_button_unchecked
            : Icons.desktop_windows_outlined,
      ),
      heading: heading,
      body: body,
      children: [
        Semantics(
          liveRegion: true,
          container: true,
          child: Row(
            key: const Key('pairing-status'),
            mainAxisSize: MainAxisSize.min,
            children: [
              const ExcludeSemantics(child: PairingLoadingIndicator()),
              const SizedBox(width: DovahDialogMetrics.progressStatusGap),
              Text(
                status,
                style: TextStyle(
                  color: tokens.textMuted,
                  fontSize: DovahThemeTokens.compactFontSize,
                ),
              ),
            ],
          ),
        ),
        if (isWaiting) ...[
          SizedBox(height: metrics.actionsTopGap),
          DovahButton(
            key: const Key('pairing-close-button'),
            label: 'Close',
            variant: DovahButtonVariant.secondary,
            onPressed: onClose,
          ),
        ],
      ],
    );
  }
}
