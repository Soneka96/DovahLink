import 'dart:ui';

import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/shared/theme/materials/dovah_material.dart';

/// How a theme dresses its own card in the appearance picker (the prototype's per-theme
/// `.preset-frostbound`, `.preset-dovah`, and `.preset-hearth` rules): a card material of its own,
/// simpler than the theme's panel material, its three text tones, and its selected badge. A card is
/// drawn in the theme it shows, whatever theme is active, so these values belong to the shown theme.
/// The card's outline is geometry and lives in `DovahAppearanceThemeMetrics`; the preview picture is
/// the theme's `DovahPreviewScene`.
class DovahPresetCardStyle extends Equatable {
  /// The card's body material: layers and border, with no edge lines and no shadow.
  final DovahMaterial material;

  /// The color of the card's title (the prototype's card `color`).
  final Color titleColor;

  /// The color of the card's summary line (the prototype's `.preset-copy>span`).
  final Color summaryColor;

  /// The color of the card's materials line (the prototype's `.preset-copy>small`).
  final Color detailColor;

  /// The fill of the selected badge (the prototype's `.preset-check` `background`).
  final Color badgeFill;

  /// The color of the check in the selected badge (the prototype's `.preset-check` `color`).
  final Color badgeForeground;

  /// Creates a card style from its parts.
  const DovahPresetCardStyle({
    required this.material,
    required this.titleColor,
    required this.summaryColor,
    required this.detailColor,
    required this.badgeFill,
    required this.badgeForeground,
  });

  /// The fields that define this style's value equality.
  @override
  List<Object?> get props => [
    material,
    titleColor,
    summaryColor,
    detailColor,
    badgeFill,
    badgeForeground,
  ];
}
