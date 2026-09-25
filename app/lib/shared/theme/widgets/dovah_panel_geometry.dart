import 'dart:math' as math;
import 'dart:ui';

import 'package:dovahlink_client/shared/constants/enums.dart';

/// Builds the outline every DovahLink shared surface clips and paints to, translating the
/// approved prototype's per-theme `clip-path`/`border-radius` treatment into a Flutter [Path]:
/// a single top-right bevel for [DovahPanelCornerStyle.singleBevel], opposite-corner bevels for
/// [DovahPanelCornerStyle.doubleBevel], and a plain rounded rectangle for
/// [DovahPanelCornerStyle.rounded]. A bevelled style is sharp elsewhere and ignores
/// [cornerRadius], as the prototype's bevelled themes set `border-radius:0`. [cutSize] is clamped to half the shorter side so a bevel
/// cannot self-intersect at small constraint sizes.
Path buildDovahPanelPath(
  Size size, {
  required DovahPanelCornerStyle cornerStyle,
  required double cornerRadius,
  required double cutSize,
}) {
  final double width = size.width;
  final double height = size.height;
  final double clampedCut = cutSize.clamp(0, math.min(width, height) / 2);

  switch (cornerStyle) {
    case DovahPanelCornerStyle.rounded:
      return Path()..addRRect(
        RRect.fromRectAndRadius(
          Offset.zero & size,
          Radius.circular(cornerRadius),
        ),
      );
    case DovahPanelCornerStyle.singleBevel:
      return Path()
        ..moveTo(0, 0)
        ..lineTo(width - clampedCut, 0)
        ..lineTo(width, clampedCut)
        ..lineTo(width, height)
        ..lineTo(0, height)
        ..close();
    case DovahPanelCornerStyle.doubleBevel:
      return Path()
        ..moveTo(0, 0)
        ..lineTo(width - clampedCut, 0)
        ..lineTo(width, clampedCut)
        ..lineTo(width, height)
        ..lineTo(clampedCut, height)
        ..lineTo(0, height - clampedCut)
        ..close();
  }
}
