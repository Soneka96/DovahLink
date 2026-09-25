import 'dart:ui' show Tristate;

import 'package:flutter/material.dart';
import 'package:flutter/semantics.dart';
import 'package:flutter/services.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_control_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_focus_ring.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_icon_button.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_material_painter.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_surface.widget.dart';
import 'dovah_widget_test_helpers.dart';

/// Exercises [DovahIconButton] across every DovahLink theme, both test sizes, and interaction.
void main() {
  group('DovahIconButton outlines its surface as the prototype does', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'DovahIconButton paints a plain rounded box by the theme radius under $preset',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            DovahIconButton(
              icon: Icons.settings_outlined,
              label: 'Appearance settings',
              onPressed: () {},
            ),
            preset: preset,
            size: dovahTestSizes.first,
          );
          final DovahThemeTokens tokens = dovahThemeDataFor(
            preset,
          ).extension<DovahThemeTokens>()!;
          final DovahMaterialPainter painter =
              tester
                      .widget<CustomPaint>(
                        find
                            .descendant(
                              of: find.byType(DovahSurface),
                              matching: find.byType(CustomPaint),
                            )
                            .first,
                      )
                      .painter!
                  as DovahMaterialPainter;

          expect(painter.cornerStyle, DovahPanelCornerStyle.rounded);
          expect(painter.cornerRadius, tokens.cornerRadius);
        },
      );
    }
  });

  group('DovahIconButton renders correctly', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in dovahTestSizes) {
        testWidgets(
          'DovahIconButton renders under $preset at $size without overflow',
          (WidgetTester tester) async {
            await pumpDovahThemedWidget(
              tester,
              DovahIconButton(
                icon: Icons.settings_outlined,
                label: 'Appearance settings',
                onPressed: () {},
              ),
              preset: preset,
              size: size,
            );

            final Icon icon = tester.widget(
              find.byIcon(Icons.settings_outlined),
            );
            final Size surface = tester.getSize(
              find.descendant(
                of: find.byType(DovahIconButton),
                matching: find.byType(DovahSurface),
              ),
            );

            expect(tester.takeException(), isNull);
            expect(icon.size, isA<double>());
            expect(icon.size, DovahControlMetrics.iconButtonIconSize);
            expect(surface.width, DovahControlMetrics.iconButtonSize);
            expect(surface.height, DovahControlMetrics.iconButtonSize);
          },
        );
      }
    }
  });

  group('DovahIconButton calls onPressed', () {
    testWidgets('DovahIconButton calls onPressed when tapped and enabled', (
      WidgetTester tester,
    ) async {
      int tapCount = 0;

      await pumpDovahThemedWidget(
        tester,
        DovahIconButton(
          icon: Icons.settings_outlined,
          label: 'Appearance settings',
          onPressed: () => tapCount++,
        ),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );
      await tester.tap(find.byIcon(Icons.settings_outlined));
      await tester.pump();

      expect(tapCount, 1);
    });

    testWidgets('DovahIconButton does not throw when tapped while disabled', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const DovahIconButton(
          icon: Icons.settings_outlined,
          label: 'Appearance settings',
          onPressed: null,
        ),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );
      await tester.tap(
        find.byIcon(Icons.settings_outlined),
        warnIfMissed: false,
      );
      await tester.pump();

      expect(tester.takeException(), isNull);
    });
  });

  group('DovahIconButton supports keyboard activation', () {
    for (final LogicalKeyboardKey key in <LogicalKeyboardKey>[
      LogicalKeyboardKey.enter,
      LogicalKeyboardKey.space,
    ]) {
      testWidgets('DovahIconButton activates on $key when focused', (
        WidgetTester tester,
      ) async {
        int activationCount = 0;
        await pumpDovahThemedWidget(
          tester,
          DovahIconButton(
            icon: Icons.settings_outlined,
            label: 'Appearance settings',
            onPressed: () => activationCount++,
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

    testWidgets('DovahIconButton displays its focus outline after Tab', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        Column(
          children: [
            DovahIconButton(
              icon: Icons.settings_outlined,
              label: 'Appearance settings',
              onPressed: () {},
            ),
            TextButton(onPressed: () {}, child: const Text('Next')),
          ],
        ),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );

      await tester.sendKeyEvent(LogicalKeyboardKey.tab);
      await tester.pump();
      expect(find.byKey(DovahFocusRing.ringKey), findsOneWidget);

      await tester.sendKeyEvent(LogicalKeyboardKey.tab);
      await tester.pump();
      expect(find.byKey(DovahFocusRing.ringKey), findsNothing);
    });
  });

  group('DovahIconButton renders its disabled and tooltip states', () {
    testWidgets('DovahIconButton dims itself and skips focus when disabled', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const DovahIconButton(
          icon: Icons.settings_outlined,
          label: 'Appearance settings',
          onPressed: null,
        ),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );

      await tester.sendKeyEvent(LogicalKeyboardKey.tab);
      await tester.pump();
      final Opacity opacity = tester.widget(
        find.descendant(
          of: find.byType(DovahIconButton),
          matching: find.byType(Opacity),
        ),
      );

      expect(opacity.opacity, isA<double>());
      expect(opacity.opacity, DovahControlMetrics.disabledControlOpacity);
      expect(find.byKey(DovahFocusRing.ringKey), findsNothing);
    });

    testWidgets('DovahIconButton does not dim itself when enabled', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        DovahIconButton(
          icon: Icons.settings_outlined,
          label: 'Appearance settings',
          onPressed: () {},
        ),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );

      final Opacity opacity = tester.widget(
        find.descendant(
          of: find.byType(DovahIconButton),
          matching: find.byType(Opacity),
        ),
      );

      expect(opacity.opacity, isA<double>());
      expect(opacity.opacity, 1);
    });

    testWidgets(
      'DovahIconButton displays its label as a tooltip on long press',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          DovahIconButton(
            icon: Icons.settings_outlined,
            label: 'Appearance settings',
            onPressed: () {},
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        await tester.longPress(find.byType(DovahIconButton));
        await tester.pump();

        expect(find.text('Appearance settings'), findsOneWidget);
      },
    );
  });

  group('DovahIconButton exposes button semantics', () {
    testWidgets('DovahIconButton exposes its label and enabled state', (
      WidgetTester tester,
    ) async {
      final SemanticsHandle semantics = tester.ensureSemantics();
      try {
        int tapCount = 0;
        await pumpDovahThemedWidget(
          tester,
          DovahIconButton(
            icon: Icons.settings_outlined,
            label: 'Appearance settings',
            onPressed: () => tapCount++,
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        final SemanticsData data = tester
            .getSemantics(find.byKey(const Key('dovah-icon-button-semantics')))
            .getSemanticsData();
        expect(data.label, 'Appearance settings');
        expect(find.bySemanticsLabel('Appearance settings'), findsOneWidget);
        expect(data.flagsCollection.isButton, isTrue);
        expect(data.flagsCollection.isEnabled, Tristate.isTrue);
        expect(data.hasAction(SemanticsAction.tap), isTrue);
        tester.semantics.performAction(
          find.semantics.byLabel('Appearance settings'),
          SemanticsAction.tap,
        );
        expect(tapCount, 1);
      } finally {
        semantics.dispose();
      }
    });

    testWidgets('DovahIconButton exposes disabled state without a tap action', (
      WidgetTester tester,
    ) async {
      final SemanticsHandle semantics = tester.ensureSemantics();
      try {
        await pumpDovahThemedWidget(
          tester,
          const DovahIconButton(
            icon: Icons.settings_outlined,
            label: 'Appearance settings',
            onPressed: null,
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        final SemanticsData data = tester
            .getSemantics(find.byKey(const Key('dovah-icon-button-semantics')))
            .getSemanticsData();
        expect(data.flagsCollection.isEnabled, Tristate.isFalse);
        expect(data.hasAction(SemanticsAction.tap), isFalse);
      } finally {
        semantics.dispose();
      }
    });
  });

  group('DovahIconButton contains a tap target of at least 48x48', () {
    testWidgets('DovahIconButton contains a tap target of at least 48x48', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        DovahIconButton(
          icon: Icons.settings_outlined,
          label: 'Appearance settings',
          onPressed: () {},
        ),
        preset: DovahThemePreset.frostbound,
        size: dovahTestSizes.first,
      );

      final Size size = tester.getSize(find.byType(DovahIconButton));

      expect(
        size.width,
        greaterThanOrEqualTo(DovahControlMetrics.minimumTapTargetSize),
      );
      expect(
        size.height,
        greaterThanOrEqualTo(DovahControlMetrics.minimumTapTargetSize),
      );
    });
  });

  group('DovahIconButton uses theme material', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'DovahIconButton uses the raised surface and primary text color under $preset',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            DovahIconButton(
              icon: Icons.settings_outlined,
              label: 'Appearance settings',
              onPressed: () {},
            ),
            preset: preset,
            size: dovahTestSizes.first,
          );

          final DovahSurface surface = tester.widget(
            find.descendant(
              of: find.byType(DovahIconButton),
              matching: find.byType(DovahSurface),
            ),
          );
          final Icon icon = tester.widget(find.byIcon(Icons.settings_outlined));
          final DovahThemeTokens tokens = dovahThemeDataFor(
            preset,
          ).extension<DovahThemeTokens>()!;

          expect(surface.role, DovahMaterialRole.control);
          expect(icon.color, tokens.textPrimary);
        },
      );
    }
  });
}
