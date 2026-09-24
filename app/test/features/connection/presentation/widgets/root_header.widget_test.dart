import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/presentation/widgets/root_header.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_root_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_sigil.widget.dart';
import '../../../../shared/theme/widgets/dovah_widget_test_helpers.dart';

/// Exercises [RootHeader] across every DovahLink theme, both test sizes, and text scaling.
void main() {
  group('RootHeader renders correctly', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in dovahTestSizes) {
        testWidgets(
          'RootHeader renders under $preset at $size with its themed height and no overflow',
          (WidgetTester tester) async {
            await pumpDovahThemedWidget(
              tester,
              SingleChildScrollView(child: RootHeader(onOpenAppearance: () {})),
              preset: preset,
              size: size,
            );
            expect(tester.takeException(), isNull);
            expect(
              tester.getSize(find.byType(RootHeader)).height,
              isA<double>(),
            );
            expect(
              tester.getSize(find.byType(RootHeader)).height,
              DovahRootMetrics.forWindow(
                preset: preset,
                window: size,
              ).headerHeight,
            );
            expect(find.text('DOVAHLINK'), findsOneWidget);
            expect(find.text('SKYRIM COMPANION'), findsOneWidget);
            expect(find.byType(DovahSigil), findsOneWidget);
          },
        );
      }
    }

    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'RootHeader spans its themed fraction of the header with its gradient rule under $preset',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            SingleChildScrollView(child: RootHeader(onOpenAppearance: () {})),
            preset: preset,
            size: dovahTestSizes.first,
          );
          final DovahThemeTokens tokens = dovahThemeDataFor(
            preset,
          ).extension<DovahThemeTokens>()!;
          final double headerWidth = tester
              .getSize(find.byType(RootHeader))
              .width;
          final Size rule = tester.getSize(
            find.byKey(const Key('root-header-rule')),
          );

          expect(
            rule.width,
            closeTo(headerWidth * tokens.rootHeaderRuleFraction, 0.01),
          );
          expect(rule.height, DovahRootMetrics.headerRuleHeight);
        },
      );
    }

    testWidgets('RootHeader renders at double text scale without overflow', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        Builder(
          builder: (BuildContext context) => MediaQuery(
            data: MediaQuery.of(
              context,
            ).copyWith(textScaler: const TextScaler.linear(2)),
            child: SingleChildScrollView(
              child: RootHeader(onOpenAppearance: () {}),
            ),
          ),
        ),
        preset: DovahThemePreset.frostbound,
        size: dovahTestSizes.first,
      );

      expect(tester.takeException(), isNull);
    });
  });

  group('RootHeader calls onOpenAppearance', () {
    testWidgets('RootHeader calls onOpenAppearance when the action is tapped', (
      WidgetTester tester,
    ) async {
      int callCount = 0;
      await pumpDovahThemedWidget(
        tester,
        RootHeader(onOpenAppearance: () => callCount++),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );

      await tester.tap(find.byIcon(Icons.settings_outlined));
      await tester.pump();

      expect(callCount, 1);
    });

    testWidgets('RootHeader does not call onOpenAppearance before a tap', (
      WidgetTester tester,
    ) async {
      int callCount = 0;
      await pumpDovahThemedWidget(
        tester,
        RootHeader(onOpenAppearance: () => callCount++),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );

      expect(callCount, 0);
    });
  });

  group('RootHeader exposes sensible semantics', () {
    testWidgets(
      'RootHeader labels the appearance action and meets tap-target size',
      (WidgetTester tester) async {
        final SemanticsHandle semantics = tester.ensureSemantics();
        try {
          await pumpDovahThemedWidget(
            tester,
            RootHeader(onOpenAppearance: () {}),
            preset: DovahThemePreset.dovah,
            size: dovahTestSizes.first,
          );

          expect(find.bySemanticsLabel('Appearance settings'), findsOneWidget);
          await expectLater(tester, meetsGuideline(labeledTapTargetGuideline));
          await expectLater(tester, meetsGuideline(androidTapTargetGuideline));
        } finally {
          semantics.dispose();
        }
      },
    );
  });

  group('RootHeader follows the prototype breakpoints and brand colors', () {
    for (final (DovahThemePreset preset, Size size, double height) in [
      (DovahThemePreset.frostbound, const Size(1280, 720), 70),
      (DovahThemePreset.dovah, const Size(1280, 720), 88),
      (DovahThemePreset.hearth, const Size(1600, 900), 86),
      (DovahThemePreset.dovah, const Size(800, 700), 88),
      (DovahThemePreset.frostbound, const Size(720, 480), 56),
      (DovahThemePreset.dovah, const Size(900, 560), 62),
      (DovahThemePreset.hearth, const Size(720, 480), 62),
    ]) {
      testWidgets('RootHeader is $height tall under ${preset.name} at $size', (
        WidgetTester tester,
      ) async {
        await pumpDovahThemedWidget(
          tester,
          SingleChildScrollView(child: RootHeader(onOpenAppearance: () {})),
          preset: preset,
          size: size,
        );

        expect(tester.takeException(), isNull);
        expect(tester.getSize(find.byType(RootHeader)).height, height);
      });
    }

    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'RootHeader colors its LINK half and tagline with the ${preset.name} brand tones',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            SingleChildScrollView(child: RootHeader(onOpenAppearance: () {})),
            preset: preset,
            size: const Size(1280, 720),
          );
          final DovahThemeTokens tokens = dovahThemeDataFor(
            preset,
          ).extension<DovahThemeTokens>()!;
          final Text wordmark = tester.widget(find.text('DOVAHLINK'));
          final TextSpan span = wordmark.textSpan! as TextSpan;
          final Text tagline = tester.widget(find.text('SKYRIM COMPANION'));

          expect(span.text, 'DOVAH');
          expect((span.children!.single as TextSpan).text, 'LINK');
          expect(
            (span.children!.single as TextSpan).style?.color,
            tokens.brandAccent,
          );
          expect(wordmark.style?.color, tokens.textPrimary);
          expect(tagline.style?.color, tokens.brandTagline);
          expect(
            tagline.style?.letterSpacing,
            DovahRootMetrics.forWindow(
                  preset: preset,
                  window: const Size(1280, 720),
                ).brandTaglineLetterSpacingEm *
                DovahRootMetrics.brandTaglineFontSize,
          );
        },
      );
    }
  });
}
