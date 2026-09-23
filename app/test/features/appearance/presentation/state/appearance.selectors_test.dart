import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/appearance/presentation/state/appearance.selectors.dart';
import 'package:dovahlink_client/features/appearance/presentation/state/appearance.state.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Exercises [AppearanceSelectors] over [AppState].
void main() {
  AppState buildAppState(DovahThemePreset preset) =>
      AppState.initial(appearance: AppearanceState(activePreset: preset));

  group('Property activePresetSelector behaves correctly', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      test('Property activePresetSelector returns $preset', () {
        final AppState state = buildAppState(preset);

        expect(AppearanceSelectors.activePresetSelector(state), preset);
      });
    }
  });
}
