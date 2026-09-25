import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// Exercises the values shared constants pin to a cross-side contract.
void main() {
  group('Property pairingCodeLength behaves correctly', () {
    test(
      'Property pairingCodeLength matches the six digits the Host requires',
      () {
        expect(pairingCodeLength, isA<int>());
        expect(pairingCodeLength, 6);
      },
    );
  });

  group('Property defaultThemePreset behaves correctly', () {
    test('Property defaultThemePreset is the prototype default preset', () {
      expect(defaultThemePreset, DovahThemePreset.dovah);
    });
  });

  group('Property appearance picker constants behave correctly', () {
    test('Property appearance picker constants keep their approved sizes', () {
      expect(appearancePresetCardMinimumWidth, isA<double>());
      expect(appearancePresetCardMinimumWidth, 160);
      expect(appearancePreviewAccentHeight, isA<double>());
      expect(appearancePreviewAccentHeight, 6);
      expect(appearanceSelectionIconSize, isA<double>());
      expect(appearanceSelectionIconSize, 20);
    });
  });

  group('Property theme asset constants behave correctly', () {
    test(
      'Property theme asset constants point at the bundled theme images',
      () {
        expect(frostboundEnvironmentAsset, isA<String>());
        expect(
          frostboundEnvironmentAsset,
          'assets/themes/frostbound/frostbound-environment.png',
        );
        expect(dovahConnectionHeroAsset, isA<String>());
        expect(
          dovahConnectionHeroAsset,
          'assets/themes/dovah/dovahlink-connection-hero.png',
        );
        expect(hearthEnvironmentAsset, isA<String>());
        expect(
          hearthEnvironmentAsset,
          'assets/themes/hearth/hearth-environment.png',
        );
      },
    );
  });
}
