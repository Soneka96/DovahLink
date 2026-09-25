import 'package:flutter/material.dart';

/// Paints the DovahLink Linked Sigil -- the two interlocking arrows of the approved
/// `dovahlink-sigil.svg` -- scaled from its 100x100 view box to the canvas. It reproduces the
/// SVG paths and brand colors directly, so the app needs no SVG-rendering dependency; the
/// colors belong to the sigil asset, not to the theme tokens.
class DovahSigilPainter extends CustomPainter {
  /// The side length of the sigil's view box.
  static const double viewBoxSize = 100;

  /// The fill of the sigil's upper-left arrow.
  static const Color upperColor = Color(0xFF315F92);

  /// The fill of the sigil's lower-right arrow.
  static const Color lowerColor = Color(0xFF74BDE8);

  /// The color of the glow this painter draws instead of the sigil, or `null` when it paints the
  /// sigil itself.
  final Color? glowColor;

  /// The blur radius of the glow, as a CSS blur radius. Only used together with [glowColor].
  final double glowBlurRadius;

  /// Creates the sigil painter.
  const DovahSigilPainter() : glowColor = null, glowBlurRadius = 0;

  /// Creates a painter that draws only the soft glow around the sigil's silhouette (the
  /// prototype's `drop-shadow(0 0 <blur> <color>)`), so it can sit beneath a treated sigil without
  /// being treated with it.
  const DovahSigilPainter.glow({
    required Color color,
    required double blurRadius,
  }) : glowColor = color,
       glowBlurRadius = blurRadius;

  /// See [CustomPainter.paint].
  @override
  void paint(Canvas canvas, Size size) {
    if (size.isEmpty) {
      return;
    }

    canvas.scale(size.width / viewBoxSize, size.height / viewBoxSize);

    final Path upper = Path()
      ..moveTo(10, 30)
      ..lineTo(34, 6)
      ..relativeLineTo(26, 26)
      ..relativeLineTo(-10, 10)
      ..relativeLineTo(-16, -16)
      ..relativeLineTo(-10, 10)
      ..relativeLineTo(0, 28)
      ..relativeLineTo(16, 16)
      ..relativeLineTo(0, 14)
      ..lineTo(10, 70)
      ..close();
    final Path lower = Path()
      ..moveTo(90, 70)
      ..lineTo(66, 94)
      ..lineTo(40, 68)
      ..relativeLineTo(10, -10)
      ..relativeLineTo(16, 16)
      ..relativeLineTo(10, -10)
      ..lineTo(76, 36)
      ..lineTo(60, 20)
      ..lineTo(60, 6)
      ..relativeLineTo(30, 24)
      ..close();

    final Color? glow = glowColor;
    if (glow != null) {
      // A blur sigma is measured in the scaled canvas, so undo the scale to keep the CSS radius.
      final Paint glowPaint = Paint()
        ..color = glow
        ..maskFilter = MaskFilter.blur(
          BlurStyle.normal,
          glowBlurRadius / 2 * viewBoxSize / size.width,
        );
      canvas.drawPath(upper, glowPaint);
      canvas.drawPath(lower, glowPaint);
      return;
    }

    canvas.drawPath(upper, Paint()..color = upperColor);
    canvas.drawPath(lower, Paint()..color = lowerColor);
  }

  /// See [CustomPainter.shouldRepaint]. The sigil is constant; only a glow can change.
  @override
  bool shouldRepaint(covariant DovahSigilPainter oldDelegate) =>
      oldDelegate.glowColor != glowColor ||
      oldDelegate.glowBlurRadius != glowBlurRadius;
}
