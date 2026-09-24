import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_connection_card_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_connection_card_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_dialog_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_overview_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_page_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_root_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_root_theme_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_session_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_context.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';

/// Exercises [DovahThemeContext.dovahTokens] and the metrics accessors that resolve from the active
/// theme and window.
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

  group('Property dovahRootMetrics behaves correctly', () {
    for (final (DovahThemePreset preset, Size size) in [
      (DovahThemePreset.frostbound, const Size(1280, 720)),
      (DovahThemePreset.dovah, const Size(900, 560)),
      (DovahThemePreset.hearth, const Size(800, 700)),
    ]) {
      testWidgets(
        'Property dovahRootMetrics resolves the ${preset.name} set at $size',
        (WidgetTester tester) async {
          tester.view.physicalSize = size * tester.view.devicePixelRatio;
          addTearDown(tester.view.reset);
          late DovahRootMetrics resolved;

          await tester.pumpWidget(
            MaterialApp(
              theme: dovahThemeDataFor(preset),
              home: Builder(
                builder: (BuildContext context) {
                  resolved = context.dovahRootMetrics;
                  return const SizedBox.shrink();
                },
              ),
            ),
          );

          expect(
            resolved,
            DovahRootMetrics.forWindow(
              themeMetrics: dovahThemeDataFor(
                preset,
              ).extension<DovahRootThemeMetrics>()!,
              window: size,
            ),
          );
        },
      );
    }

    testWidgets(
      'Property dovahRootMetrics follows the window when it is resized',
      (WidgetTester tester) async {
        addTearDown(tester.view.reset);
        late DovahRootMetrics resolved;
        tester.view.physicalSize =
            const Size(1280, 720) * tester.view.devicePixelRatio;
        await tester.pumpWidget(
          MaterialApp(
            theme: dovahThemeDataFor(DovahThemePreset.dovah),
            home: Builder(
              builder: (BuildContext context) {
                resolved = context.dovahRootMetrics;
                return const SizedBox.shrink();
              },
            ),
          ),
        );
        expect(resolved.headerHeight, 88);

        tester.view.physicalSize =
            const Size(1280, 500) * tester.view.devicePixelRatio;
        await tester.pump();

        expect(resolved.headerHeight, 62);
      },
    );
  });

  group('Property dovahConnectionCardMetrics behaves correctly', () {
    for (final (DovahThemePreset preset, Size size) in [
      (DovahThemePreset.frostbound, const Size(900, 560)),
      (DovahThemePreset.dovah, const Size(1280, 720)),
      (DovahThemePreset.hearth, const Size(800, 700)),
    ]) {
      testWidgets(
        'Property dovahConnectionCardMetrics resolves the ${preset.name} set at $size',
        (WidgetTester tester) async {
          tester.view.physicalSize = size * tester.view.devicePixelRatio;
          addTearDown(tester.view.reset);
          late DovahConnectionCardMetrics resolved;

          await tester.pumpWidget(
            MaterialApp(
              theme: dovahThemeDataFor(preset),
              home: Builder(
                builder: (BuildContext context) {
                  resolved = context.dovahConnectionCardMetrics;
                  return const SizedBox.shrink();
                },
              ),
            ),
          );

          expect(
            resolved,
            DovahConnectionCardMetrics.forWindow(
              themeMetrics: dovahThemeDataFor(
                preset,
              ).extension<DovahConnectionCardThemeMetrics>()!,
              window: size,
            ),
          );
        },
      );
    }
  });

  group('Property dovahPageMetrics behaves correctly', () {
    for (final (DovahThemePreset preset, Size size) in [
      (DovahThemePreset.frostbound, const Size(900, 560)),
      (DovahThemePreset.dovah, const Size(1280, 720)),
      (DovahThemePreset.hearth, const Size(800, 700)),
    ]) {
      testWidgets(
        'Property dovahPageMetrics resolves the ${preset.name} set at $size',
        (WidgetTester tester) async {
          tester.view.physicalSize = size * tester.view.devicePixelRatio;
          addTearDown(tester.view.reset);
          late DovahPageMetrics resolved;

          await tester.pumpWidget(
            MaterialApp(
              theme: dovahThemeDataFor(preset),
              home: Builder(
                builder: (BuildContext context) {
                  resolved = context.dovahPageMetrics;
                  return const SizedBox.shrink();
                },
              ),
            ),
          );

          expect(
            resolved,
            DovahPageMetrics.forWindow(preset: preset, window: size),
          );
        },
      );
    }
  });

  group('Property dovahSessionMetrics behaves correctly', () {
    for (final (DovahThemePreset preset, Size size) in [
      (DovahThemePreset.frostbound, const Size(1280, 720)),
      (DovahThemePreset.dovah, const Size(900, 560)),
      (DovahThemePreset.hearth, const Size(800, 700)),
    ]) {
      testWidgets(
        'Property dovahSessionMetrics resolves the ${preset.name} set at $size',
        (WidgetTester tester) async {
          tester.view.physicalSize = size * tester.view.devicePixelRatio;
          addTearDown(tester.view.reset);
          late DovahSessionMetrics resolved;

          await tester.pumpWidget(
            MaterialApp(
              theme: dovahThemeDataFor(preset),
              home: Builder(
                builder: (BuildContext context) {
                  resolved = context.dovahSessionMetrics;
                  return const SizedBox.shrink();
                },
              ),
            ),
          );

          expect(
            resolved,
            DovahSessionMetrics.forWindow(preset: preset, window: size),
          );
        },
      );
    }
  });

  group('Property dovahOverviewMetrics behaves correctly', () {
    for (final (DovahThemePreset preset, Size size) in [
      (DovahThemePreset.frostbound, const Size(1000, 560)),
      (DovahThemePreset.dovah, const Size(1280, 720)),
      (DovahThemePreset.hearth, const Size(800, 700)),
    ]) {
      testWidgets(
        'Property dovahOverviewMetrics resolves the ${preset.name} set at $size',
        (WidgetTester tester) async {
          tester.view.physicalSize = size * tester.view.devicePixelRatio;
          addTearDown(tester.view.reset);
          late DovahOverviewMetrics resolved;

          await tester.pumpWidget(
            MaterialApp(
              theme: dovahThemeDataFor(preset),
              home: Builder(
                builder: (BuildContext context) {
                  resolved = context.dovahOverviewMetrics;
                  return const SizedBox.shrink();
                },
              ),
            ),
          );

          expect(
            resolved,
            DovahOverviewMetrics.forWindow(preset: preset, window: size),
          );
        },
      );
    }
  });
}
