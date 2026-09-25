import 'package:flutter/material.dart';

import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_atmosphere.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_backdrop.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_brand_mark_treatment.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_connection_accent.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_material.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_preset_card_style.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_preview_scene.dart';

/// The visual recipes each theme paints with, as a [ThemeExtension] installed by every preset: one
/// material per [DovahMaterialRole] and the canvas [atmosphere], all transcribed from the approved
/// prototype's `themes.css`. Widgets ask for a role or the atmosphere and never inspect a recipe.
///
/// Component texture, canvas atmosphere, and feature artwork are three separate things. A material
/// textures a component and an atmosphere fills the world behind every component; this class holds
/// those two as distinct recipes and never holds feature artwork, which stays with its feature. A material's outer shadow is empty wherever the prototype clips the component
/// with `clip-path` (Frostbound and Dovah panels, cards, dialogs, and primary buttons), because CSS
/// clips `box-shadow` along with the element. Discrete recipes cannot be interpolated, so a theme
/// transition switches to the next preset's materials at its midpoint.
@immutable
class DovahThemeMaterials extends ThemeExtension<DovahThemeMaterials>
    with Equatable {
  /// The resting panel, card, and dialog material.
  final DovahMaterial surface;

  /// The hovered or emphasized panel and card material.
  final DovahMaterial raised;

  /// The interactive-control material (secondary button, icon button, input).
  final DovahMaterial control;

  /// The leading icon-tile material.
  final DovahMaterial icon;

  /// The primary action button material.
  final DovahMaterial primaryAction;

  /// The canvas atmosphere behind the whole application.
  final DovahAtmosphere atmosphere;

  /// How the header sigil is dressed.
  final DovahBrandMarkTreatment brandMark;

  /// The decoration a connection card wears over its surface material.
  final DovahConnectionAccent connectionAccent;

  /// The treatment behind a modal dialog.
  final DovahBackdrop backdrop;

  /// How the theme dresses its card in the appearance picker.
  final DovahPresetCardStyle presetCard;

  /// How the theme shows itself in the appearance picker.
  final DovahPreviewScene previewScene;

  /// Creates a complete material set. Every role is required so no theme can be assembled with an
  /// accidentally missing material.
  const DovahThemeMaterials({
    required this.surface,
    required this.raised,
    required this.control,
    required this.icon,
    required this.primaryAction,
    required this.atmosphere,
    required this.brandMark,
    required this.connectionAccent,
    required this.backdrop,
    required this.presetCard,
    required this.previewScene,
  });

  /// Returns the material this theme paints [role] with.
  DovahMaterial forRole(DovahMaterialRole role) => switch (role) {
    DovahMaterialRole.surface => surface,
    DovahMaterialRole.raised => raised,
    DovahMaterialRole.control => control,
    DovahMaterialRole.icon => icon,
    DovahMaterialRole.primaryAction => primaryAction,
  };

  /// Returns a copy with selected visual recipes replaced.
  @override
  DovahThemeMaterials copyWith({
    DovahMaterial? surface,
    DovahMaterial? raised,
    DovahMaterial? control,
    DovahMaterial? icon,
    DovahMaterial? primaryAction,
    DovahAtmosphere? atmosphere,
    DovahBrandMarkTreatment? brandMark,
    DovahConnectionAccent? connectionAccent,
    DovahBackdrop? backdrop,
    DovahPresetCardStyle? presetCard,
    DovahPreviewScene? previewScene,
  }) => DovahThemeMaterials(
    surface: surface ?? this.surface,
    raised: raised ?? this.raised,
    control: control ?? this.control,
    icon: icon ?? this.icon,
    primaryAction: primaryAction ?? this.primaryAction,
    atmosphere: atmosphere ?? this.atmosphere,
    brandMark: brandMark ?? this.brandMark,
    connectionAccent: connectionAccent ?? this.connectionAccent,
    backdrop: backdrop ?? this.backdrop,
    presetCard: presetCard ?? this.presetCard,
    previewScene: previewScene ?? this.previewScene,
  );

  /// Switches to [other]'s materials once [t] passes the midpoint; a layered recipe has no
  /// meaningful halfway point.
  @override
  DovahThemeMaterials lerp(
    ThemeExtension<DovahThemeMaterials>? other,
    double t,
  ) {
    if (other is! DovahThemeMaterials) {
      return this;
    }
    return t < 0.5 ? this : other;
  }

  /// See [Equatable.props].
  @override
  List<Object?> get props => [
    surface,
    raised,
    control,
    icon,
    primaryAction,
    atmosphere,
    brandMark,
    connectionAccent,
    backdrop,
    presetCard,
    previewScene,
  ];
}
