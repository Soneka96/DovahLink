import 'dart:math' as math;

import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/dovah_connection_card_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_connection_accent.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_material_layer.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_layers_painter.dart';

/// Paints a connection card's [DovahConnectionAccent] inside the card's padding box, as the
/// prototype's positioned pseudo-elements sit inside it: the whole-card overlay, Dovah's link line,
/// and the faint diamond outline at the bottom-right corner, then the available edge. The card
/// asks for two passes, one beneath its content and one above it, and the accent decides which
/// pass draws its parts ([DovahConnectionAccent.overContent]); the available edge is an inset
/// shadow of the card's own background, so it is always beneath. The painter never takes hits.
class DovahConnectionAccentPainter extends CustomPainter {
  /// The decoration to paint.
  final DovahConnectionAccent accent;

  /// Whether the card is in its available state, which adds [DovahConnectionAccent.availableEdge].
  final bool available;

  /// Whether Dovah's link line is shown; the prototype hides it at narrow widths.
  final bool showLinkLine;

  /// Whether this pass paints above the card's content instead of beneath it.
  final bool aboveContent;

  /// Creates a painter for one pass of [accent].
  const DovahConnectionAccentPainter({
    required this.accent,
    required this.available,
    required this.showLinkLine,
    required this.aboveContent,
  });

  /// See [CustomPainter.paint].
  @override
  void paint(Canvas canvas, Size size) {
    final Rect inner = (Offset.zero & size).deflate(
      DovahThemeTokens.surfaceBorderWidth,
    );
    if (inner.isEmpty) {
      return;
    }

    canvas.save();
    canvas.clipRect(inner);
    canvas.translate(inner.left, inner.top);
    final Size box = inner.size;

    final Color? edge = accent.availableEdge;
    if (!aboveContent && available && edge != null) {
      canvas.drawRect(
        Rect.fromLTWH(
          box.width - DovahConnectionCardMetrics.availableEdgeWidth,
          0,
          DovahConnectionCardMetrics.availableEdgeWidth,
          box.height,
        ),
        Paint()..color = edge,
      );
    }

    if (accent.overContent == aboveContent) {
      DovahLayersPainter(
        layers: accent.overlayLayers,
        opacity: accent.overlayOpacity,
      ).paint(canvas, box);
      _paintLink(canvas, box);
      _paintCornerOutline(canvas, box);
    }
    canvas.restore();
  }

  /// Paints the link line across the middle of the card, when the accent has one and it is shown.
  void _paintLink(Canvas canvas, Size box) {
    final DovahMaterialLayer? layer = accent.linkLayer;
    final double width =
        box.width -
        DovahConnectionCardMetrics.linkLineLeftInset -
        DovahConnectionCardMetrics.linkLineRightInset;
    if (layer == null || !showLinkLine || width <= 0) {
      return;
    }

    final Size lineSize = Size(
      width,
      DovahConnectionCardMetrics.linkLineHeight,
    );
    canvas.save();
    // The line's top edge sits at half the card's height, as `top:50%` does.
    canvas.translate(
      DovahConnectionCardMetrics.linkLineLeftInset,
      box.height / 2,
    );
    DovahLayersPainter(
      layers: [layer],
      opacity: accent.linkOpacity,
    ).paint(canvas, lineSize);
    canvas.restore();
  }

  /// Paints the diamond outline reaching in from the bottom-right corner, when the accent has one.
  void _paintCornerOutline(Canvas canvas, Size box) {
    final Color? color = accent.cornerOutline;
    if (color == null) {
      return;
    }

    const double side = DovahConnectionCardMetrics.cornerOutlineSize;
    final Offset center = Offset(
      box.width +
          DovahConnectionCardMetrics.cornerOutlineRightOffset -
          side / 2,
      box.height +
          DovahConnectionCardMetrics.cornerOutlineBottomOffset -
          side / 2,
    );
    canvas.save();
    canvas.translate(center.dx, center.dy);
    canvas.rotate(math.pi / 4);
    // A one-pixel CSS border sits inside the box, so the stroke runs half a pixel in from its edge.
    canvas.drawRect(
      Rect.fromCenter(
        center: Offset.zero,
        width: side - DovahThemeTokens.surfaceBorderWidth,
        height: side - DovahThemeTokens.surfaceBorderWidth,
      ),
      Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = DovahThemeTokens.surfaceBorderWidth
        ..color = color,
    );
    canvas.restore();
  }

  /// See [CustomPainter.shouldRepaint].
  @override
  bool shouldRepaint(covariant DovahConnectionAccentPainter oldPainter) =>
      oldPainter.accent != accent ||
      oldPainter.available != available ||
      oldPainter.showLinkLine != showLinkLine ||
      oldPainter.aboveContent != aboveContent;

  /// A decoration is never a hit target.
  @override
  bool? hitTest(Offset position) => false;
}
