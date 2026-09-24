import 'dart:ui';

import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_panel.widget.dart';

/// A DovahLink themed modal card: a title, a close affordance, and scrollable content, shown
/// with a blurred backdrop (translating the approved prototype's `backdrop-filter: blur`).
/// [show] wires this into Flutter's own dialog route, which already provides barrier dismissal,
/// Escape-to-close, and focus containment -- this widget does not reimplement that behavior. The
/// card supplies its own transparent [Material], which a dialog route does not, so ink-based
/// content such as an [InkWell] works inside it.
class DovahDialog extends StatelessWidget {
  /// Creates a themed dialog card.
  const DovahDialog({
    required this.title,
    required this.child,
    this.onClose,
    super.key,
  });

  /// Shows [child] as a [DovahDialog] with the given [title], behind a blurred backdrop.
  static Future<T?> show<T>(
    BuildContext context, {
    required String title,
    required Widget child,
  }) => showDialog<T>(
    context: context,
    barrierColor: DovahThemeTokens.dialogBackdropColor.withValues(
      alpha: DovahThemeTokens.dialogBackdropOpacity,
    ),
    builder: (BuildContext dialogContext) => BackdropFilter(
      filter: ImageFilter.blur(
        sigmaX: DovahThemeTokens.dialogBackdropBlurSigma,
        sigmaY: DovahThemeTokens.dialogBackdropBlurSigma,
      ),
      child: Padding(
        padding: const EdgeInsets.all(DovahThemeTokens.spacing24),
        child: Center(
          child: DovahDialog(title: title, child: child),
        ),
      ),
    ),
  );

  /// The dialog's title.
  final String title;

  /// The dialog's scrollable content.
  final Widget child;

  /// Called when the close affordance is tapped, or `null` to pop the current route.
  final VoidCallback? onClose;

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final tokens = context.dovahTokens;
    return Material(
      type: MaterialType.transparency,
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 720, maxHeight: 640),
        child: DovahPanel(
          padding: EdgeInsets.zero,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Padding(
                padding: EdgeInsets.all(
                  DovahThemeTokens.spacing19 * tokens.densityScale,
                ),
                child: Row(
                  children: [
                    Expanded(
                      child: Text(
                        title,
                        style: TextStyle(
                          fontFamily: tokens.displayFontFamily,
                          fontSize: DovahThemeTokens.dialogTitleFontSize,
                          color: tokens.textPrimary,
                        ),
                      ),
                    ),
                    IconButton(
                      icon: Icon(Icons.close, color: tokens.textMuted),
                      onPressed:
                          onClose ?? () => Navigator.of(context).maybePop(),
                      tooltip: 'Close',
                      constraints: const BoxConstraints(
                        minWidth: DovahThemeTokens.minimumTapTargetSize,
                        minHeight: DovahThemeTokens.minimumTapTargetSize,
                      ),
                    ),
                  ],
                ),
              ),
              Flexible(
                child: Padding(
                  padding: EdgeInsets.all(
                    DovahThemeTokens.spacing22 * tokens.densityScale,
                  ),
                  child: SingleChildScrollView(child: child),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
