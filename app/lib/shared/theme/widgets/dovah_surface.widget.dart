import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_material.dart';
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

  /// Overrides the corner treatment for this surface, for a component whose shape its own metrics
  /// fix (for example a leading icon tile). Without one, a [role] that
  /// [DovahMaterialRole.followsThemeOutline] takes the theme's corner treatment, and any other role
  /// is rounded by the theme's `--radius`.
  final DovahPanelCornerStyle? cornerStyle;

  /// Overrides the theme's corner radius for this surface, for a component whose approved radius
  /// differs from the theme's general one (for example a panel or primary button). Only used when
  /// the surface's corner style is [DovahPanelCornerStyle.rounded].
  final double? cornerRadius;

  /// Overrides the theme's bevel cut size for this surface, for a component whose approved bevel
  /// differs from the theme's general one (for example a connection card). Only used when the
  /// surface's corner style is a bevel.
  final double? cornerCutSize;

  /// Paints beneath [child], inside the clipped surface and above the material, or `null` for
  /// nothing (a connection card's decoration).
  final CustomPainter? underlay;

  /// Paints above [child], inside the clipped surface, or `null` for nothing.
  final CustomPainter? overlay;

  /// Replaces the role material's border color, for a component whose prototype rule pins a border
  /// the role's own does not, or `null` to keep the material's.
  final Color? borderColor;

  /// Whether the material's drop shadow is painted; `false` for a component whose prototype rule
  /// sets `box-shadow:none` (a disabled primary button).
  final bool castsShadow;

  /// Creates a themed surface around [child].
  const DovahSurface({
    required this.child,
    this.role = DovahMaterialRole.surface,
    this.padding,
    this.cornerStyle,
    this.cornerRadius,
    this.cornerCutSize,
    this.castsShadow = true,
    this.underlay,
    this.overlay,
    this.borderColor,
    super.key,
  });

  /// See [StatelessWidget.build].
  @override
  Widget build(BuildContext context) {
    final tokens = context.dovahTokens;
    final DovahPanelCornerStyle style =
        cornerStyle ??
        (role.followsThemeOutline
            ? tokens.cornerStyle
            : DovahPanelCornerStyle.rounded);
    final double radius = cornerRadius ?? tokens.cornerRadius;
    final double cut = cornerCutSize ?? tokens.cornerCutSize;

    final DovahMaterial roleMaterial = context.dovahMaterials.forRole(role);
    final DovahMaterial baseMaterial = castsShadow
        ? roleMaterial
        : roleMaterial.withoutShadow();
    final Color? pinnedBorder = borderColor;
    Widget content = Padding(padding: padding ?? EdgeInsets.zero, child: child);
    if (underlay != null || overlay != null) {
      content = CustomPaint(
        painter: underlay,
        foregroundPainter: overlay,
        child: content,
      );
    }

    return CustomPaint(
      painter: DovahMaterialPainter(
        cornerStyle: style,
        cornerRadius: radius,
        cutSize: cut,
        material: pinnedBorder == null
            ? baseMaterial
            : baseMaterial.withBorderColor(pinnedBorder),
      ),
      child: ClipPath(
        clipper: DovahPanelClipper(
          cornerStyle: style,
          cornerRadius: radius,
          cutSize: cut,
        ),
        child: content,
      ),
    );
  }
}
