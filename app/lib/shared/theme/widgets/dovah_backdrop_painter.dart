import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/theme/materials/dovah_backdrop.dart';

/// Paints the color treatment and tint of a [DovahBackdrop] over the blurred page beneath it.
///
/// Flutter has no public color-matrix filter for a backdrop, so the prototype's `saturate()` and
/// `sepia()` are reproduced with blend-mode overlays instead: a mid-gray painted with
/// [BlendMode.saturation] at `1 - saturation` mixes each pixel toward its own gray, which is what
/// `saturate` does, and a sepia tone painted with [BlendMode.color] at the sepia amount shifts hue
/// toward brown. The gray uses the compositing spec's luminosity weights, not CSS's, so the result
/// differs from the CSS filter by a few percent of a channel; that is invisible under the
/// translucent tint that covers it. The painter never takes hits, so the dialog barrier beneath
/// still receives taps.
class DovahBackdropPainter extends CustomPainter {
  /// The sepia tone: what the CSS sepia matrix makes of mid-gray. Its hue and saturation are used;
  /// the page keeps its own brightness.
  static const Color sepiaTone = Color(0xFFAC9977);

  /// The backdrop to paint.
  final DovahBackdrop backdrop;

  /// Creates a painter for [backdrop].
  const DovahBackdropPainter({required this.backdrop});

  /// See [CustomPainter.paint].
  @override
  void paint(Canvas canvas, Size size) {
    final Rect bounds = Offset.zero & size;
    if (backdrop.saturation < 1) {
      canvas.drawRect(
        bounds,
        Paint()
          ..blendMode = BlendMode.saturation
          ..color = Color.fromRGBO(128, 128, 128, 1 - backdrop.saturation),
      );
    }
    if (backdrop.sepia > 0) {
      canvas.drawRect(
        bounds,
        Paint()
          ..blendMode = BlendMode.color
          ..color = sepiaTone.withValues(alpha: backdrop.sepia),
      );
    }
    canvas.drawRect(bounds, Paint()..color = backdrop.tint);
  }

  /// See [CustomPainter.shouldRepaint].
  @override
  bool shouldRepaint(covariant DovahBackdropPainter oldPainter) =>
      oldPainter.backdrop != backdrop;

  /// A scrim is never a hit target: [CustomPaint] would otherwise absorb taps meant for the modal
  /// barrier.
  @override
  bool? hitTest(Offset position) => false;
}
