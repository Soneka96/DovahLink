import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';

/// Exercises [DovahThemeContext.dovahTokens].
void main() {
  group('Property dovahTokens behaves correctly', () {
    testWidgets(
      'Property dovahTokens returns the active theme\'s DovahThemeTokens',
      (WidgetTester tester) async {
        late DovahThemeTokens resolved;

        await tester.pumpWidget(
          MaterialApp(
            theme: dovahThemeDataFor(DovahThemePreset.hearth),
            home: Builder(
              builder: (BuildContext context) {
                resolved = context.dovahTokens;
                return const SizedBox.shrink();
              },
            ),
          ),
        );

        final DovahThemeTokens expected = dovahThemeDataFor(
          DovahThemePreset.hearth,
        ).extension<DovahThemeTokens>()!;
        expect(resolved, expected);
      },
    );
  });
}
