import 'dart:ui';

import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_color_filter.dart';

/// The sigil tile in a theme's appearance-preset preview (the prototype's per-theme
/// `.preset-sigil`): a small filled, outlined tile of a theme-specific [shape] holding the
/// application mark, tinted by [markFilter].
class DovahPreviewSigil extends Equatable {
  /// The tile's fill color.
  final Color fill;

  /// The tile's one-pixel border color.
  final Color border;

  /// The tile's outline.
  final DovahPreviewSigilShape shape;

  /// The color treatment applied to the mark inside the tile.
  final DovahColorFilter markFilter;

  /// Creates a sigil tile; the mark is untreated unless [markFilter] says otherwise.
  const DovahPreviewSigil({
    required this.fill,
    required this.border,
    required this.shape,
    this.markFilter = DovahColorFilter.none,
  });

  /// The fields that define this sigil's value equality.
  @override
  List<Object?> get props => [fill, border, shape, markFilter];
}
