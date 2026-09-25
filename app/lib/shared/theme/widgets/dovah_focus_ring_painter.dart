import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/dovah_control_metrics.dart';

/// Paints the keyboard-focus outline the prototype draws around every focused button and input
/// (`button:focus-visible,input:focus-visible{outline:2px solid var(--accent);outline-offset:3px}`):
/// a [DovahControlMetrics.focusOutlineWidth] line that sits [DovahControlMetrics.focusOutlineOffset]
/// outside the box, following its corner radius. It paints outside its own bounds, above the
/// content, and never takes hits.
class DovahFocusRingPainter extends CustomPainter {
  /// The outline color; the theme's accent.
  final Color color;

  /// The corner radius of the box the outline surrounds; `0` for a square or bevelled box.
  final double cornerRadius;

  /// Creates a painter for an outline of [color] around a box of [cornerRadius].
  const DovahFocusRingPainter({
    required this.color,
    required this.cornerRadius,
  });

  /// See [CustomPainter.paint].
  @override
  void paint(Canvas canvas, Size size) {
    // A stroke is centered on its path, so the path runs through the middle of the outline.
    const double inflate =
        DovahControlMetrics.focusOutlineOffset +
        DovahControlMetrics.focusOutlineWidth / 2;
    final Rect outline = (Offset.zero & size).inflate(inflate);

    canvas.drawRRect(
      cornerRadius > 0
          ? RRect.fromRectAndRadius(
              outline,
              Radius.circular(cornerRadius + inflate),
            )
          : RRect.fromRectAndRadius(outline, Radius.zero),
      Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = DovahControlMetrics.focusOutlineWidth
        ..color = color,
    );
  }

  /// See [CustomPainter.shouldRepaint].
  @override
  bool shouldRepaint(covariant DovahFocusRingPainter oldPainter) =>
      oldPainter.color != color || oldPainter.cornerRadius != cornerRadius;

  /// An outline is never a hit target.
  @override
  bool? hitTest(Offset position) => false;
}
