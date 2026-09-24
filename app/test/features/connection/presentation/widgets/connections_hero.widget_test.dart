import 'dart:ui' show Tristate;

import 'package:flutter/material.dart';
import 'package:flutter/semantics.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/presentation/widgets/connections_hero.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import '../../../../shared/theme/widgets/dovah_widget_test_helpers.dart';

/// Exercises [ConnectionsHero] across every DovahLink theme, both test sizes, and text scaling.
void main() {
  group('ConnectionsHero renders correctly', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in dovahTestSizes) {
        testWidgets(
          'ConnectionsHero renders under $preset at $size without overflow',
          (WidgetTester tester) async {
            await pumpDovahThemedWidget(
              tester,
              ConnectionsHero(onDiscover: () {}),
              preset: preset,
              size: size,
            );
            final DovahThemeTokens tokens = dovahThemeDataFor(
              preset,
            ).extension<DovahThemeTokens>()!;
            final Text eyebrow = tester.widget(find.text('YOUR SKYRIM'));
            final Text title = tester.widget(
              find.text(tokens.uppercaseLabels ? 'CONNECTIONS' : 'Connections'),
            );

            expect(tester.takeException(), isNull);
            expect(eyebrow.style?.color, tokens.eyebrow);
            expect(title.style?.fontSize, isA<double>());
            expect(title.style?.fontSize, tokens.pageTitleFontSize);
            expect(title.style?.fontFamily, tokens.displayFontFamily);
            expect(
              find.text('Select an available PC to enter its game.'),
              findsOneWidget,
            );
            expect(find.text('Discover Skyrim'), findsOneWidget);
            expect(find.byIcon(Icons.zoom_in), findsOneWidget);
          },
        );
      }
    }

    testWidgets(
      'ConnectionsHero renders at double text scale without overflow',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          Builder(
            builder: (BuildContext context) => MediaQuery(
              data: MediaQuery.of(
                context,
              ).copyWith(textScaler: const TextScaler.linear(2)),
              child: ConnectionsHero(onDiscover: () {}),
            ),
          ),
          preset: DovahThemePreset.hearth,
          size: dovahTestSizes.first,
        );

        expect(tester.takeException(), isNull);
      },
    );
  });

  group('ConnectionsHero calls onDiscover', () {
    testWidgets('ConnectionsHero calls onDiscover when the action is tapped', (
      WidgetTester tester,
    ) async {
      int callCount = 0;
      await pumpDovahThemedWidget(
        tester,
        ConnectionsHero(onDiscover: () => callCount++),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );

      await tester.tap(find.text('Discover Skyrim'));
      await tester.pump();

      expect(callCount, 1);
    });

    testWidgets(
      'ConnectionsHero does not call anything when onDiscover is null',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const ConnectionsHero(onDiscover: null),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        await tester.tap(find.text('Discover Skyrim'), warnIfMissed: false);
        await tester.pump();

        expect(tester.takeException(), isNull);
      },
    );
  });

  group('ConnectionsHero exposes sensible semantics', () {
    testWidgets(
      'ConnectionsHero exposes its title as a header and its action as a button',
      (WidgetTester tester) async {
        final SemanticsHandle semantics = tester.ensureSemantics();
        try {
          await pumpDovahThemedWidget(
            tester,
            ConnectionsHero(onDiscover: () {}),
            preset: DovahThemePreset.frostbound,
            size: dovahTestSizes.first,
          );

          final SemanticsData title = tester
              .getSemantics(find.bySemanticsLabel('Connections'))
              .getSemanticsData();
          final SemanticsData action = tester
              .getSemantics(find.bySemanticsLabel('Discover Skyrim'))
              .getSemanticsData();

          expect(title.flagsCollection.isHeader, isTrue);
          expect(action.flagsCollection.isButton, isTrue);
          expect(action.flagsCollection.isEnabled, Tristate.isTrue);
          await expectLater(tester, meetsGuideline(labeledTapTargetGuideline));
        } finally {
          semantics.dispose();
        }
      },
    );

    testWidgets(
      'ConnectionsHero exposes a disabled action when onDiscover is null',
      (WidgetTester tester) async {
        final SemanticsHandle semantics = tester.ensureSemantics();
        try {
          await pumpDovahThemedWidget(
            tester,
            const ConnectionsHero(onDiscover: null),
            preset: DovahThemePreset.dovah,
            size: dovahTestSizes.first,
          );

          final SemanticsData action = tester
              .getSemantics(find.bySemanticsLabel('Discover Skyrim'))
              .getSemanticsData();

          expect(action.flagsCollection.isEnabled, Tristate.isFalse);
        } finally {
          semantics.dispose();
        }
      },
    );
  });
}
