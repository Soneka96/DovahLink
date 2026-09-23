import 'dart:ui' show Tristate;

import 'package:flutter/material.dart';
import 'package:flutter/semantics.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_connection_card.widget.dart';

import 'dovah_widget_test_helpers.dart';

/// Exercises [DovahConnectionCard] across every DovahLink theme, every
/// [DovahConnectionCardState], and interaction. Stage 10/11 will map real SDK/discovery/recovery
/// state onto these states in the connected application; this test only proves the shared
/// component renders and behaves correctly for each state, independent of that future work.
void main() {
  group('DovahConnectionCard renders correctly', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final DovahConnectionCardState state
          in DovahConnectionCardState.values) {
        for (final Size size in dovahTestSizes) {
          testWidgets(
            'DovahConnectionCard renders $state under $preset at $size without overflow',
            (WidgetTester tester) async {
              await pumpDovahThemedWidget(
                tester,
                DovahConnectionCard(
                  title: 'Gaming PC',
                  subtitle: 'Skyrim Special Edition',
                  detail: 'Level 43 · Whiterun',
                  state: state,
                ),
                preset: preset,
                size: size,
              );

              expect(tester.takeException(), isNull);
              expect(find.text('Gaming PC'), findsOneWidget);
              expect(find.text(state.label), findsOneWidget);
            },
          );
        }
      }
    }

    testWidgets('DovahConnectionCard contains a chevron when available', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const DovahConnectionCard(
          title: 'Gaming PC',
          subtitle: 'Skyrim Special Edition',
          detail: 'Level 43 · Whiterun',
          state: DovahConnectionCardState.available,
        ),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );

      expect(find.byIcon(Icons.chevron_right), findsOneWidget);
    });

    testWidgets('DovahConnectionCard contains a chevron when it needs repair', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const DovahConnectionCard(
          title: 'Laptop',
          subtitle: 'Skyrim Special Edition',
          detail: 'Trust changed in Skyrim',
          state: DovahConnectionCardState.repair,
        ),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );

      expect(find.byIcon(Icons.chevron_right), findsOneWidget);
    });

    testWidgets('DovahConnectionCard does not contain a chevron when offline', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const DovahConnectionCard(
          title: 'Living Room PC',
          subtitle: 'Skyrim Anniversary Edition',
          detail: 'Last connected yesterday',
          state: DovahConnectionCardState.offline,
        ),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );

      expect(find.byIcon(Icons.chevron_right), findsNothing);
    });
  });

  group('DovahConnectionCard calls onTap', () {
    testWidgets('DovahConnectionCard calls onTap when tapped', (
      WidgetTester tester,
    ) async {
      int tapCount = 0;

      await pumpDovahThemedWidget(
        tester,
        DovahConnectionCard(
          title: 'Gaming PC',
          subtitle: 'Skyrim Special Edition',
          detail: 'Level 43 · Whiterun',
          state: DovahConnectionCardState.available,
          onTap: () => tapCount++,
        ),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );
      await tester.tap(find.text('Gaming PC'));
      await tester.pump();

      expect(tapCount, 1);
    });

    testWidgets('DovahConnectionCard does not call onTap when it has none', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const DovahConnectionCard(
          title: 'Gaming PC',
          subtitle: 'Skyrim Special Edition',
          detail: 'Level 43 · Whiterun',
          state: DovahConnectionCardState.available,
        ),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );
      await tester.tap(find.text('Gaming PC'), warnIfMissed: false);
      await tester.pump();

      expect(tester.takeException(), isNull);
    });
  });

  group('DovahConnectionCard supports keyboard activation', () {
    for (final LogicalKeyboardKey key in <LogicalKeyboardKey>[
      LogicalKeyboardKey.enter,
      LogicalKeyboardKey.space,
    ]) {
      testWidgets('DovahConnectionCard activates on $key when focused', (
        WidgetTester tester,
      ) async {
        int activationCount = 0;
        await pumpDovahThemedWidget(
          tester,
          DovahConnectionCard(
            title: 'Gaming PC',
            subtitle: 'Skyrim Special Edition',
            detail: 'Level 43 · Whiterun',
            state: DovahConnectionCardState.available,
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

    testWidgets('DovahConnectionCard displays its focus outline after Tab', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        Column(
          children: [
            DovahConnectionCard(
              title: 'Gaming PC',
              subtitle: 'Skyrim Special Edition',
              detail: 'Level 43 · Whiterun',
              state: DovahConnectionCardState.available,
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
        find.byKey(const Key('dovah-connection-card-focus-outline')),
        findsOneWidget,
      );

      await tester.sendKeyEvent(LogicalKeyboardKey.tab);
      await tester.pump();
      expect(
        find.byKey(const Key('dovah-connection-card-focus-outline')),
        findsNothing,
      );
    });
  });

  group('DovahConnectionCard exposes button semantics', () {
    testWidgets('DovahConnectionCard exposes enabled state when tappable', (
      WidgetTester tester,
    ) async {
      final SemanticsHandle semantics = tester.ensureSemantics();
      addTearDown(semantics.dispose);
      await pumpDovahThemedWidget(
        tester,
        DovahConnectionCard(
          title: 'Gaming PC',
          subtitle: 'Skyrim Special Edition',
          detail: 'Level 43 · Whiterun',
          state: DovahConnectionCardState.available,
          onTap: () {},
        ),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );

      final SemanticsNode node = tester.getSemantics(
        find.bySemanticsLabel(
          'Gaming PC, Skyrim Special Edition, Level 43 · Whiterun, '
          '${DovahConnectionCardState.available.label}',
        ),
      );
      final SemanticsData data = node.getSemanticsData();
      expect(data.flagsCollection.isButton, isTrue);
      expect(data.flagsCollection.isEnabled, Tristate.isTrue);
      expect(data.hasAction(SemanticsAction.tap), isTrue);
    });

    testWidgets('DovahConnectionCard exposes its label and enabled state', (
      WidgetTester tester,
    ) async {
      final SemanticsHandle semantics = tester.ensureSemantics();
      addTearDown(semantics.dispose);
      await pumpDovahThemedWidget(
        tester,
        const DovahConnectionCard(
          title: 'Gaming PC',
          subtitle: 'Skyrim Special Edition',
          detail: 'Level 43 · Whiterun',
          state: DovahConnectionCardState.available,
          onTap: null,
        ),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );

      final SemanticsNode node = tester.getSemantics(
        find.bySemanticsLabel(
          'Gaming PC, Skyrim Special Edition, Level 43 · Whiterun, '
          '${DovahConnectionCardState.available.label}',
        ),
      );
      final SemanticsData data = node.getSemanticsData();
      expect(data.flagsCollection.isButton, isTrue);
      expect(data.flagsCollection.isEnabled, Tristate.isFalse);
      expect(data.hasAction(SemanticsAction.tap), isFalse);

      await tester.sendKeyEvent(LogicalKeyboardKey.tab);
      await tester.sendKeyEvent(LogicalKeyboardKey.enter);
      await tester.sendKeyEvent(LogicalKeyboardKey.space);
      expect(
        find.byKey(const Key('dovah-connection-card-focus-outline')),
        findsNothing,
      );
    });
  });
}
