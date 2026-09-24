import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/presentation/widgets/root_header.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
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
            final DovahThemeTokens tokens = dovahThemeDataFor(
              preset,
            ).extension<DovahThemeTokens>()!;

            expect(tester.takeException(), isNull);
            expect(
              tester.getSize(find.byType(RootHeader)).height,
              isA<double>(),
            );
            expect(
              tester.getSize(find.byType(RootHeader)).height,
              tokens.rootHeaderHeight,
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
          expect(rule.height, DovahThemeTokens.rootHeaderRuleHeight);
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
}
