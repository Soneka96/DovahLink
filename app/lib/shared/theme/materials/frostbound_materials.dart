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

/// Transparent stop shared by this preset's layered fades.
const Color _clear = Color(0x00000000);

/// Frostbound's surface layers, base first: the prototype's `--material` (scratched iron with a
/// hairline, frost stains, and two fracture lines).
const List<DovahMaterialLayer> _frostboundSurfaceLayers = [
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
const List<DovahMaterialLayer> _frostboundRaisedLayers = [
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

/// Frostbound's materials, from the prototype's `body.theme-frostbound` `--material`,
/// `--material-raised`, `--material-shadow`, `.pc-icon`, and `.primary` rules.
const DovahThemeMaterials frostboundMaterials = DovahThemeMaterials(
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
        colors: [_clear, _clear, Color.fromRGBO(214, 230, 235, 0.032), _clear],
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
);
