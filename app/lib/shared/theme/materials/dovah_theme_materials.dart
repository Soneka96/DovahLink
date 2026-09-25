import 'package:flutter/material.dart';

import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_atmosphere.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_backdrop.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_brand_mark_treatment.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_color_filter.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_connection_accent.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_linear_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_material.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_material_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_preset_card_style.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_preview_scene.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_preview_sigil.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_radial_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_stripe_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_tile_layer.dart';

/// The visual recipes each theme paints with, as a [ThemeExtension] installed by every preset: one
/// material per [DovahMaterialRole], the canvas [atmosphere], the [brandMark] treatment, the
/// [connectionAccent] of a connection card, the dialog [backdrop], and the appearance-picker
/// [presetCard] and [previewScene], all transcribed from the approved prototype's `themes.css`. Widgets ask for a
/// role or the atmosphere and never inspect a recipe.
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
      circular: true,
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
      circular: true,
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
    atmosphere: DovahAtmosphere(
      imageAssetPath: frostboundEnvironmentAsset,
      imageFilter: DovahColorFilter(
        grayscale: 0.34,
        saturate: 0.48,
        contrast: 1.16,
      ),
      layers: [
        DovahLinearLayer(
          angleDegrees: 180,
          colors: [Color.fromRGBO(1, 3, 4, 0.7), Color.fromRGBO(1, 3, 4, 0.84)],
          stops: [0, 1],
        ),
      ],
      hazeLayers: [
        DovahLinearLayer(
          angleDegrees: 24,
          colors: [
            _clear,
            _clear,
            Color.fromRGBO(214, 230, 235, 0.032),
            _clear,
          ],
          stops: [0, 0.72, 0.721, 0.7235],
        ),
        DovahLinearLayer(
          angleDegrees: 112,
          colors: [
            _clear,
            _clear,
            Color.fromRGBO(214, 230, 235, 0.055),
            _clear,
            _clear,
            Color.fromRGBO(214, 230, 235, 0.038),
            _clear,
          ],
          stops: [0, 0.19, 0.1915, 0.194, 0.64, 0.641, 0.6435],
        ),
      ],
      hazeOpacity: 0.3,
    ),
    brandMark: DovahBrandMarkTreatment(
      filter: DovahColorFilter(grayscale: 0.65, contrast: 1.25),
      glowColor: Color.fromRGBO(154, 201, 220, 0.17),
      glowBlurRadius: 8,
    ),
    connectionAccent: DovahConnectionAccent(
      overlayLayers: [
        DovahLinearLayer(
          angleDegrees: 118,
          colors: [
            _clear,
            _clear,
            Color.fromRGBO(206, 229, 237, 0.07),
            _clear,
            _clear,
            Color.fromRGBO(206, 229, 237, 0.05),
            _clear,
          ],
          stops: [0, 0.17, 0.173, 0.177, 0.63, 0.632, 0.636],
        ),
      ],
      overlayOpacity: 0.8,
      cornerOutline: Color.fromRGBO(169, 201, 216, 0.08),
      availableEdge: Color(0xFF86B4C7),
      overContent: true,
    ),
    backdrop: DovahBackdrop(
      tint: Color.fromRGBO(0, 2, 4, 0.78),
      blurSigma: 7,
      saturation: 0.72,
    ),
    presetCard: DovahPresetCardStyle(
      material: DovahMaterial(
        layers: [
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
          DovahLinearLayer(
            angleDegrees: 116,
            colors: [
              _clear,
              _clear,
              Color.fromRGBO(207, 226, 232, 0.11),
              Color.fromRGBO(2, 5, 7, 0.5),
              _clear,
            ],
            stops: [0, 0.19, 0.1915, 0.1935, 0.197],
          ),
        ],
        borderColor: Color(0xFF65747D),
      ),
      titleColor: Color(0xFFEDF3F6),
      summaryColor: Color(0xFFC0C7CA),
      detailColor: Color(0xFF929DA2),
      badgeFill: Color(0xFFA9C7D1),
      badgeForeground: Color(0xFF061014),
    ),
    previewScene: DovahPreviewScene(
      imageAssetPath: frostboundEnvironmentAsset,
      imageFilter: DovahColorFilter(
        grayscale: 0.35,
        saturate: 0.55,
        contrast: 1.15,
      ),
      layers: [
        DovahLinearLayer(
          angleDegrees: 180,
          colors: [Color.fromRGBO(1, 3, 4, 0.5), Color.fromRGBO(1, 3, 4, 0.78)],
          stops: [0, 1],
        ),
        DovahLinearLayer(
          angleDegrees: 180,
          colors: [_clear, _clear, Color(0xFF06090A)],
          stops: [0, 0.54, 1],
        ),
      ],
      sigil: DovahPreviewSigil(
        fill: Color(0xFF090E12),
        border: Color(0xFF71808A),
        shape: DovahPreviewSigilShape.square,
        markFilter: DovahColorFilter(grayscale: 0.72),
      ),
      barFill: DovahLinearLayer(
        angleDegrees: 90,
        colors: [Color(0xFF303B41), Color(0xFF303B41)],
        stops: [0, 1],
      ),
      barEdgeColor: Color(0xFFA43B40),
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
    atmosphere: DovahAtmosphere(
      layers: [
        DovahLinearLayer(
          angleDegrees: 120,
          colors: [Color(0xFF05090E), Color(0xFF09131D), Color(0xFF05090E)],
          stops: [0, 0.52, 1],
        ),
        DovahRadialLayer(
          center: Offset(0.06, 1.05),
          colors: [Color.fromRGBO(202, 121, 48, 0.14), _clear],
          stops: [0, 0.34],
        ),
        DovahRadialLayer(
          center: Offset(0.88, 0),
          colors: [Color.fromRGBO(39, 114, 157, 0.3), _clear],
          stops: [0, 0.36],
        ),
      ],
      hazeLayers: [
        DovahTileLayer(
          tileSize: Size(90, 90),
          content: DovahRadialLayer(
            center: Offset(0.5, 0.5),
            colors: [
              Color.fromRGBO(226, 165, 94, 0.18),
              Color.fromRGBO(226, 165, 94, 0.18),
              _clear,
            ],
            stops: [0, 1 / 1.5, 1],
            radius: 1.5,
            circular: true,
          ),
        ),
        DovahTileLayer(
          tileSize: Size(68, 118),
          content: DovahLinearLayer(
            angleDegrees: 120,
            colors: [
              _clear,
              Color.fromRGBO(116, 189, 232, 0.035),
              Color.fromRGBO(116, 189, 232, 0.035),
              _clear,
            ],
            stops: [0.46, 0.47, 0.48, 0.49],
          ),
        ),
      ],
      hazeOpacity: 0.32,
    ),
    brandMark: DovahBrandMarkTreatment(
      glowColor: Color.fromRGBO(116, 189, 232, 0.22),
      glowBlurRadius: 15,
    ),
    connectionAccent: DovahConnectionAccent(
      linkLayer: DovahLinearLayer(
        angleDegrees: 90,
        colors: [
          Color.fromRGBO(226, 165, 94, 0.75),
          Color.fromRGBO(226, 165, 94, 0.08),
          Color.fromRGBO(116, 189, 232, 0.08),
          Color.fromRGBO(116, 189, 232, 0.75),
        ],
        stops: [0, 0.25, 0.73, 1],
      ),
      linkOpacity: 0.6,
      cornerOutline: Color.fromRGBO(169, 201, 216, 0.08),
    ),
    backdrop: DovahBackdrop(tint: Color.fromRGBO(2, 4, 7, 0.76), blurSigma: 8),
    presetCard: DovahPresetCardStyle(
      material: DovahMaterial(
        layers: [
          DovahLinearLayer(
            angleDegrees: 145,
            colors: [Color(0xFF11212D), Color(0xFF071018)],
            stops: [0, 1],
          ),
          DovahStripeLayer(
            angleDegrees: 0,
            colors: [
              Color.fromRGBO(210, 232, 243, 0.025),
              Color.fromRGBO(210, 232, 243, 0.025),
              _clear,
              _clear,
            ],
            stopsPx: [0, 1, 1, 4],
          ),
        ],
        borderColor: Color(0xFF45667E),
      ),
      titleColor: Color(0xFFF1F6F9),
      summaryColor: Color(0xFFB1C2CD),
      detailColor: Color(0xFF7892A2),
      badgeFill: Color(0xFF8ED6FF),
      badgeForeground: Color(0xFF071015),
    ),
    previewScene: DovahPreviewScene(
      imageAssetPath: dovahConnectionHeroAsset,
      layers: [
        DovahLinearLayer(
          angleDegrees: 180,
          colors: [
            Color.fromRGBO(5, 10, 15, 0.25),
            Color.fromRGBO(5, 10, 15, 0.68),
          ],
          stops: [0, 1],
        ),
        DovahLinearLayer(
          angleDegrees: 180,
          colors: [_clear, _clear, Color(0xFF0B151E)],
          stops: [0, 0.54, 1],
        ),
      ],
      sigil: DovahPreviewSigil(
        fill: Color(0xFF10202C),
        border: Color(0xFFD49A55),
        shape: DovahPreviewSigilShape.diamond,
      ),
      barFill: DovahLinearLayer(
        angleDegrees: 90,
        colors: [Color(0xFFD7954D), Color(0xFF66B6E3)],
        stops: [0, 1],
      ),
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
          circular: true,
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
    atmosphere: DovahAtmosphere(
      imageAssetPath: hearthEnvironmentAsset,
      imageFilter: DovahColorFilter(
        brightness: 0.92,
        saturate: 0.92,
        contrast: 1.06,
      ),
      layers: [
        DovahLinearLayer(
          angleDegrees: 90,
          colors: [
            Color.fromRGBO(208, 183, 146, 0.58),
            Color.fromRGBO(192, 153, 104, 0.5),
          ],
          stops: [0, 1],
        ),
      ],
      hazeLayers: [
        DovahTileLayer(
          tileSize: Size(17, 13),
          content: DovahRadialLayer(
            center: Offset(0.18, 0.2),
            colors: [
              Color.fromRGBO(91, 56, 27, 0.1),
              Color.fromRGBO(91, 56, 27, 0.1),
              _clear,
              _clear,
            ],
            stops: [0, 0.2, 0.2, 1],
            radius: 5,
            repeating: true,
          ),
        ),
      ],
      hazeOpacity: 0.34,
    ),
    brandMark: DovahBrandMarkTreatment(
      filter: DovahColorFilter(
        sepia: 0.4,
        hueRotateDegrees: 345,
        saturate: 0.78,
      ),
      backingColor: Color.fromRGBO(255, 248, 230, 0.3),
    ),
    connectionAccent: DovahConnectionAccent(restingBorder: Color(0xFF79542F)),
    backdrop: DovahBackdrop(
      tint: Color.fromRGBO(47, 31, 18, 0.54),
      blurSigma: 9,
      sepia: 0.12,
    ),
    presetCard: DovahPresetCardStyle(
      material: DovahMaterial(
        layers: [
          DovahLinearLayer(
            angleDegrees: 145,
            colors: [Color(0xFFF7E6C9), Color(0xFFD9B681)],
            stops: [0, 1],
          ),
          DovahStripeLayer(
            angleDegrees: 5,
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
            center: Offset(0.18, 0.22),
            colors: [
              Color.fromRGBO(91, 55, 27, 0.11),
              Color.fromRGBO(91, 55, 27, 0.11),
              _clear,
            ],
            stops: [0, 1 / 1.5, 1],
            radius: 1.5,
            circular: true,
          ),
        ],
        borderColor: Color(0xFF8D6336),
      ),
      titleColor: Color(0xFF271B12),
      summaryColor: Color(0xFF4F3A28),
      detailColor: Color(0xFF6D5035),
      badgeFill: Color(0xFF965923),
      badgeForeground: Color(0xFFFFF9EE),
    ),
    previewScene: DovahPreviewScene(
      imageAssetPath: hearthEnvironmentAsset,
      imageFilter: DovahColorFilter(saturate: 0.92, contrast: 1.05),
      layers: [
        DovahLinearLayer(
          angleDegrees: 180,
          colors: [
            Color.fromRGBO(230, 209, 175, 0.12),
            Color.fromRGBO(202, 160, 104, 0.48),
          ],
          stops: [0, 1],
        ),
        DovahLinearLayer(
          angleDegrees: 180,
          colors: [_clear, _clear, Color(0xFFD9B681)],
          stops: [0, 0.54, 1],
        ),
      ],
      sigil: DovahPreviewSigil(
        fill: Color(0xFFDFBF8D),
        border: Color(0xFF82592F),
        shape: DovahPreviewSigilShape.circle,
        markFilter: DovahColorFilter(
          sepia: 0.38,
          hueRotateDegrees: 345,
          saturate: 0.75,
        ),
      ),
      barFill: DovahLinearLayer(
        angleDegrees: 90,
        colors: [Color(0xFFA8652D), Color(0xFFA8652D)],
        stops: [0, 1],
      ),
      barCornerRadius: 4,
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

  /// The canvas atmosphere behind the whole application.
  final DovahAtmosphere atmosphere;

  /// How the header's DovahLink sigil is dressed.
  final DovahBrandMarkTreatment brandMark;

  /// The decoration a connection card wears on top of its surface material.
  final DovahConnectionAccent connectionAccent;

  /// The treatment behind a modal dialog.
  final DovahBackdrop backdrop;

  /// How the theme dresses its own card in the appearance picker.
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

  /// Returns a copy with selected recipes replaced.
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
