import 'dart:ui';

import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/shared/theme/materials/dovah_color_filter.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_material_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_preview_sigil.dart';

/// How a theme shows itself in the appearance picker (the prototype's per-theme `.preset-scene`):
/// the theme's own scene image under gradient layers, all seen through one color treatment, with a
/// sigil tile and three accent bars in the scene's colors. It references the same asset constants
/// and color values as the theme's canvas atmosphere and materials, so a preview stays a picture of
/// the real theme instead of a separate miniature.
class DovahPreviewScene extends Equatable {
  /// The asset path of the scene image.
  final String imageAssetPath;

  /// The treatment applied to the image and [layers] together.
  final DovahColorFilter imageFilter;

  /// The gradient layers painted over the image, base first, inside [imageFilter]. They include
  /// the fade into the card color along the scene's floor.
  final List<DovahMaterialLayer> layers;

  /// The sigil tile centered in the scene.
  final DovahPreviewSigil sigil;

  /// The fill of each accent bar along the scene's floor.
  final DovahMaterialLayer barFill;

  /// The color of the two-pixel edge on the left of each accent bar, or `null` for none.
  final Color? barEdgeColor;

  /// The corner radius of each accent bar.
  final double barCornerRadius;

  /// Creates a preview scene from its parts.
  const DovahPreviewScene({
    required this.imageAssetPath,
    required this.layers,
    required this.sigil,
    required this.barFill,
    this.imageFilter = DovahColorFilter.none,
    this.barEdgeColor,
    this.barCornerRadius = 0,
  });

  /// The fields that define this scene's value equality.
  @override
  List<Object?> get props => [
    imageAssetPath,
    imageFilter,
    layers,
    sigil,
    barFill,
    barEdgeColor,
    barCornerRadius,
  ];
}
