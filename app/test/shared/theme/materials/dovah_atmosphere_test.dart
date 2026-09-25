import 'dart:ui';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/theme/materials/dovah_atmosphere.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_color_filter.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_linear_layer.dart';

/// Exercises [DovahAtmosphere]'s defaults and equality.
void main() {
  const DovahLinearLayer layer = DovahLinearLayer(
    angleDegrees: 180,
    colors: [Color(0xB3010304), Color(0xD6010304)],
    stops: [0, 1],
  );

  group('Behavior construction behaves correctly', () {
    test(
      'Behavior construction defaults to a plain, neutral, hazeless atmosphere',
      () {
        const DovahAtmosphere atmosphere = DovahAtmosphere();

        expect(atmosphere.imageAssetPath, isNull);
        expect(atmosphere.imageFilter, DovahColorFilter.none);
        expect(atmosphere.layers, isEmpty);
        expect(atmosphere.hazeLayers, isEmpty);
        expect(atmosphere.hazeOpacity, isA<double>());
        expect(atmosphere.hazeOpacity, 1);
      },
    );
  });

  group('Behavior equality behaves correctly', () {
    const DovahAtmosphere atmosphere = DovahAtmosphere(
      imageAssetPath: 'assets/a.png',
      imageFilter: DovahColorFilter(saturate: 0.5),
      layers: [layer],
      hazeLayers: [layer],
      hazeOpacity: 0.3,
    );

    test('Behavior equality holds for identical atmospheres', () {
      const DovahAtmosphere same = DovahAtmosphere(
        imageAssetPath: 'assets/a.png',
        imageFilter: DovahColorFilter(saturate: 0.5),
        layers: [layer],
        hazeLayers: [layer],
        hazeOpacity: 0.3,
      );

      expect(atmosphere == same, isTrue);
      expect(atmosphere.hashCode, same.hashCode);
    });

    test('Behavior equality fails when the image differs', () {
      expect(
        atmosphere ==
            const DovahAtmosphere(
              imageAssetPath: 'assets/b.png',
              imageFilter: DovahColorFilter(saturate: 0.5),
              layers: [layer],
              hazeLayers: [layer],
              hazeOpacity: 0.3,
            ),
        isFalse,
      );
    });

    test('Behavior equality fails when the filter differs', () {
      expect(
        atmosphere ==
            const DovahAtmosphere(
              imageAssetPath: 'assets/a.png',
              layers: [layer],
              hazeLayers: [layer],
              hazeOpacity: 0.3,
            ),
        isFalse,
      );
    });

    test('Behavior equality fails when the layers differ', () {
      expect(
        atmosphere ==
            const DovahAtmosphere(
              imageAssetPath: 'assets/a.png',
              imageFilter: DovahColorFilter(saturate: 0.5),
              hazeLayers: [layer],
              hazeOpacity: 0.3,
            ),
        isFalse,
      );
    });

    test('Behavior equality fails when the haze layers differ', () {
      expect(
        atmosphere ==
            const DovahAtmosphere(
              imageAssetPath: 'assets/a.png',
              imageFilter: DovahColorFilter(saturate: 0.5),
              layers: [layer],
              hazeOpacity: 0.3,
            ),
        isFalse,
      );
    });

    test('Behavior equality fails when the haze opacity differs', () {
      expect(
        atmosphere ==
            const DovahAtmosphere(
              imageAssetPath: 'assets/a.png',
              imageFilter: DovahColorFilter(saturate: 0.5),
              layers: [layer],
              hazeLayers: [layer],
              hazeOpacity: 0.4,
            ),
        isFalse,
      );
    });
  });
}
