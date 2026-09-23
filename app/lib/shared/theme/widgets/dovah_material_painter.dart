import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_panel_geometry.dart';

/// Paints a DovahLink surface's material: an ambient shadow following the theme's corner
/// outline, the gradient fill, and the border stroke. Translates the approved prototype's
/// `box-shadow`/`background-image`/`border` layering onto an arbitrary (including bevelled)
/// path, which [BoxDecoration] cannot clip or shadow on its own. The shadow is a single native
/// [Canvas.drawShadow] ambient shadow derived from the theme's shadow tone rather than the
/// prototype's exact multi-layer CSS shadow (which also relies on `inset` shadows Flutter has no
/// direct equivalent for); this is a disclosed simplification, not a redesign.
class DovahMaterialPainter extends CustomPainter {
  /// Creates a painter for the given theme geometry and material.
  DovahMaterialPainter({
    required this.cornerStyle,
    required this.cornerRadius,
    required this.cutSize,
    required this.gradient,
    required this.borderColor,
    required this.shadow,
  });

  /// Which corner treatment to paint.
  final DovahPanelCornerStyle cornerStyle;

  /// The radius used when [cornerStyle] is [DovahPanelCornerStyle.rounded].
  final double cornerRadius;

  /// The bevel cut size used when [cornerStyle] is a bevelled style.
  final double cutSize;

  /// The fill gradient.
  final Gradient gradient;

  /// The border stroke color.
  final Color borderColor;

  /// The theme's outer shadow. Only the first entry's color and blur radius inform the native
  /// ambient shadow this painter draws; see the class documentation for why.
  final List<BoxShadow> shadow;

  /// See [CustomPainter.paint].
  @override
  void paint(Canvas canvas, Size size) {
    final Path path = buildDovahPanelPath(
      size,
      cornerStyle: cornerStyle,
      cornerRadius: cornerRadius,
      cutSize: cutSize,
    );

    if (shadow.isNotEmpty) {
      final BoxShadow reference = shadow.first;
      canvas.drawShadow(path, reference.color, reference.blurRadius / 2, false);
    }

    final Paint fillPaint = Paint()
      ..shader = gradient.createShader(Offset.zero & size);
    canvas.drawPath(path, fillPaint);

    final Paint borderPaint = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1
      ..color = borderColor;
    canvas.drawPath(path, borderPaint);
  }

  /// See [CustomPainter.shouldRepaint].
  @override
  bool shouldRepaint(covariant DovahMaterialPainter oldPainter) =>
      oldPainter.cornerStyle != cornerStyle ||
      oldPainter.cornerRadius != cornerRadius ||
      oldPainter.cutSize != cutSize ||
      oldPainter.gradient != gradient ||
      oldPainter.borderColor != borderColor ||
      oldPainter.shadow != shadow;
}
