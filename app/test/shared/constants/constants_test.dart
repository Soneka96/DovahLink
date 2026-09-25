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
      expect(appearancePreviewHeight, isA<double>());
      expect(appearancePreviewHeight, 48);
      expect(appearancePresetCardMinimumWidth, isA<double>());
      expect(appearancePresetCardMinimumWidth, 160);
      expect(appearancePreviewAccentHeight, isA<double>());
      expect(appearancePreviewAccentHeight, 6);
      expect(appearanceSelectionIconSize, isA<double>());
      expect(appearanceSelectionIconSize, 20);
    });
  });
}
