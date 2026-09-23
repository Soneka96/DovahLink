import 'dart:ui' show Tristate;

import 'package:flutter/material.dart';
import 'package:flutter/semantics.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/appearance/presentation/widgets/appearance_preset_card.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

import '../../../../shared/theme/widgets/dovah_widget_test_helpers.dart';

/// Exercises [AppearancePresetCard] across every DovahLink theme, selected state, and
/// interaction.
void main() {
  group('AppearancePresetCard renders correctly', () {
    for (final DovahThemePreset activeTheme in DovahThemePreset.values) {
      for (final DovahThemePreset previewedPreset in DovahThemePreset.values) {
        for (final Size size in dovahTestSizes) {
          testWidgets(
            'AppearancePresetCard renders $previewedPreset under $activeTheme at $size '
            'without overflow',
            (WidgetTester tester) async {
              await pumpDovahThemedWidget(
                tester,
                AppearancePresetCard(
                  preset: previewedPreset,
                  selected: previewedPreset == activeTheme,
                  onTap: () {},
                ),
                preset: activeTheme,
                size: size,
              );

              expect(tester.takeException(), isNull);
              expect(find.text(previewedPreset.label), findsOneWidget);
            },
          );
        }
      }
    }

    testWidgets('AppearancePresetCard contains a check icon when selected', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        AppearancePresetCard(
          preset: DovahThemePreset.hearth,
          selected: true,
          onTap: () {},
        ),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );

      expect(find.byIcon(Icons.check_circle), findsOneWidget);
    });

    testWidgets(
      'AppearancePresetCard does not contain a check icon when not selected',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          AppearancePresetCard(
            preset: DovahThemePreset.hearth,
            selected: false,
            onTap: () {},
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        expect(find.byIcon(Icons.check_circle), findsNothing);
      },
    );
  });

  group('AppearancePresetCard calls onTap', () {
    testWidgets('AppearancePresetCard calls onTap when tapped', (
      WidgetTester tester,
    ) async {
      int tapCount = 0;

      await pumpDovahThemedWidget(
        tester,
        AppearancePresetCard(
          preset: DovahThemePreset.hearth,
          selected: false,
          onTap: () => tapCount++,
        ),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );
      await tester.tap(find.text(DovahThemePreset.hearth.label));
      await tester.pump();

      expect(tapCount, 1);
    });
  });

  group('AppearancePresetCard supports keyboard activation', () {
    for (final LogicalKeyboardKey key in <LogicalKeyboardKey>[
      LogicalKeyboardKey.enter,
      LogicalKeyboardKey.space,
    ]) {
      testWidgets('AppearancePresetCard activates on $key when focused', (
        WidgetTester tester,
      ) async {
        int activationCount = 0;
        await pumpDovahThemedWidget(
          tester,
          AppearancePresetCard(
            preset: DovahThemePreset.hearth,
            selected: false,
            onTap: () => activationCount++,
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        await tester.sendKeyEvent(LogicalKeyboardKey.tab);
        await tester.sendKeyEvent(key);
        await tester.pump();

        expect(activationCount, 1);
      });
    }

    testWidgets('AppearancePresetCard displays its focus outline after Tab', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        Column(
          children: [
            AppearancePresetCard(
              preset: DovahThemePreset.hearth,
              selected: false,
              onTap: () {},
            ),
            TextButton(onPressed: () {}, child: const Text('Next')),
          ],
        ),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );

      await tester.sendKeyEvent(LogicalKeyboardKey.tab);
      await tester.pump();
      expect(
        find.byKey(const Key('appearance-preset-card-focus-outline')),
        findsOneWidget,
      );

      await tester.sendKeyEvent(LogicalKeyboardKey.tab);
      await tester.pump();
      expect(
        find.byKey(const Key('appearance-preset-card-focus-outline')),
        findsNothing,
      );
    });
  });

  group('AppearancePresetCard exposes button semantics', () {
    testWidgets(
      'AppearancePresetCard exposes its label, enabled, and selected state',
      (WidgetTester tester) async {
        final SemanticsHandle semantics = tester.ensureSemantics();
        addTearDown(semantics.dispose);
        await pumpDovahThemedWidget(
          tester,
          AppearancePresetCard(
            preset: DovahThemePreset.hearth,
            selected: true,
            onTap: () {},
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        final SemanticsNode node = tester.getSemantics(
          find.bySemanticsLabel(DovahThemePreset.hearth.label),
        );
        final SemanticsData data = node.getSemanticsData();
        expect(data.flagsCollection.isButton, isTrue);
        expect(data.flagsCollection.isEnabled, Tristate.isTrue);
        expect(data.flagsCollection.isSelected, Tristate.isTrue);
        expect(data.hasAction(SemanticsAction.tap), isTrue);
      },
    );
  });
}
