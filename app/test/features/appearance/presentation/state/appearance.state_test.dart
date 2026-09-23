import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/appearance/presentation/state/appearance.state.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// Exercises [AppearanceState] initialization and value semantics.
void main() {
  group('AppearanceState — initial', () {
    test('initial uses defaultThemePreset', () {
      final AppearanceState state = AppearanceState.initial();

      expect(state.activePreset, isA<DovahThemePreset>());
      expect(state.activePreset, defaultThemePreset);
    });
  });

  group('Method copyWith behaves correctly', () {
    test('Method copyWith replaces activePreset when given', () {
      final AppearanceState state = AppearanceState.initial();

      final AppearanceState result = state.copyWith(
        activePreset: DovahThemePreset.hearth,
      );

      expect(result.activePreset, DovahThemePreset.hearth);
    });

    test('Method copyWith keeps activePreset when omitted', () {
      const AppearanceState state = AppearanceState(
        activePreset: DovahThemePreset.frostbound,
      );

      final AppearanceState result = state.copyWith();

      expect(result.activePreset, DovahThemePreset.frostbound);
    });
  });

  group('Behavior equality behaves correctly', () {
    test('Behavior equality holds for states built from equal fields', () {
      const AppearanceState first = AppearanceState(
        activePreset: DovahThemePreset.dovah,
      );
      const AppearanceState second = AppearanceState(
        activePreset: DovahThemePreset.dovah,
      );

      expect(first, second);
      expect(first.hashCode, second.hashCode);
    });

    test('Behavior equality fails when activePreset differs', () {
      const AppearanceState first = AppearanceState(
        activePreset: DovahThemePreset.dovah,
      );
      const AppearanceState second = AppearanceState(
        activePreset: DovahThemePreset.hearth,
      );

      expect(first, isNot(second));
    });
  });
}
