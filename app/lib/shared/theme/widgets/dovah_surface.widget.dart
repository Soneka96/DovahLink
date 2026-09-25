import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_theme_materials.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_material_painter.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_panel_clipper.dart';

/// The foundational DovahLink material surface: the theme's [DovahMaterialRole] material (layered
/// texture, edge lines, border, and shadow) on the theme's corner treatment, all read from the
/// active theme. Every other shared DovahLink surface (panel, card, button, dialog) is built on
/// this; a caller picks a role and never sees the texture behind it.
class DovahSurface extends StatelessWidget {
  /// The content to render inside the surface, clipped to its corner treatment.
  final Widget child;

  /// Which of the theme's materials paints the surface. See [DovahThemeMaterials.forRole].
  final DovahMaterialRole role;

  /// Padding applied inside the clipped surface, around [child].
  final EdgeInsetsGeometry? padding;

  /// Overrides the theme's corner treatment for this surface, for a component whose shape its own
  /// metrics fix regardless of the theme's panel geometry (for example a leading icon tile).
  final DovahPanelCornerStyle? cornerStyle;

  /// Overrides the theme's corner radius for this surface, for a component whose approved radius
  /// differs from the theme's general one (for example a panel or primary button). Only used when
  /// the surface's corner style is [DovahPanelCornerStyle.rounded].
  final double? cornerRadius;

  /// Overrides the theme's bevel cut size for this surface, for a component whose approved bevel
  /// differs from the theme's general one (for example a connection card). Only used when the
  /// surface's corner style is a bevel.
  final double? cornerCutSize;

  /// Creates a themed surface around [child].
  const DovahSurface({
    required this.child,
    this.role = DovahMaterialRole.surface,
    this.padding,
    this.cornerStyle,
    this.cornerRadius,
    this.cornerCutSize,
    super.key,
  });

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final tokens = context.dovahTokens;
    final DovahPanelCornerStyle style = cornerStyle ?? tokens.cornerStyle;
    final double radius = cornerRadius ?? tokens.cornerRadius;
    final double cut = cornerCutSize ?? tokens.cornerCutSize;

    return CustomPaint(
      painter: DovahMaterialPainter(
        cornerStyle: style,
        cornerRadius: radius,
        cutSize: cut,
        material: context.dovahMaterials.forRole(role),
      ),
      child: ClipPath(
        clipper: DovahPanelClipper(
          cornerStyle: style,
          cornerRadius: radius,
          cutSize: cut,
        ),
        child: Padding(padding: padding ?? EdgeInsets.zero, child: child),
      ),
    );
  }
}
