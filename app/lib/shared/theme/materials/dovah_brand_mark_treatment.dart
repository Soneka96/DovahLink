import 'dart:ui';

import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/shared/theme/materials/dovah_color_filter.dart';

/// How a theme dresses the header's DovahLink sigil (the prototype's per-theme `.brand-mark` rules
/// in `themes.css`): a color treatment over the mark, an optional soft glow around it, and an
/// optional translucent disc behind it. Frostbound mutes the mark and lets it glow faintly, Dovah
/// lets it glow more, and Hearth warms it onto a pale disc. The appearance preview's sigil tile has
/// its own treatment in the preview scene.
class DovahBrandMarkTreatment extends Equatable {
  /// The treatment applied to the mark and its [backingColor] together (the prototype's `filter`
  /// functions before any `drop-shadow`).
  final DovahColorFilter filter;

  /// The color of the glow around the mark's silhouette (the prototype's `drop-shadow` color), or
  /// `null` for none. The glow is not touched by [filter], because CSS applies a drop shadow after
  /// the functions before it.
  final Color? glowColor;

  /// The blur radius of the glow, as a CSS blur radius.
  final double glowBlurRadius;

  /// The color of the disc behind the mark, or `null` for none.
  final Color? backingColor;

  /// Creates a treatment; omitted parts default to none.
  const DovahBrandMarkTreatment({
    this.filter = DovahColorFilter.none,
    this.glowColor,
    this.glowBlurRadius = 0,
    this.backingColor,
  });

  /// The fields that define this treatment's value equality.
  @override
  List<Object?> get props => [filter, glowColor, glowBlurRadius, backingColor];
}
