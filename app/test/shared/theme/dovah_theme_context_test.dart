import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_dialog_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';

/// Exercises [DovahThemeContext.dovahTokens] and [DovahThemeContext.dovahDialogMetrics].
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

  group('Property dovahDialogMetrics behaves correctly', () {
    for (final (Size size, DovahDialogMetrics expected) in [
      (const Size(720, 480), DovahDialogMetrics.compact),
      (const Size(900, 560), DovahDialogMetrics.compact),
      (const Size(1100, 620), DovahDialogMetrics.compact),
      (const Size(1100, 621), DovahDialogMetrics.regular),
      (const Size(1280, 720), DovahDialogMetrics.regular),
      (const Size(1600, 900), DovahDialogMetrics.regular),
    ]) {
      testWidgets(
        'Property dovahDialogMetrics resolves the height-matched set at $size',
        (WidgetTester tester) async {
          tester.view.physicalSize = size * tester.view.devicePixelRatio;
          addTearDown(tester.view.reset);
          late DovahDialogMetrics resolved;

          await tester.pumpWidget(
            MaterialApp(
              home: Builder(
                builder: (BuildContext context) {
                  resolved = context.dovahDialogMetrics;
                  return const SizedBox.shrink();
                },
              ),
            ),
          );

          expect(resolved, expected);
        },
      );
    }
  });

  testWidgets(
    'Property dovahDialogMetrics follows the window when it is resized',
    (WidgetTester tester) async {
      addTearDown(tester.view.reset);
      late DovahDialogMetrics resolved;
      tester.view.physicalSize =
          const Size(1280, 720) * tester.view.devicePixelRatio;
      await tester.pumpWidget(
        MaterialApp(
          home: Builder(
            builder: (BuildContext context) {
              resolved = context.dovahDialogMetrics;
              return const SizedBox.shrink();
            },
          ),
        ),
      );
      expect(resolved, DovahDialogMetrics.regular);

      tester.view.physicalSize =
          const Size(1280, 500) * tester.view.devicePixelRatio;
      await tester.pump();

      expect(resolved, DovahDialogMetrics.compact);
    },
  );
}
