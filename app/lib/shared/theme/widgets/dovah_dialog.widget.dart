import 'dart:ui';

import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_panel.widget.dart';

/// A DovahLink themed modal card: a title, a close affordance, and scrollable content, shown
/// with a blurred backdrop (translating the approved prototype's `backdrop-filter: blur`).
/// [show] wires this into Flutter's own dialog route, which already provides barrier dismissal,
/// Escape-to-close, and focus containment -- this widget does not reimplement that behavior.
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
    barrierColor: Colors.transparent,
    builder: (BuildContext dialogContext) => BackdropFilter(
      filter: ImageFilter.blur(sigmaX: 8, sigmaY: 8),
      child: Container(
        alignment: Alignment.center,
        color: Colors.black.withValues(alpha: 0.35),
        padding: const EdgeInsets.all(24),
        child: DovahDialog(title: title, child: child),
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
    return ConstrainedBox(
      constraints: const BoxConstraints(maxWidth: 720, maxHeight: 640),
      child: DovahPanel(
        padding: EdgeInsets.zero,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Padding(
              padding: EdgeInsets.all(19 * tokens.densityScale),
              child: Row(
                children: [
                  Expanded(
                    child: Text(
                      title,
                      style: TextStyle(
                        fontFamily: tokens.displayFontFamily,
                        fontSize: 23,
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
                      minWidth: 48,
                      minHeight: 48,
                    ),
                  ),
                ],
              ),
            ),
            Flexible(
              child: Padding(
                padding: EdgeInsets.all(22 * tokens.densityScale),
                child: SingleChildScrollView(child: child),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
