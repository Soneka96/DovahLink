import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_surface.widget.dart';

/// A DovahLink leading icon tile (the approved prototype's `.pc-icon`): a square on the theme's
/// icon material, [rotation] turned and rounded by [cornerRadius], with its glyph held upright. The
/// prototype turns Dovah's tile a quarter of a half turn into a diamond and turns its glyph back
/// (`.pc-icon` `rotate(45deg)`, `.pc-icon svg` `rotate(-45deg)`), so the material's texture turns
/// with the tile while the glyph never does. The turn does not change the tile's layout size, as
/// in CSS, so a turned tile's corners reach beyond [size].
///
/// The tile is decorative: it adds no semantics, and the row it leads carries the meaning.
class DovahIconTile extends StatelessWidget {
  /// Width and height of the tile's layout box.
  final double size;

  /// Corner radius of the tile before it turns; half of [size] makes a circle.
  final double cornerRadius;

  /// How far the tile turns, in radians. The glyph turns by the opposite amount.
  final double rotation;

  /// The glyph drawn at the tile's center, kept upright.
  final Widget child;

  /// Creates a tile of [size] around the glyph [child].
  const DovahIconTile({
    required this.size,
    required this.cornerRadius,
    required this.rotation,
    required this.child,
    super.key,
  });

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: size,
      height: size,
      child: Transform.rotate(
        angle: rotation,
        child: DovahSurface(
          role: DovahMaterialRole.icon,
          cornerRadius: cornerRadius,
          child: Center(
            child: Transform.rotate(angle: -rotation, child: child),
          ),
        ),
      ),
    );
  }
}
