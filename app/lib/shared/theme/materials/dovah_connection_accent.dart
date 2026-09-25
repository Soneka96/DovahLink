import 'dart:ui';

import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/shared/theme/materials/dovah_material_layer.dart';

/// The per-theme decoration a connection card wears on top of its surface material (the
/// prototype's `.connection:before`, `.connection:after`, and `.connection.available` rules in
/// `themes.css`): Frostbound's fracture lines and available edge, Dovah's engraved link line, both
/// bevelled themes' faint corner outline, and Hearth's darker resting border. Each part is optional
/// and Hearth has none of the drawn ones, so [none] is a valid recipe. Where each part sits on the
/// card is geometry and lives in `DovahConnectionCardMetrics`.
class DovahConnectionAccent extends Equatable {
  /// A card with no decoration.
  static const DovahConnectionAccent none = DovahConnectionAccent();

  /// Layers that fill the whole card, base first (Frostbound's fracture lines).
  final List<DovahMaterialLayer> overlayLayers;

  /// The opacity [overlayLayers] are painted at, from `0` to `1`.
  final double overlayOpacity;

  /// The layer of the one-pixel line across the card's middle (Dovah's ember-to-ice link), or
  /// `null` for none.
  final DovahMaterialLayer? linkLayer;

  /// The opacity [linkLayer] is painted at, from `0` to `1`.
  final double linkOpacity;

  /// The color of the faint diamond outline peeking in from the card's bottom-right corner, or
  /// `null` for none.
  final Color? cornerOutline;

  /// The color of the two-pixel edge along the right of an available card, or `null` for none.
  final Color? availableEdge;

  /// The card's resting border, when it differs from the surface material's, or `null` to keep it.
  final Color? restingBorder;

  /// Whether the drawn parts sit above the card's content instead of beneath it. Frostbound's
  /// positioned pseudo-elements paint over its static content; Dovah lifts its content above them.
  final bool overContent;

  /// Creates a decoration; omitted parts default to none.
  const DovahConnectionAccent({
    this.overlayLayers = const [],
    this.overlayOpacity = 1,
    this.linkLayer,
    this.linkOpacity = 1,
    this.cornerOutline,
    this.availableEdge,
    this.restingBorder,
    this.overContent = false,
  });

  /// Whether the accent draws nothing, so a card can skip its painters.
  bool get drawsNothing =>
      overlayLayers.isEmpty &&
      linkLayer == null &&
      cornerOutline == null &&
      availableEdge == null;

  /// The fields that define this accent's value equality.
  @override
  List<Object?> get props => [
    overlayLayers,
    overlayOpacity,
    linkLayer,
    linkOpacity,
    cornerOutline,
    availableEdge,
    restingBorder,
    overContent,
  ];
}
