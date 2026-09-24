import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_dialog.widget.dart';

import 'dovah_widget_test_helpers.dart';

/// Exercises [DovahDialog] across every DovahLink theme and its show/close behavior.
void main() {
  group('DovahDialog renders correctly', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in dovahTestSizes) {
        testWidgets(
          'DovahDialog renders its title and content under $preset at $size without overflow',
          (WidgetTester tester) async {
            await pumpDovahThemedWidget(
              tester,
              const DovahDialog(
                title: 'Appearance',
                child: Text('Dialog body content'),
              ),
              preset: preset,
              size: size,
            );

            expect(tester.takeException(), isNull);
            expect(find.text('Appearance'), findsOneWidget);
            expect(find.text('Dialog body content'), findsOneWidget);
            final Text title = tester.widget(find.text('Appearance'));
            expect(title.style?.fontSize, DovahThemeTokens.dialogTitleFontSize);
          },
        );
      }
    }
  });

  group('DovahDialog.show contains a close affordance', () {
    testWidgets(
      'DovahDialog.show displays the given title and content over a blurred backdrop',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          Builder(
            builder: (BuildContext context) => ElevatedButton(
              onPressed: () => DovahDialog.show<void>(
                context,
                title: 'Appearance',
                child: const Text('Pick a theme'),
              ),
              child: const Text('Open'),
            ),
          ),
          preset: DovahThemePreset.frostbound,
          size: dovahTestSizes.first,
        );

        await tester.tap(find.text('Open'));
        await tester.pumpAndSettle();

        expect(tester.takeException(), isNull);
        expect(find.text('Appearance'), findsOneWidget);
        expect(find.text('Pick a theme'), findsOneWidget);
        expect(find.byType(BackdropFilter), findsOneWidget);
      },
    );

    testWidgets('DovahDialog exposes its title through semantics', (
      WidgetTester tester,
    ) async {
      final SemanticsHandle handle = tester.ensureSemantics();

      await pumpDovahThemedWidget(
        tester,
        const DovahDialog(
          title: 'Appearance',
          child: Text('Dialog body content'),
        ),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );

      expect(find.bySemanticsLabel('Appearance'), findsOneWidget);
      handle.dispose();
    });

    testWidgets(
      'DovahDialog contains a close button tap target of at least 48x48',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const DovahDialog(
            title: 'Appearance',
            child: Text('Dialog body content'),
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        final Size size = tester.getSize(find.byTooltip('Close'));

        expect(
          size.width,
          greaterThanOrEqualTo(DovahThemeTokens.minimumTapTargetSize),
        );
        expect(
          size.height,
          greaterThanOrEqualTo(DovahThemeTokens.minimumTapTargetSize),
        );
      },
    );

    testWidgets(
      'DovahDialog.show lets ink-based content work without its own Material',
      (WidgetTester tester) async {
        int tapCount = 0;
        await pumpDovahThemedWidget(
          tester,
          Builder(
            builder: (BuildContext context) => ElevatedButton(
              onPressed: () => DovahDialog.show<void>(
                context,
                title: 'Appearance',
                child: InkWell(
                  onTap: () => tapCount++,
                  child: const Text('Pick a theme'),
                ),
              ),
              child: const Text('Open'),
            ),
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        await tester.tap(find.text('Open'));
        await tester.pumpAndSettle();
        await tester.tap(find.text('Pick a theme'));
        await tester.pump();

        expect(tester.takeException(), isNull);
        expect(tapCount, 1);
      },
    );

    testWidgets('DovahDialog.show closes when the close button is tapped', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        Builder(
          builder: (BuildContext context) => ElevatedButton(
            onPressed: () => DovahDialog.show<void>(
              context,
              title: 'Appearance',
              child: const Text('Pick a theme'),
            ),
            child: const Text('Open'),
          ),
        ),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );

      await tester.tap(find.text('Open'));
      await tester.pumpAndSettle();
      expect(find.text('Pick a theme'), findsOneWidget);

      await tester.tap(find.byTooltip('Close'));
      await tester.pumpAndSettle();

      expect(find.text('Pick a theme'), findsNothing);
    });

    testWidgets('DovahDialog.show closes when the backdrop is tapped', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        Builder(
          builder: (BuildContext context) => ElevatedButton(
            onPressed: () => DovahDialog.show<void>(
              context,
              title: 'Appearance',
              child: const Text('Pick a theme'),
            ),
            child: const Text('Open'),
          ),
        ),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );

      await tester.tap(find.text('Open'));
      await tester.pumpAndSettle();
      expect(find.text('Pick a theme'), findsOneWidget);

      await tester.tapAt(const Offset(10, 10));
      await tester.pumpAndSettle();

      expect(find.text('Pick a theme'), findsNothing);
      expect(find.text('Open'), findsOneWidget);
    });
  });
}
