import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/shared/theme/materials/dovah_color_filter.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_material_layer.dart';

/// The atmosphere a theme paints behind the whole application: the canvas, never a component. It
/// is the prototype's `body:before` (an optional environment image under gradient layers, all seen
/// through one color treatment) plus its `body:after` (a faint haze of fine lines or grain that
/// fades out down the canvas, see [hazeFadeEnd]).
/// Component texture lives in a themed material and feature artwork stays with its feature; this
/// recipe only describes the world the components sit in.
class DovahAtmosphere extends Equatable {
  /// How far down the canvas, as a fraction of its height, the haze fades from fully visible at the
  /// top to gone. Every theme shares it: the prototype's base `body:after` keeps
  /// `mask-image:linear-gradient(to bottom,black,transparent 80%)` under each theme's own haze.
  static const double hazeFadeEnd = 0.8;

  /// The asset path of the environment image, or `null` for a purely gradient atmosphere.
  final String? imageAssetPath;

  /// The treatment applied to the image and [layers] together.
  final DovahColorFilter imageFilter;

  /// The gradient layers painted over the image (or over the base color when there is none), base
  /// first, inside [imageFilter].
  final List<DovahMaterialLayer> layers;

  /// The haze layers painted over everything else, base first, at [hazeOpacity].
  final List<DovahMaterialLayer> hazeLayers;

  /// The opacity of the haze, from `0` to `1`.
  final double hazeOpacity;

  /// Creates an atmosphere; omitted parts default to none.
  const DovahAtmosphere({
    this.imageAssetPath,
    this.imageFilter = DovahColorFilter.none,
    this.layers = const [],
    this.hazeLayers = const [],
    this.hazeOpacity = 1,
  });

  /// The fields that define this atmosphere's value equality.
  @override
  List<Object?> get props => [
    imageAssetPath,
    imageFilter,
    layers,
    hazeLayers,
    hazeOpacity,
  ];
}
