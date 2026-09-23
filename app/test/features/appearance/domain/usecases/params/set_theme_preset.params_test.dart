import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/appearance/domain/usecases/params/set_theme_preset.params.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// Exercises [SetThemePresetParams]'s properties and equality.
void main() {
  group('Property preset behaves correctly', () {
    test('Property preset returns the constructed value', () {
      const SetThemePresetParams params = SetThemePresetParams(
        preset: DovahThemePreset.frostbound,
      );

      expect(params.preset, isA<DovahThemePreset>());
      expect(params.preset, DovahThemePreset.frostbound);
    });
  });

  group('Behavior equality behaves correctly', () {
    test('Behavior equality holds for params built from equal fields', () {
      const SetThemePresetParams first = SetThemePresetParams(
        preset: DovahThemePreset.frostbound,
      );
      const SetThemePresetParams second = SetThemePresetParams(
        preset: DovahThemePreset.frostbound,
      );

      expect(first, second);
      expect(first.hashCode, second.hashCode);
    });

    test('Behavior equality fails when preset differs', () {
      const SetThemePresetParams first = SetThemePresetParams(
        preset: DovahThemePreset.frostbound,
      );
      const SetThemePresetParams second = SetThemePresetParams(
        preset: DovahThemePreset.hearth,
      );

      expect(first, isNot(second));
    });
  });
}
