import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';

/// The inline message slot under a pairing state's content, the approved prototype's `.error`. It
/// reserves its height while empty so a message appearing does not shift the layout, and is a
/// live region so a new message is announced.
class PairingMessage extends StatelessWidget {
  /// The message to show, or `null` to leave the slot empty.
  final String? message;

  /// Creates a message slot showing [message].
  const PairingMessage({this.message, super.key});

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final DovahThemeTokens tokens = context.dovahTokens;

    return ConstrainedBox(
      constraints: const BoxConstraints(
        minHeight: DovahThemeTokens.formErrorMinHeight,
      ),
      child: Semantics(
        liveRegion: true,
        child: Text(
          message ?? '',
          textAlign: TextAlign.center,
          style: TextStyle(
            color: tokens.danger,
            fontSize: DovahThemeTokens.formErrorFontSize,
          ),
        ),
      ),
    );
  }
}
