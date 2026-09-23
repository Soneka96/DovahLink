import 'package:flutter/material.dart';
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
}
