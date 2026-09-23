import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/appearance/presentation/state/appearance.actions.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// Exercises [ThemePresetSelectedAction]'s properties and equality.
void main() {
  group('Property preset behaves correctly', () {
    test('Property preset returns the constructed value', () {
      const ThemePresetSelectedAction action = ThemePresetSelectedAction(
        DovahThemePreset.frostbound,
      );

      expect(action.preset, isA<DovahThemePreset>());
      expect(action.preset, DovahThemePreset.frostbound);
    });
  });

  group('Behavior equality behaves correctly', () {
    test('Behavior equality holds for actions built from equal fields', () {
      const ThemePresetSelectedAction first = ThemePresetSelectedAction(
        DovahThemePreset.frostbound,
      );
      const ThemePresetSelectedAction second = ThemePresetSelectedAction(
        DovahThemePreset.frostbound,
      );

      expect(first, second);
      expect(first.hashCode, second.hashCode);
    });

    test('Behavior equality fails when preset differs', () {
      const ThemePresetSelectedAction first = ThemePresetSelectedAction(
        DovahThemePreset.frostbound,
      );
      const ThemePresetSelectedAction second = ThemePresetSelectedAction(
        DovahThemePreset.hearth,
      );

      expect(first, isNot(second));
    });
  });
}
