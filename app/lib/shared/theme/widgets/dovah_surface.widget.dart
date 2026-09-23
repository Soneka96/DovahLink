import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_material_painter.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_panel_clipper.dart';

/// The foundational DovahLink material surface: a themed gradient fill, border, shadow, and
/// corner treatment, all read from the active [DovahLink theme's tokens][DovahThemeContext].
/// Every other shared DovahLink surface (panel, card, button, dialog) is built on this.
class DovahSurface extends StatelessWidget {
  /// Creates a themed surface around [child].
  const DovahSurface({
    required this.child,
    this.raised = false,
    this.padding,
    this.gradient,
    super.key,
  });

  /// The content to render inside the surface, clipped to its corner treatment.
  final Widget child;

  /// Whether to use the theme's raised material (hover/emphasis) instead of its resting
  /// material. Ignored when [gradient] is given.
  final bool raised;

  /// Padding applied inside the clipped surface, around [child].
  final EdgeInsetsGeometry? padding;

  /// Overrides the theme's material gradient with a specific fill (for example a themed accent
  /// gradient for a call-to-action), while still using the theme's corner treatment, border, and
  /// shadow.
  final Gradient? gradient;

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final tokens = context.dovahTokens;
    final Gradient fill =
        gradient ??
        (raised ? tokens.materialRaisedGradient : tokens.materialGradient);

    if (tokens.cornerStyle == DovahPanelCornerStyle.rounded) {
      return Container(
        padding: padding,
        decoration: BoxDecoration(
          gradient: fill,
          borderRadius: BorderRadius.circular(tokens.cornerRadius),
          border: Border.all(color: tokens.lineStrong),
          boxShadow: tokens.panelShadow,
        ),
        child: child,
      );
    }

    return CustomPaint(
      painter: DovahMaterialPainter(
        cornerStyle: tokens.cornerStyle,
        cornerRadius: tokens.cornerRadius,
        cutSize: tokens.cornerCutSize,
        gradient: fill,
        borderColor: tokens.lineStrong,
        shadow: tokens.panelShadow,
      ),
      child: ClipPath(
        clipper: DovahPanelClipper(
          cornerStyle: tokens.cornerStyle,
          cornerRadius: tokens.cornerRadius,
          cutSize: tokens.cornerCutSize,
        ),
        child: Padding(padding: padding ?? EdgeInsets.zero, child: child),
      ),
    );
  }
}
