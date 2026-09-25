import 'dart:ui';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_color_filter.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_linear_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_preview_scene.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_preview_sigil.dart';

/// Builds a scene, overridable per field, for the equality tests.
DovahPreviewScene buildScene({
  String imageAssetPath = 'assets/a.png',
  DovahColorFilter imageFilter = DovahColorFilter.none,
  List<DovahLinearLayer> layers = const [
    DovahLinearLayer(
      angleDegrees: 180,
      colors: [Color(0x80000000), Color(0xC0000000)],
      stops: [0, 1],
    ),
  ],
  DovahPreviewSigil sigil = const DovahPreviewSigil(
    fill: Color(0xFF090E12),
    border: Color(0xFF71808A),
    shape: DovahPreviewSigilShape.square,
  ),
  DovahLinearLayer barFill = const DovahLinearLayer(
    angleDegrees: 90,
    colors: [Color(0xFF303B41), Color(0xFF303B41)],
    stops: [0, 1],
  ),
  Color? barEdgeColor,
  double barCornerRadius = 0,
}) => DovahPreviewScene(
  imageAssetPath: imageAssetPath,
  imageFilter: imageFilter,
  layers: layers,
  sigil: sigil,
  barFill: barFill,
  barEdgeColor: barEdgeColor,
  barCornerRadius: barCornerRadius,
);

/// Exercises [DovahPreviewScene]'s defaults and equality.
void main() {
  group('Behavior construction behaves correctly', () {
    test(
      'Behavior construction defaults to an untreated scene with plain bars',
      () {
        final DovahPreviewScene scene = buildScene();

        expect(scene.imageFilter, DovahColorFilter.none);
        expect(scene.barEdgeColor, isNull);
        expect(scene.barCornerRadius, isA<double>());
        expect(scene.barCornerRadius, 0);
      },
    );
  });

  group('Behavior equality behaves correctly', () {
    test('Behavior equality holds for identical scenes', () {
      expect(buildScene() == buildScene(), isTrue);
      expect(buildScene().hashCode, buildScene().hashCode);
    });

    test('Behavior equality fails when any field differs', () {
      final DovahPreviewScene base = buildScene();
      final List<DovahPreviewScene> others = [
        buildScene(imageAssetPath: 'assets/b.png'),
        buildScene(imageFilter: const DovahColorFilter(saturate: 0.5)),
        buildScene(layers: const []),
        buildScene(
          layers: const [
            DovahLinearLayer(
              angleDegrees: 180,
              colors: [Color(0x80000000), Color(0xE0000000)],
              stops: [0, 1],
            ),
          ],
        ),
        buildScene(
          sigil: const DovahPreviewSigil(
            fill: Color(0xFF000000),
            border: Color(0xFF71808A),
            shape: DovahPreviewSigilShape.square,
          ),
        ),
        buildScene(
          barFill: const DovahLinearLayer(
            angleDegrees: 90,
            colors: [Color(0xFF000000), Color(0xFF000000)],
            stops: [0, 1],
          ),
        ),
        buildScene(barEdgeColor: const Color(0xFFA43B40)),
        buildScene(barCornerRadius: 4),
      ];

      for (final DovahPreviewScene other in others) {
        expect(base == other, isFalse);
      }
    });
  });
}
