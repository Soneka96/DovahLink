import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_surface.widget.dart';

/// A DovahLink themed button. [DovahButtonVariant.primary] fills with a gradient between the
/// theme's [signal][DovahThemeContext] and ember accent tones (the approved prototype's own
/// signature ember-to-ice pairing) with automatically-contrasted label text; a dedicated
/// per-theme action gradient/text-color pair is not yet part of [DovahThemeTokens], so this is a
/// disclosed simplification rather than a literal transcription of the prototype's distinct
/// primary-button gradients. [DovahButtonVariant.secondary] is a bordered, low-emphasis surface.
class DovahButton extends StatelessWidget {
  /// Creates a themed button.
  const DovahButton({
    required this.label,
    required this.onPressed,
    this.variant = DovahButtonVariant.primary,
    super.key,
  });

  /// The button's visible text.
  final String label;

  /// Called when the button is tapped, or `null` to render it disabled.
  final VoidCallback? onPressed;

  /// The button's visual emphasis.
  final DovahButtonVariant variant;

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final tokens = context.dovahTokens;
    final EdgeInsets padding = EdgeInsets.symmetric(
      vertical: 12 * tokens.densityScale,
      horizontal: 17 * tokens.densityScale,
    );
    final bool enabled = onPressed != null;

    Widget surface;
    TextStyle textStyle;
    if (variant == DovahButtonVariant.primary) {
      final Gradient gradient = LinearGradient(
        begin: Alignment.topLeft,
        end: Alignment.bottomRight,
        colors: [tokens.ember, tokens.signal],
      );
      final Color midpoint = Color.lerp(tokens.ember, tokens.signal, 0.5)!;
      final Color onGradient =
          ThemeData.estimateBrightnessForColor(midpoint) == Brightness.dark
          ? Colors.white
          : Colors.black87;
      textStyle = TextStyle(color: onGradient, fontWeight: FontWeight.w800);
      surface = DovahSurface(
        gradient: gradient,
        padding: padding,
        child: Center(
          widthFactor: 1,
          heightFactor: 1,
          child: Text(label, style: textStyle),
        ),
      );
    } else {
      textStyle = TextStyle(
        color: tokens.textPrimary,
        fontWeight: FontWeight.w700,
      );
      surface = DovahSurface(
        padding: padding,
        child: Center(
          widthFactor: 1,
          heightFactor: 1,
          child: Text(label, style: textStyle),
        ),
      );
    }

    return Opacity(
      opacity: enabled ? 1 : 0.46,
      child: Semantics(
        button: true,
        enabled: enabled,
        label: label,
        child: InkWell(
          onTap: onPressed,
          mouseCursor: enabled
              ? SystemMouseCursors.click
              : SystemMouseCursors.basic,
          child: Builder(
            builder: (BuildContext context) {
              final bool focused = Focus.of(context).hasPrimaryFocus;

              return Container(
                key: focused ? const Key('dovah-button-focus-outline') : null,
                foregroundDecoration: focused
                    ? BoxDecoration(
                        border: Border.all(color: tokens.signal, width: 2),
                        borderRadius: BorderRadius.circular(
                          tokens.cornerRadius,
                        ),
                        boxShadow: <BoxShadow>[
                          BoxShadow(color: tokens.focusRingTint, blurRadius: 8),
                        ],
                      )
                    : null,
                child: ConstrainedBox(
                  constraints: const BoxConstraints(
                    minWidth: 48,
                    minHeight: 48,
                  ),
                  child: Center(child: surface),
                ),
              );
            },
          ),
        ),
      ),
    );
  }
}
