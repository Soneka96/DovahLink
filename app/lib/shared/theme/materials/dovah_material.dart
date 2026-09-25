import 'package:flutter/painting.dart';

import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/shared/theme/materials/dovah_material_layer.dart';

/// A themed component material: the layered texture, edge lines, border, and drop shadow a surface
/// paints in one theme. A material is a semantic recipe, not a set of texture knobs; the theme
/// chooses which one a component role receives and no feature widget inspects its layers.
class DovahMaterial extends Equatable {
  /// The texture layers, painted in list order: the first entry is the base fill and the last is
  /// the top-most texture. This is the reverse of CSS `background`, which lists the top layer
  /// first.
  final List<DovahMaterialLayer> layers;

  /// The one-pixel highlight along the inside of the top edge (the prototype's
  /// `inset 0 1px <color>`), or `null` for none.
  final Color? topEdgeHighlight;

  /// The one-pixel shade along the inside of the bottom edge (the prototype's
  /// `inset 0 -1px <color>`), or `null` for none.
  final Color? bottomEdgeShade;

  /// The outer border color, or `null` for no border.
  final Color? borderColor;

  /// The outer drop shadow. A clipped or bevelled surface casts none in the prototype, because
  /// CSS `clip-path` also clips `box-shadow`.
  final List<BoxShadow> shadow;

  /// Creates a material from [layers] and its optional edges, border, and [shadow].
  const DovahMaterial({
    required this.layers,
    this.topEdgeHighlight,
    this.bottomEdgeShade,
    this.borderColor,
    this.shadow = const [],
  });

  /// Returns this material with no drop shadow, for a component whose prototype rule sets
  /// `box-shadow:none` (a disabled primary button).
  DovahMaterial withoutShadow() => DovahMaterial(
    layers: layers,
    topEdgeHighlight: topEdgeHighlight,
    bottomEdgeShade: bottomEdgeShade,
    borderColor: borderColor,
  );

  /// See [Equatable.props].
  @override
  List<Object?> get props => [
    layers,
    topEdgeHighlight,
    bottomEdgeShade,
    borderColor,
    shadow,
  ];
}
