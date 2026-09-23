import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/appearance/presentation/state/appearance.selectors.dart';
import 'package:dovahlink_client/features/appearance/presentation/state/appearance.state.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';

/// Exercises [AppearanceSelectors] over [AppState].
void main() {
  AppState buildAppState(DovahThemePreset preset) =>
      AppState.initial(appearance: AppearanceState(activePreset: preset));

  group('Property activePresetSelector behaves correctly', () {
    test('Property activePresetSelector returns the active preset', () {
      final AppState state = buildAppState(DovahThemePreset.frostbound);

      expect(
        AppearanceSelectors.activePresetSelector(state),
        DovahThemePreset.frostbound,
      );
    });
  });

  group('Property activeThemeDataSelector behaves correctly', () {
    test(
      'Property activeThemeDataSelector returns ThemeData carrying the active preset\'s tokens',
      () {
        final AppState state = buildAppState(DovahThemePreset.hearth);

        final ThemeData theme = AppearanceSelectors.activeThemeDataSelector(
          state,
        );
        final DovahThemeTokens? tokens = theme.extension<DovahThemeTokens>();

        expect(theme, isA<ThemeData>());
        expect(tokens, isA<DovahThemeTokens>());
        expect(tokens!.cornerStyle, DovahPanelCornerStyle.rounded);
      },
    );

    test('Property activeThemeDataSelector reflects a different preset', () {
      final AppState state = buildAppState(DovahThemePreset.frostbound);

      final ThemeData theme = AppearanceSelectors.activeThemeDataSelector(
        state,
      );
      final DovahThemeTokens? tokens = theme.extension<DovahThemeTokens>();

      expect(tokens!.cornerStyle, DovahPanelCornerStyle.singleBevel);
    });
  });
}
