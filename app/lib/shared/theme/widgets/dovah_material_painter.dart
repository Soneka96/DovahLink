import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_material.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_material_layer.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_panel_geometry.dart';

/// Paints a DovahLink surface's [DovahMaterial] on the theme's corner outline (including bevelled
/// outlines, which [BoxDecoration] cannot clip or shadow): the drop shadow, then each texture layer
/// clipped to the outline, the one-pixel top and bottom edge lines, and the border. The border is
/// painted inside the outline, as a CSS border is, so it never grows the surface. On a bevelled
/// outline it runs along the box's straight edges only, as under CSS `clip-path`, and the bevel
/// itself has no border line.
///
/// Layers are gradient fills, so painting needs no `saveLayer` and no image processing. The shadow
/// uses the shadow's offset and blur; its spread is not applied, because no prototype material has
/// one.
class DovahMaterialPainter extends CustomPainter {
  /// Which corner treatment to paint.
  final DovahPanelCornerStyle cornerStyle;

  /// The radius used when [cornerStyle] is [DovahPanelCornerStyle.rounded].
  final double cornerRadius;

  /// The bevel cut size used when [cornerStyle] is a bevelled style.
  final double cutSize;

  /// The material to paint.
  final DovahMaterial material;

  /// Creates a painter for the given theme geometry and material.
  const DovahMaterialPainter({
    required this.cornerStyle,
    required this.cornerRadius,
    required this.cutSize,
    required this.material,
  });

  /// See [CustomPainter.paint].
  @override
  void paint(Canvas canvas, Size size) {
    if (size.isEmpty) {
      return;
    }

    final Path path = buildDovahPanelPath(
      size,
      cornerStyle: cornerStyle,
      cornerRadius: cornerRadius,
      cutSize: cutSize,
    );

    for (final BoxShadow shadow in material.shadow) {
      canvas.drawPath(path.shift(shadow.offset), shadow.toPaint());
    }

    canvas.save();
    canvas.clipPath(path, doAntiAlias: true);

    final Rect bounds = Offset.zero & size;
    for (final DovahMaterialLayer layer in material.layers) {
      canvas.drawRect(bounds, Paint()..shader = layer.createShader(size));
    }

    final Color? borderColor = material.borderColor;
    final double borderWidth = borderColor == null
        ? 0
        : DovahThemeTokens.surfaceBorderWidth;
    final Color? topEdge = material.topEdgeHighlight;
    if (topEdge != null) {
      canvas.drawRect(
        Rect.fromLTWH(0, borderWidth, size.width, 1),
        Paint()..color = topEdge,
      );
    }
    final Color? bottomEdge = material.bottomEdgeShade;
    if (bottomEdge != null) {
      canvas.drawRect(
        Rect.fromLTWH(0, size.height - borderWidth - 1, size.width, 1),
        Paint()..color = bottomEdge,
      );
    }

    if (borderColor != null) {
      // Stroking twice the border width and clipping to the outline leaves exactly the inner half,
      // which is where a CSS border sits. A bevelled outline is a `clip-path` over an ordinary
      // rectangular border, so the border follows the box and the bevel cuts it off instead of
      // drawing a line along the diagonal.
      final Path borderOutline = cornerStyle == DovahPanelCornerStyle.rounded
          ? path
          : (Path()..addRect(bounds));
      canvas.drawPath(
        borderOutline,
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = borderWidth * 2
          ..color = borderColor,
      );
    }
    canvas.restore();
  }

  /// See [CustomPainter.shouldRepaint].
  @override
  bool shouldRepaint(covariant DovahMaterialPainter oldPainter) =>
      oldPainter.cornerStyle != cornerStyle ||
      oldPainter.cornerRadius != cornerRadius ||
      oldPainter.cutSize != cutSize ||
      oldPainter.material != material;
}
