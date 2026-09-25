import 'package:flutter/material.dart';

import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_atmosphere.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_color_filter.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_linear_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_material.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_material_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_radial_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_stripe_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_theme_materials.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_tile_layer.dart';

/// Transparent stop shared by this preset's layered fades.
const Color _clear = Color(0x00000000);

/// Hearth's surface layers, base first: the prototype's `--material` (pressed parchment with
/// paper fibre and two ink specks).
const List<DovahMaterialLayer> _hearthSurfaceLayers = [
  DovahLinearLayer(
    angleDegrees: 145,
    colors: [Color(0xFFF7E6C9), Color(0xFFD9B681)],
    stops: [0, 1],
  ),
  DovahStripeLayer(
    angleDegrees: 6,
    colors: [_clear, _clear, Color.fromRGBO(92, 57, 29, 0.055), _clear, _clear],
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
const List<DovahMaterialLayer> _hearthRaisedLayers = [
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

/// Hearth's materials, from the prototype's `body.theme-hearth` `--material`,
/// `--material-raised`, `--material-shadow`, `.pc-icon`, and `.primary` rules. Its panels are
/// not clipped, so its surfaces keep the prototype's soft drop shadow. The surface border is the
/// panel's `#8d6336`; the prototype's connection card uses the slightly darker `--line2`.
const DovahThemeMaterials hearthMaterials = DovahThemeMaterials(
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
);
