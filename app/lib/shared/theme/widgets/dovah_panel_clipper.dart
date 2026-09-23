import 'package:flutter/rendering.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_panel_geometry.dart';

/// Clips a DovahLink surface's content to its theme's corner outline, matching the shape
/// [DovahMaterialPainter] paints for that same surface.
class DovahPanelClipper extends CustomClipper<Path> {
  /// Creates a clipper for the given theme geometry.
  const DovahPanelClipper({
    required this.cornerStyle,
    required this.cornerRadius,
    required this.cutSize,
  });

  /// Which corner treatment to clip to.
  final DovahPanelCornerStyle cornerStyle;

  /// The radius used when [cornerStyle] is [DovahPanelCornerStyle.rounded].
  final double cornerRadius;

  /// The bevel cut size used when [cornerStyle] is a bevelled style.
  final double cutSize;

  /// See [CustomClipper.getClip].
  @override
  Path getClip(Size size) => buildDovahPanelPath(
    size,
    cornerStyle: cornerStyle,
    cornerRadius: cornerRadius,
    cutSize: cutSize,
  );

  /// See [CustomClipper.shouldReclip].
  @override
  bool shouldReclip(covariant DovahPanelClipper oldClipper) =>
      oldClipper.cornerStyle != cornerStyle ||
      oldClipper.cornerRadius != cornerRadius ||
      oldClipper.cutSize != cutSize;
}
