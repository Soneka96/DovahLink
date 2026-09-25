import 'package:flutter/material.dart';

import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_linear_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_material.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_material_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_radial_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_stripe_layer.dart';

/// The component materials each theme paints its surfaces with, as a [ThemeExtension] installed by
/// every preset. Each material is one [DovahMaterialRole]'s complete recipe, transcribed from the
/// approved prototype's `themes.css`; widgets ask for a role and never inspect a recipe.
///
/// Component texture is separate from canvas atmosphere and from feature artwork: this class owns
/// only the first. A material's outer shadow is empty wherever the prototype clips the component
/// with `clip-path` (Frostbound and Dovah panels, cards, dialogs, and primary buttons), because CSS
/// clips `box-shadow` along with the element. Discrete recipes cannot be interpolated, so a theme
/// transition switches to the next preset's materials at its midpoint.
@immutable
class DovahThemeMaterials extends ThemeExtension<DovahThemeMaterials>
    with Equatable {
  /// A fully transparent stop. [DovahLinearLayer] and its siblings give it the hue of its
  /// neighbor, so fades never darken.
  static const Color _clear = Color(0x00000000);

  /// Frostbound's surface layers, base first: the prototype's `--material` (scratched iron with a
  /// hairline, frost stains, and two fracture lines).
  static const List<DovahMaterialLayer> _frostboundSurfaceLayers = [
    DovahLinearLayer(
      angleDegrees: 145,
      colors: [Color(0xFF11181C), Color(0xFF040708)],
      stops: [0, 1],
    ),
    DovahStripeLayer(
      angleDegrees: 178,
      colors: [
        _clear,
        _clear,
        Color.fromRGBO(225, 235, 238, 0.016),
        _clear,
        _clear,
      ],
      stopsPx: [0, 8, 9, 10, 19],
    ),
    DovahRadialLayer(
      center: Offset(0.78, 0.72),
      colors: [Color.fromRGBO(92, 111, 119, 0.12), _clear],
      stops: [0, 0.34],
    ),
    DovahRadialLayer(
      center: Offset(0.14, 0.08),
      colors: [Color.fromRGBO(207, 231, 240, 0.105), _clear],
      stops: [0, 0.25],
    ),
    DovahLinearLayer(
      angleDegrees: 24,
      colors: [_clear, _clear, Color.fromRGBO(184, 205, 212, 0.055), _clear],
      stops: [0, 0.41, 0.411, 0.4145],
    ),
    DovahLinearLayer(
      angleDegrees: 116,
      colors: [
        _clear,
        _clear,
        Color.fromRGBO(207, 226, 232, 0.12),
        Color.fromRGBO(3, 6, 8, 0.46),
        _clear,
        _clear,
        Color.fromRGBO(207, 226, 232, 0.075),
        Color.fromRGBO(2, 5, 7, 0.5),
        _clear,
      ],
      stops: [0, 0.17, 0.1715, 0.1735, 0.177, 0.62, 0.6212, 0.6232, 0.627],
    ),
  ];

  /// Frostbound's raised and control layers, base first: the prototype's `--material-raised`.
  static const List<DovahMaterialLayer> _frostboundRaisedLayers = [
    DovahLinearLayer(
      angleDegrees: 145,
      colors: [Color(0xFF151E23), Color(0xFF070B0D)],
      stops: [0, 1],
    ),
    DovahStripeLayer(
      angleDegrees: 176,
      colors: [
        _clear,
        _clear,
        Color.fromRGBO(225, 235, 238, 0.017),
        _clear,
        _clear,
      ],
      stopsPx: [0, 9, 10, 11, 21],
    ),
    DovahRadialLayer(
      center: Offset(0.92, 0.08),
      colors: [Color.fromRGBO(158, 202, 218, 0.13), _clear],
      stops: [0, 0.25],
      circular: true,
    ),
    DovahLinearLayer(
      angleDegrees: 123,
      colors: [
        _clear,
        _clear,
        Color.fromRGBO(216, 232, 237, 0.09),
        Color.fromRGBO(3, 6, 8, 0.42),
        _clear,
      ],
      stops: [0, 0.31, 0.3115, 0.3135, 0.317],
    ),
  ];

  /// Dovah's surface layers, base first: the prototype's `--material` (forged steel with engraved
  /// vertical lines, scanlines, and an ember-to-ice side tint).
  static const List<DovahMaterialLayer> _dovahSurfaceLayers = [
    DovahLinearLayer(
      angleDegrees: 145,
      colors: [Color(0xFF11212D), Color(0xFF071018)],
      stops: [0, 1],
    ),
    DovahStripeLayer(
      angleDegrees: 93,
      colors: [
        _clear,
        _clear,
        Color.fromRGBO(116, 189, 232, 0.025),
        _clear,
        _clear,
      ],
      stopsPx: [0, 42, 43, 44, 88],
    ),
    DovahStripeLayer(
      angleDegrees: 0,
      colors: [
        Color.fromRGBO(194, 218, 231, 0.025),
        Color.fromRGBO(194, 218, 231, 0.025),
        _clear,
        _clear,
      ],
      stopsPx: [0, 1, 1, 4],
    ),
    DovahLinearLayer(
      angleDegrees: 90,
      colors: [
        Color.fromRGBO(226, 165, 94, 0.045),
        _clear,
        _clear,
        Color.fromRGBO(116, 189, 232, 0.045),
      ],
      stops: [0, 0.28, 0.7, 1],
    ),
  ];

  /// Dovah's raised and control layers, base first: the prototype's `--material-raised`.
  static const List<DovahMaterialLayer> _dovahRaisedLayers = [
    DovahLinearLayer(
      angleDegrees: 145,
      colors: [Color(0xFF162A38), Color(0xFF09141D)],
      stops: [0, 1],
    ),
    DovahStripeLayer(
      angleDegrees: 0,
      colors: [
        Color.fromRGBO(205, 226, 236, 0.023),
        Color.fromRGBO(205, 226, 236, 0.023),
        _clear,
        _clear,
      ],
      stopsPx: [0, 1, 1, 5],
    ),
    DovahRadialLayer(
      center: Offset(0.84, 0.08),
      colors: [Color.fromRGBO(116, 189, 232, 0.09), _clear],
      stops: [0, 0.27],
      circular: true,
    ),
  ];

  /// Hearth's surface layers, base first: the prototype's `--material` (pressed parchment with
  /// paper fibre and two ink specks).
  static const List<DovahMaterialLayer> _hearthSurfaceLayers = [
    DovahLinearLayer(
      angleDegrees: 145,
      colors: [Color(0xFFF7E6C9), Color(0xFFD9B681)],
      stops: [0, 1],
    ),
    DovahStripeLayer(
      angleDegrees: 6,
      colors: [
        _clear,
        _clear,
        Color.fromRGBO(92, 57, 29, 0.055),
        _clear,
        _clear,
      ],
      stopsPx: [0, 4, 5, 6, 11],
    ),
    DovahRadialLayer(
      center: Offset(0.79, 0.67),
      colors: [
        Color.fromRGBO(112, 69, 31, 0.08),
        Color.fromRGBO(112, 69, 31, 0.08),
        _clear,
      ],
      stops: [0, 1 / 1.6, 1],
      radius: 1.6,
    ),
    DovahRadialLayer(
      center: Offset(0.16, 0.2),
      colors: [
        Color.fromRGBO(91, 55, 27, 0.11),
        Color.fromRGBO(91, 55, 27, 0.11),
        _clear,
      ],
      stops: [0, 1 / 1.5, 1],
      radius: 1.5,
    ),
  ];

  /// Hearth's raised and control layers, base first: the prototype's `--material-raised`.
  static const List<DovahMaterialLayer> _hearthRaisedLayers = [
    DovahLinearLayer(
      angleDegrees: 145,
      colors: [Color(0xFFFAE9CD), Color(0xFFDDB985)],
      stops: [0, 1],
    ),
    DovahStripeLayer(
      angleDegrees: 4,
      colors: [
        Color.fromRGBO(91, 56, 27, 0.055),
        Color.fromRGBO(91, 56, 27, 0.055),
        _clear,
        _clear,
      ],
      stopsPx: [0, 1, 1, 5],
    ),
    DovahRadialLayer(
      center: Offset(0.83, 0.13),
      colors: [Color.fromRGBO(255, 255, 255, 0.3), _clear],
      stops: [0, 0.27],
      circular: true,
    ),
  ];

  /// Frostbound's materials, from the prototype's `body.theme-frostbound` `--material`,
  /// `--material-raised`, `--material-shadow`, `.pc-icon`, and `.primary` rules.
  static const DovahThemeMaterials frostbound = DovahThemeMaterials(
    surface: DovahMaterial(
      layers: _frostboundSurfaceLayers,
      topEdgeHighlight: Color.fromRGBO(220, 235, 240, 0.13),
      bottomEdgeShade: Color.fromRGBO(0, 0, 0, 0.72),
      borderColor: Color(0xFF71808A),
    ),
    raised: DovahMaterial(
      layers: _frostboundRaisedLayers,
      topEdgeHighlight: Color.fromRGBO(220, 235, 240, 0.13),
      bottomEdgeShade: Color.fromRGBO(0, 0, 0, 0.72),
      borderColor: Color(0xFFA9C7D1),
    ),
    control: DovahMaterial(
      layers: _frostboundRaisedLayers,
      borderColor: Color(0xFF71808A),
    ),
    icon: DovahMaterial(
      layers: [
        DovahStripeLayer(
          angleDegrees: 110,
          colors: [
            Color(0xFF10191F),
            Color(0xFF10191F),
            Color(0xFF0A1014),
            Color(0xFF0A1014),
          ],
          stopsPx: [0, 5, 6, 11],
        ),
      ],
      topEdgeHighlight: Color.fromRGBO(225, 240, 245, 0.08),
      borderColor: Color(0xFF657985),
    ),
    primaryAction: DovahMaterial(
      layers: [
        DovahLinearLayer(
          angleDegrees: 135,
          colors: [Color(0xFF263239), Color(0xFF11191D)],
          stops: [0, 1],
        ),
        DovahStripeLayer(
          angleDegrees: 176,
          colors: [
            _clear,
            _clear,
            Color.fromRGBO(225, 235, 238, 0.025),
            _clear,
            _clear,
          ],
          stopsPx: [0, 7, 8, 9, 16],
        ),
        DovahLinearLayer(
          angleDegrees: 118,
          colors: [_clear, _clear, Color.fromRGBO(218, 233, 238, 0.09), _clear],
          stops: [0, 0.28, 0.282, 0.286],
        ),
      ],
      topEdgeHighlight: Color.fromRGBO(255, 255, 255, 0.14),
      borderColor: Color(0xFF7C9099),
    ),
  );

  /// The Dovah preset's materials, from the prototype's `body.theme-dovah` `--material`,
  /// `--material-raised`, `--material-shadow`, `.pc-icon`, and `.primary` rules.
  static const DovahThemeMaterials dovah = DovahThemeMaterials(
    surface: DovahMaterial(
      layers: _dovahSurfaceLayers,
      topEdgeHighlight: Color.fromRGBO(189, 222, 239, 0.07),
      borderColor: Color(0xFF4A6B84),
    ),
    raised: DovahMaterial(
      layers: _dovahRaisedLayers,
      topEdgeHighlight: Color.fromRGBO(189, 222, 239, 0.07),
      borderColor: Color(0xFF8ED6FF),
    ),
    control: DovahMaterial(
      layers: _dovahRaisedLayers,
      borderColor: Color(0xFF4A6B84),
    ),
    icon: DovahMaterial(
      layers: [
        DovahLinearLayer(
          angleDegrees: 145,
          colors: [Color(0xFF1C2F3E), Color(0xFF0B141C)],
          stops: [0, 1],
        ),
        DovahStripeLayer(
          angleDegrees: 0,
          colors: [
            Color.fromRGBO(197, 225, 239, 0.03),
            Color.fromRGBO(197, 225, 239, 0.03),
            _clear,
            _clear,
          ],
          stopsPx: [0, 1, 1, 4],
        ),
      ],
      borderColor: Color.fromRGBO(226, 165, 94, 0.62),
      shadow: [
        BoxShadow(color: Color.fromRGBO(226, 165, 94, 0.1), blurRadius: 18),
      ],
    ),
    primaryAction: DovahMaterial(
      layers: [
        DovahLinearLayer(
          angleDegrees: 135,
          colors: [Color(0xFFF0BD73), Color(0xFFC77D38)],
          stops: [0, 1],
        ),
        DovahStripeLayer(
          angleDegrees: 0,
          colors: [
            Color.fromRGBO(255, 255, 255, 0.07),
            Color.fromRGBO(255, 255, 255, 0.07),
            _clear,
            _clear,
          ],
          stopsPx: [0, 1, 1, 4],
        ),
      ],
    ),
  );

  /// Hearth's materials, from the prototype's `body.theme-hearth` `--material`,
  /// `--material-raised`, `--material-shadow`, `.pc-icon`, and `.primary` rules. Its panels are
  /// not clipped, so its surfaces keep the prototype's soft drop shadow. The surface border is the
  /// panel's `#8d6336`; the prototype's connection card uses the slightly darker `--line2`.
  static const DovahThemeMaterials hearth = DovahThemeMaterials(
    surface: DovahMaterial(
      layers: _hearthSurfaceLayers,
      topEdgeHighlight: Color.fromRGBO(255, 255, 255, 0.72),
      bottomEdgeShade: Color.fromRGBO(103, 65, 29, 0.16),
      borderColor: Color(0xFF8D6336),
      shadow: [
        BoxShadow(
          color: Color.fromRGBO(69, 44, 21, 0.23),
          blurRadius: 26,
          offset: Offset(0, 12),
        ),
      ],
    ),
    raised: DovahMaterial(
      layers: _hearthRaisedLayers,
      topEdgeHighlight: Color.fromRGBO(255, 255, 255, 0.72),
      bottomEdgeShade: Color.fromRGBO(103, 65, 29, 0.16),
      borderColor: Color(0xFF965923),
      shadow: [
        BoxShadow(
          color: Color.fromRGBO(69, 44, 21, 0.23),
          blurRadius: 26,
          offset: Offset(0, 12),
        ),
      ],
    ),
    control: DovahMaterial(
      layers: _hearthRaisedLayers,
      borderColor: Color(0xFF79542F),
    ),
    icon: DovahMaterial(
      layers: [
        DovahLinearLayer(
          angleDegrees: 145,
          colors: [Color(0xFFEAD4AE), Color(0xFFD9B982)],
          stops: [0, 1],
        ),
        DovahRadialLayer(
          center: Offset(0.2, 0.2),
          colors: [
            Color.fromRGBO(105, 67, 31, 0.08),
            Color.fromRGBO(105, 67, 31, 0.08),
            _clear,
            _clear,
          ],
          stops: [0, 0.2, 0.2, 1],
          radius: 5,
          repeating: true,
        ),
      ],
      topEdgeHighlight: Color.fromRGBO(255, 255, 255, 0.68),
      borderColor: Color(0xFF977044),
      shadow: [
        BoxShadow(
          color: Color.fromRGBO(83, 52, 24, 0.11),
          blurRadius: 11,
          offset: Offset(0, 4),
        ),
      ],
    ),
    primaryAction: DovahMaterial(
      layers: [
        DovahLinearLayer(
          angleDegrees: 180,
          colors: [Color(0xFFA96932), Color(0xFF82491E)],
          stops: [0, 1],
        ),
        DovahStripeLayer(
          angleDegrees: 88,
          colors: [
            Color.fromRGBO(69, 37, 15, 0.045),
            Color.fromRGBO(69, 37, 15, 0.045),
            _clear,
            _clear,
          ],
          stopsPx: [0, 1, 1, 5],
        ),
      ],
      topEdgeHighlight: Color.fromRGBO(255, 255, 255, 0.2),
      borderColor: Color.fromRGBO(95, 52, 18, 0.34),
      shadow: [
        BoxShadow(
          color: Color.fromRGBO(92, 52, 20, 0.2),
          blurRadius: 20,
          offset: Offset(0, 8),
        ),
      ],
    ),
  );

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

  /// Creates a complete material set. Every role is required so no theme can be assembled with an
  /// accidentally missing material.
  const DovahThemeMaterials({
    required this.surface,
    required this.raised,
    required this.control,
    required this.icon,
    required this.primaryAction,
  });

  /// Returns the material this theme paints [role] with.
  DovahMaterial forRole(DovahMaterialRole role) => switch (role) {
    DovahMaterialRole.surface => surface,
    DovahMaterialRole.raised => raised,
    DovahMaterialRole.control => control,
    DovahMaterialRole.icon => icon,
    DovahMaterialRole.primaryAction => primaryAction,
  };

  /// Returns a copy with selected materials replaced.
  @override
  DovahThemeMaterials copyWith({
    DovahMaterial? surface,
    DovahMaterial? raised,
    DovahMaterial? control,
    DovahMaterial? icon,
    DovahMaterial? primaryAction,
  }) => DovahThemeMaterials(
    surface: surface ?? this.surface,
    raised: raised ?? this.raised,
    control: control ?? this.control,
    icon: icon ?? this.icon,
    primaryAction: primaryAction ?? this.primaryAction,
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
  List<Object?> get props => [surface, raised, control, icon, primaryAction];
}
