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

  /// Creates the sigil painter.
  const DovahSigilPainter();

  /// See [CustomPainter.paint].
  @override
  void paint(Canvas canvas, Size size) {
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

    canvas.drawPath(upper, Paint()..color = upperColor);
    canvas.drawPath(lower, Paint()..color = lowerColor);
  }

  /// See [CustomPainter.shouldRepaint]. The sigil is constant, so it never repaints.
  @override
  bool shouldRepaint(covariant DovahSigilPainter oldDelegate) => false;
}
