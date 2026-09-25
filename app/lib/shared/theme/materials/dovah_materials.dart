import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_atmosphere.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_backdrop.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_brand_mark_treatment.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_connection_accent.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_linear_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_material.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_material_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_preset_card_style.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_preview_scene.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_preview_sigil.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_radial_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_stripe_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_theme_materials.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_tile_layer.dart';

/// Transparent stop shared by this preset's layered fades.
const Color _clear = Color(0x00000000);

/// Dovah's surface layers, base first: the prototype's `--material` (forged steel with engraved
/// vertical lines, scanlines, and an ember-to-ice side tint).
const List<DovahMaterialLayer> _dovahSurfaceLayers = [
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
const List<DovahMaterialLayer> _dovahRaisedLayers = [
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

/// The Dovah preset's materials, from the prototype's `body.theme-dovah` `--material`,
/// `--material-raised`, `--material-shadow`, `.pc-icon`, and `.primary` rules.
const DovahThemeMaterials dovahMaterials = DovahThemeMaterials(
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
