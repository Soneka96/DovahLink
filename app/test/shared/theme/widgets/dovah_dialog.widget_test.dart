import 'dart:ui';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_control_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_dialog_metrics.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_dialog.widget.dart';
import 'dovah_widget_test_helpers.dart';

/// Exercises [DovahDialog] across every DovahLink theme and its show/close behavior.
void main() {
  group('DovahDialog renders correctly', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in dovahResponsiveTestSizes) {
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
            expect(title.style?.fontSize, DovahDialogMetrics.titleFontSize);
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
          greaterThanOrEqualTo(DovahControlMetrics.minimumTapTargetSize),
        );
        expect(
          size.height,
          greaterThanOrEqualTo(DovahControlMetrics.minimumTapTargetSize),
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

    testWidgets('DovahDialog calls its supplied close callback', (
      WidgetTester tester,
    ) async {
      int closeCount = 0;
      await pumpDovahThemedWidget(
        tester,
        DovahDialog(
          title: 'Appearance',
          onClose: () => closeCount++,
          child: const Text('Pick a theme'),
        ),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );

      await tester.tap(find.byTooltip('Close'));
      await tester.pump();

      expect(closeCount, 1);
      expect(find.text('Pick a theme'), findsOneWidget);
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

    testWidgets('DovahDialog.show closes when Escape is pressed', (
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

      await tester.sendKeyEvent(LogicalKeyboardKey.escape);
      await tester.pumpAndSettle();

      expect(find.text('Pick a theme'), findsNothing);
      expect(find.text('Open'), findsOneWidget);
    });

    testWidgets(
      'DovahDialog.show keeps long content scrollable within its maximum size',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          Builder(
            builder: (BuildContext context) => ElevatedButton(
              onPressed: () => DovahDialog.show<void>(
                context,
                title: 'Appearance',
                child: Column(
                  children: List<Widget>.generate(
                    40,
                    (int index) => Text('Theme option $index'),
                  ),
                ),
              ),
              child: const Text('Open'),
            ),
          ),
          preset: DovahThemePreset.dovah,
          size: const Size(720, 480),
        );

        await tester.tap(find.text('Open'));
        await tester.pumpAndSettle();

        final Finder dialog = find.byType(DovahDialog);
        final Finder contentScrollView = find.descendant(
          of: dialog,
          matching: find.byType(Scrollable),
        );
        final ScrollableState scrollable = tester.state(contentScrollView);

        expect(tester.takeException(), isNull);
        expect(
          tester.getSize(dialog).height,
          lessThanOrEqualTo(480 - DovahDialogMetrics.backdropPadding * 2),
        );
        expect(scrollable.position.maxScrollExtent, greaterThan(0));

        await tester.drag(contentScrollView, const Offset(0, -160));
        await tester.pumpAndSettle();

        expect(scrollable.position.pixels, greaterThan(0));
        expect(tester.takeException(), isNull);
      },
    );
  });

  group('DovahDialog.showBuilder shows a caller-built dialog', () {
    testWidgets(
      'DovahDialog.showBuilder shows the built dialog over the blurred backdrop and closes on Escape',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          Builder(
            builder: (BuildContext context) => ElevatedButton(
              onPressed: () => DovahDialog.showBuilder<void>(
                context,
                builder: (BuildContext dialogContext) => const DovahDialog(
                  title: 'Built title',
                  child: Text('Built body'),
                ),
              ),
              child: const Text('Open'),
            ),
          ),
          preset: DovahThemePreset.dovah,
          size: dovahResponsiveTestSizes.first,
        );

        await tester.tap(find.text('Open'));
        await tester.pumpAndSettle();

        expect(tester.takeException(), isNull);
        expect(find.text('Built title'), findsOneWidget);
        expect(find.text('Built body'), findsOneWidget);
        expect(find.byType(BackdropFilter), findsOneWidget);

        await tester.sendKeyEvent(LogicalKeyboardKey.escape);
        await tester.pumpAndSettle();

        expect(find.text('Built body'), findsNothing);
      },
    );
  });

  group('DovahDialog matches the prototype modal metrics', () {
    for (final (DovahThemePreset preset, Color scrim, double blur) in [
      (DovahThemePreset.frostbound, const Color(0xC7000204), 7.0),
      (DovahThemePreset.dovah, const Color(0xC2020407), 8.0),
      (DovahThemePreset.hearth, const Color(0x8A2F1F12), 9.0),
    ]) {
      testWidgets(
        'DovahDialog.show scrims with the prototype ${preset.name} backdrop color and blur',
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
            preset: preset,
            size: const Size(900, 560),
          );

          await tester.tap(find.text('Open'));
          await tester.pumpAndSettle();

          final ModalBarrier barrier = tester.widget<ModalBarrier>(
            find.byType(ModalBarrier).last,
          );
          final BackdropFilter backdrop = tester.widget<BackdropFilter>(
            find.byType(BackdropFilter).last,
          );
          expect(barrier.color, scrim);
          expect(backdrop.filter, ImageFilter.blur(sigmaX: blur, sigmaY: blur));
        },
      );
    }

    testWidgets(
      'DovahDialog separates its header from its content with a rule',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const Center(
            child: DovahDialog(title: 'Appearance', child: Text('Body')),
          ),
          preset: DovahThemePreset.dovah,
          size: const Size(900, 560),
        );

        final Container header = tester.widget<Container>(
          find.byKey(const Key('dovah-dialog-header')),
        );
        final BoxDecoration decoration = header.decoration! as BoxDecoration;
        final Border border = decoration.border! as Border;
        expect(border.bottom.width, 1);
        expect(border.bottom.color, isNot(Colors.transparent));
        expect(border.top, BorderSide.none);
      },
    );

    testWidgets(
      'DovahDialog pads its header 12 by 19 and its content 13 by 17 at compact heights',
      (WidgetTester tester) async {
        for (final Size size in const [Size(720, 480), Size(900, 560)]) {
          await pumpDovahThemedWidget(
            tester,
            const Center(
              child: DovahDialog(
                title: 'Appearance',
                child: SizedBox(
                  key: Key('body'),
                  width: double.infinity,
                  height: 20,
                ),
              ),
            ),
            preset: DovahThemePreset.dovah,
            size: size,
          );

          final Container header = tester.widget<Container>(
            find.byKey(const Key('dovah-dialog-header')),
          );
          expect(
            header.padding,
            const EdgeInsets.symmetric(horizontal: 19, vertical: 12),
          );
          final Rect dialog = tester.getRect(find.byType(DovahDialog));
          final Rect body = tester.getRect(find.byKey(const Key('body')));
          expect(body.left - dialog.left, 17);
        }
      },
    );

    testWidgets(
      'DovahDialog pads its header 19 by 22 and its content 22 by 22 at regular heights',
      (WidgetTester tester) async {
        for (final Size size in const [Size(1280, 720), Size(1600, 900)]) {
          await pumpDovahThemedWidget(
            tester,
            const Center(
              child: DovahDialog(
                title: 'Appearance',
                child: SizedBox(
                  key: Key('body'),
                  width: double.infinity,
                  height: 20,
                ),
              ),
            ),
            preset: DovahThemePreset.dovah,
            size: size,
          );

          final Container header = tester.widget<Container>(
            find.byKey(const Key('dovah-dialog-header')),
          );
          expect(
            header.padding,
            const EdgeInsets.symmetric(horizontal: 22, vertical: 19),
          );
          final Rect dialog = tester.getRect(find.byType(DovahDialog));
          final Rect body = tester.getRect(find.byKey(const Key('body')));
          expect(body.left - dialog.left, 22);
        }
      },
    );

    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'DovahDialog pads its header 19 by 22 under ${preset.name} without density scaling',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            const Center(
              child: DovahDialog(title: 'Appearance', child: SizedBox()),
            ),
            preset: preset,
            size: const Size(1280, 720),
          );

          final Container header = tester.widget<Container>(
            find.byKey(const Key('dovah-dialog-header')),
          );
          expect(
            header.padding,
            const EdgeInsets.symmetric(horizontal: 22, vertical: 19),
          );
        },
      );
    }

    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'DovahDialog pads its header 12 by 19 under ${preset.name} at compact heights',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            const Center(
              child: DovahDialog(title: 'Appearance', child: SizedBox()),
            ),
            preset: preset,
            size: const Size(900, 560),
          );

          final Container header = tester.widget<Container>(
            find.byKey(const Key('dovah-dialog-header')),
          );
          expect(
            header.padding,
            const EdgeInsets.symmetric(horizontal: 19, vertical: 12),
          );
        },
      );
    }

    testWidgets('DovahDialog caps its width at 720 on a wide window', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const Center(
          child: DovahDialog(
            title: 'Appearance',
            child: SizedBox(width: 2000, height: 20),
          ),
        ),
        preset: DovahThemePreset.dovah,
        size: const Size(1600, 900),
      );

      expect(tester.getSize(find.byType(DovahDialog)).width, 720);
      // The oversized child overflows its own box, which is not what this asserts.
      tester.takeException();
    });

    testWidgets('DovahDialog fills at most 88 percent of a narrow window', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const Center(
          child: DovahDialog(
            title: 'Appearance',
            child: SizedBox(width: 2000, height: 20),
          ),
        ),
        preset: DovahThemePreset.dovah,
        size: const Size(720, 480),
      );

      expect(tester.getSize(find.byType(DovahDialog)).width, 720 * 0.88);
      tester.takeException();
    });

    testWidgets(
      'DovahDialog fills at most 92 percent of a compact window height',
      (WidgetTester tester) async {
        for (final double height in const [480, 560]) {
          await pumpDovahThemedWidget(
            tester,
            const Center(
              child: DovahDialog(
                title: 'Appearance',
                child: SizedBox(width: 100, height: 3000),
              ),
            ),
            preset: DovahThemePreset.dovah,
            size: Size(900, height),
          );

          expect(
            tester.getSize(find.byType(DovahDialog)).height,
            height * 0.92,
          );
        }
      },
    );

    testWidgets(
      'DovahDialog fills at most 86 percent of a regular window height',
      (WidgetTester tester) async {
        for (final double height in const [720, 900]) {
          await pumpDovahThemedWidget(
            tester,
            const Center(
              child: DovahDialog(
                title: 'Appearance',
                child: SizedBox(width: 100, height: 3000),
              ),
            ),
            preset: DovahThemePreset.dovah,
            size: Size(1280, height),
          );

          expect(
            tester.getSize(find.byType(DovahDialog)).height,
            height * 0.86,
          );
        }
      },
    );
  });
}
