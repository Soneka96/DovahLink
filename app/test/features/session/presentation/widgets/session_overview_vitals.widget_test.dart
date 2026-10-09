import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/session/presentation/widgets/session_overview_vitals.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_panel.widget.dart';
import '../../../../fixtures/fixtures.dart';
import '../../../../shared/theme/widgets/dovah_widget_test_helpers.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show CharacterVitalsState, DovahLinkStateStatus;

/// Builds the Current Status panel from one coherent SDK Vitals value.
SessionOverviewVitalsPanel _buildPanel({
  CharacterVitalsState? value,
  DovahLinkStateStatus status = DovahLinkStateStatus.synchronized,
  bool omitValue = false,
}) => SessionOverviewVitalsPanel(
  viewData: Fixtures.buildSessionOverviewVitalsViewData(
    value: omitValue ? null : value ?? Fixtures.buildCharacterVitals(),
    status: status,
  ),
);

/// Exercises truthful vital labels, ratios, synchronization presentation, and semantics.
void main() {
  group('SessionOverviewVitalsPanel displays', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'SessionOverviewVitalsPanel uses the $preset side-panel texture',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            _buildPanel(),
            preset: preset,
            size: const Size(1280, 720),
          );

          final DovahPanel panel = tester.widget(
            find.byKey(const Key('session-overview-vitals-panel')),
          );
          final ColoredBox leadingAccent = tester.widget(
            find.byKey(const Key('dovah-surface-leading-accent')),
          );
          expect(
            panel.overlayGradient,
            preset == DovahThemePreset.dovah ? isA<RadialGradient>() : isNull,
          );
          expect(
            panel.leadingAccent,
            dovahThemeDataFor(preset).extension<DovahThemeTokens>()!.signal,
          );
          expect(
            leadingAccent.color,
            dovahThemeDataFor(preset).extension<DovahThemeTokens>()!.signal,
          );
        },
      );
    }

    testWidgets('SessionOverviewVitalsPanel shows the three safe percentages', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        _buildPanel(
          value: Fixtures.buildCharacterVitals(
            health: Fixtures.buildCharacterVital(current: 86, max: 100),
            magicka: Fixtures.buildCharacterVital(current: 62, max: 100),
            stamina: Fixtures.buildCharacterVital(current: 74, max: 100),
          ),
        ),
        preset: DovahThemePreset.dovah,
        size: const Size(1280, 720),
      );

      expect(find.text('Current status'), findsOneWidget);
      expect(find.text('Health'), findsOneWidget);
      expect(find.text('Magicka'), findsOneWidget);
      expect(find.text('Stamina'), findsOneWidget);
      expect(find.text('86%'), findsOneWidget);
      expect(find.text('62%'), findsOneWidget);
      expect(find.text('74%'), findsOneWidget);
      expect(find.bySemanticsLabel('Health, 86 percent'), findsOneWidget);
    });

    for (final DovahLinkStateStatus status in [
      DovahLinkStateStatus.stale,
      DovahLinkStateStatus.failed,
    ]) {
      testWidgets(
        'SessionOverviewVitalsPanel keeps the $status fill visually subdued',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            _buildPanel(status: status),
            preset: DovahThemePreset.dovah,
            size: const Size(1280, 720),
          );

          expect(find.text('80%'), findsOneWidget);
          final DecoratedBox healthFill = tester.widget(
            find.byKey(const Key('session-overview-health-bar-fill')),
          );
          expect(
            (healthFill.decoration as BoxDecoration).color!.a,
            closeTo(0.65, 0.001),
          );
          final Text percentage = tester.widget(find.text('80%'));
          expect(percentage.style?.color?.a, closeTo(0.72, 0.001));
          expect(percentage.style?.fontStyle, FontStyle.italic);
        },
      );
    }

    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'SessionOverviewVitalsPanel paints precise bar ratios and colors under $preset',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            _buildPanel(
              value: Fixtures.buildCharacterVitals(
                health: Fixtures.buildCharacterVital(current: 100, max: 100),
                magicka: Fixtures.buildCharacterVital(current: 50, max: 100),
                stamina: Fixtures.buildCharacterVital(current: 0, max: 100),
              ),
            ),
            preset: preset,
            size: const Size(1280, 720),
          );

          final DovahThemeTokens tokens = dovahThemeDataFor(
            preset,
          ).extension<DovahThemeTokens>()!;
          for (final (String vital, Color color, double widthRatio) in [
            ('health', tokens.health, 1.0),
            ('magicka', tokens.magicka, 0.5),
            ('stamina', tokens.stamina, 0.0),
          ]) {
            final Size trackSize = tester.getSize(
              find.byKey(Key('session-overview-$vital-bar-track')),
            );
            final Finder fillFinder = find.byKey(
              Key('session-overview-$vital-bar-fill'),
            );
            final Size fillSize = tester.getSize(fillFinder);
            final DecoratedBox fill = tester.widget(fillFinder);

            expect(fillSize.height, trackSize.height);
            expect(fillSize.width, closeTo(trackSize.width * widthRatio, 0.01));
            expect((fill.decoration as BoxDecoration).color, color);
          }
        },
      );
    }

    testWidgets(
      'SessionOverviewVitalsPanel uses themed material while recovering',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          _buildPanel(status: DovahLinkStateStatus.recovering),
          preset: DovahThemePreset.dovah,
          size: const Size(1280, 720),
        );

        final DovahPanel panel = tester.widget(
          find.byKey(const Key('session-overview-vitals-panel')),
        );
        expect(panel.raised, isTrue);
        expect(find.text('80%'), findsOneWidget);
        expect(find.textContaining('Recovering'), findsNothing);
        final DecoratedBox healthFill = tester.widget(
          find.byKey(const Key('session-overview-health-bar-fill')),
        );
        expect(
          (healthFill.decoration as BoxDecoration).color,
          dovahThemeDataFor(
            DovahThemePreset.dovah,
          ).extension<DovahThemeTokens>()!.health,
        );
        expect(
          tester.widget<Text>(find.text('80%')).style?.color,
          dovahThemeDataFor(
            DovahThemePreset.dovah,
          ).extension<DovahThemeTokens>()!.textPrimary,
        );
      },
    );

    testWidgets('SessionOverviewVitalsPanel leaves unavailable bars dormant', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        _buildPanel(
          value: Fixtures.buildCharacterVitals(isAvailable: false),
          status: DovahLinkStateStatus.unavailable,
        ),
        preset: DovahThemePreset.dovah,
        size: const Size(1280, 720),
      );

      expect(find.text('Health'), findsOneWidget);
      expect(find.text('Magicka'), findsOneWidget);
      expect(find.text('Stamina'), findsOneWidget);
      expect(find.text('0%'), findsNothing);
      expect(find.text('Unavailable'), findsNothing);
      expect(
        find.byKey(const Key('session-overview-health-bar-fill')),
        findsNothing,
      );
    });

    testWidgets(
      'SessionOverviewVitalsPanel keeps failed values dormant without a value',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          _buildPanel(omitValue: true, status: DovahLinkStateStatus.failed),
          preset: DovahThemePreset.dovah,
          size: const Size(1280, 720),
        );

        expect(find.text('Health'), findsOneWidget);
        expect(find.text('80%'), findsNothing);
        expect(find.textContaining('Failed'), findsNothing);
      },
    );
  });

  group('SessionOverviewVitalsPanel lays out', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in dovahResponsiveTestSizes) {
        testWidgets(
          'SessionOverviewVitalsPanel renders $preset at $size without overflow',
          (WidgetTester tester) async {
            await pumpDovahThemedWidget(
              tester,
              _buildPanel(),
              preset: preset,
              size: size,
            );

            expect(tester.takeException(), isNull);
          },
        );
      }
    }
  });
}
