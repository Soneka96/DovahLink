import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/appearance/presentation/state/appearance.actions.dart';
import 'package:dovahlink_client/features/appearance/presentation/state/appearance.reducer.dart';
import 'package:dovahlink_client/features/appearance/presentation/state/appearance.state.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// Exercises the appearance reducer for every handled action and unhandled pass-through.
void main() {
  group('Action ThemePresetSelectedAction behaves correctly', () {
    test(
      'ThemePresetSelectedAction sets activePreset to the selected preset',
      () {
        final AppearanceState state = AppearanceState.initial();

        final AppearanceState result = appearanceReducer(
          state,
          const ThemePresetSelectedAction(DovahThemePreset.hearth),
        );

        expect(result.activePreset, DovahThemePreset.hearth);
      },
    );

    test('ThemePresetSelectedAction returns a distinct state instance', () {
      final AppearanceState state = AppearanceState.initial();

      final AppearanceState result = appearanceReducer(
        state,
        const ThemePresetSelectedAction(DovahThemePreset.hearth),
      );

      expect(identical(result, state), isFalse);
    });
  });

  group('Action Object behaves correctly', () {
    test('Object returns the same state instance for an unhandled action', () {
      final AppearanceState state = AppearanceState.initial();

      final AppearanceState result = appearanceReducer(state, Object());

      expect(identical(result, state), isTrue);
    });
  });
}
