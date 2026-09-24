import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/dovah_control_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_surface.widget.dart';

/// A DovahLink themed icon-only button on the theme's raised material (the approved prototype's
/// `.icon-btn`). It takes a label, an icon, and a callback as props; the label is both its tooltip
/// and its single screen-reader label.
class DovahIconButton extends StatelessWidget {
  /// The icon drawn on the button.
  final IconData icon;

  /// The button's tooltip and accessible label.
  final String label;

  /// Called when the button is tapped, or `null` to render it disabled.
  final VoidCallback? onPressed;

  /// Creates a themed icon-only button.
  const DovahIconButton({
    required this.icon,
    required this.label,
    required this.onPressed,
    super.key,
  });

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final DovahThemeTokens tokens = context.dovahTokens;
    final bool enabled = onPressed != null;

    return Opacity(
      opacity: enabled ? 1 : DovahControlMetrics.disabledControlOpacity,
      child: Semantics(
        key: const Key('dovah-icon-button-semantics'),
        excludeSemantics: true,
        button: true,
        enabled: enabled,
        label: label,
        onTap: onPressed,
        child: Tooltip(
          message: label,
          child: InkWell(
            onTap: onPressed,
            mouseCursor: enabled
                ? SystemMouseCursors.click
                : SystemMouseCursors.basic,
            child: Builder(
              builder: (BuildContext context) {
                final bool focused = Focus.of(context).hasPrimaryFocus;

                return Container(
                  key: focused
                      ? const Key('dovah-icon-button-focus-outline')
                      : null,
                  foregroundDecoration: focused
                      ? BoxDecoration(
                          border: Border.all(
                            color: tokens.signal,
                            width: DovahControlMetrics.focusOutlineWidth,
                          ),
                          borderRadius: BorderRadius.circular(
                            tokens.cornerRadius,
                          ),
                          boxShadow: <BoxShadow>[
                            BoxShadow(
                              color: tokens.soft,
                              blurRadius:
                                  DovahControlMetrics.focusGlowBlurRadius,
                            ),
                          ],
                        )
                      : null,
                  child: ConstrainedBox(
                    constraints: const BoxConstraints(
                      minWidth: DovahControlMetrics.minimumTapTargetSize,
                      minHeight: DovahControlMetrics.minimumTapTargetSize,
                    ),
                    child: Center(
                      child: SizedBox(
                        width: DovahControlMetrics.iconButtonSize,
                        height: DovahControlMetrics.iconButtonSize,
                        child: DovahSurface(
                          raised: true,
                          child: Center(
                            child: Icon(
                              icon,
                              size: DovahControlMetrics.iconButtonIconSize,
                              color: tokens.textPrimary,
                            ),
                          ),
                        ),
                      ),
                    ),
                  ),
                );
              },
            ),
          ),
        ),
      ),
    );
  }
}
