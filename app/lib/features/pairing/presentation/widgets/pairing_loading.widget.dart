import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';

/// The small inline spinner shown while a pairing step is in progress, the approved prototype's
/// `.spinner`.
class PairingLoadingIndicator extends StatelessWidget {
  /// Creates a pairing spinner.
  const PairingLoadingIndicator({super.key});

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final DovahThemeTokens tokens = context.dovahTokens;

    return SizedBox(
      width: DovahThemeTokens.progressIndicatorSize,
      height: DovahThemeTokens.progressIndicatorSize,
      child: CircularProgressIndicator(
        key: const Key('pairing-loading'),
        strokeWidth: DovahThemeTokens.progressIndicatorStrokeWidth,
        color: tokens.accentPrimary,
        backgroundColor: tokens.lineSubtle,
      ),
    );
  }
}
