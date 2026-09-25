import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/dovah_root_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';

/// The connections screen's footer note about reconnecting trusted PCs.
class ConnectionsFooter extends StatelessWidget {
  /// Creates the connections footer.
  const ConnectionsFooter({super.key});

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    if (!context.dovahRootMetrics.showFooter) {
      return const SizedBox.shrink();
    }

    return Padding(
      padding: const EdgeInsets.only(top: DovahRootMetrics.footerTopGap),
      child: Center(
        child: Text(
          'Trusted PCs reconnect automatically when Skyrim becomes available.',
          textAlign: TextAlign.center,
          style: TextStyle(
            color: context.dovahTokens.textFaint,
            fontSize: DovahRootMetrics.footerFontSize,
            height: DovahThemeTokens.bodyLineHeight,
          ),
        ),
      ),
    );
  }
}
