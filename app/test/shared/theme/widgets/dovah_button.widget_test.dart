import 'dart:ui' show Tristate;

import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';
import 'package:flutter/semantics.dart';
import 'package:flutter/services.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_button.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_material_painter.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_surface.widget.dart';
import 'dovah_widget_test_helpers.dart';

/// Exercises [DovahButton] across every DovahLink theme, both variants, and interaction.
void main() {
  group('DovahButton renders correctly', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final DovahButtonVariant variant in DovahButtonVariant.values) {
        for (final Size size in dovahTestSizes) {
          testWidgets(
            'DovahButton renders $variant under $preset at $size without overflow',
            (WidgetTester tester) async {
              await pumpDovahThemedWidget(
                tester,
                DovahButton(
                  label: 'Confirm',
                  onPressed: () {},
                  variant: variant,
                ),
                preset: preset,
                size: size,
              );

              expect(tester.takeException(), isNull);
              expect(find.text('Confirm'), findsOneWidget);
            },
          );
        }
      }
    }
  });

  group('DovahButton calls onPressed', () {
    testWidgets('DovahButton calls onPressed when tapped and enabled', (
      WidgetTester tester,
    ) async {
      int tapCount = 0;

      await pumpDovahThemedWidget(
        tester,
        DovahButton(label: 'Confirm', onPressed: () => tapCount++),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );
      await tester.tap(find.text('Confirm'));
      await tester.pump();

      expect(tapCount, 1);
    });

    testWidgets('DovahButton does not call onPressed when disabled', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const DovahButton(label: 'Confirm', onPressed: null),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );
      await tester.tap(find.text('Confirm'), warnIfMissed: false);
      await tester.pump();

      expect(tester.takeException(), isNull);
    });
  });

  group('DovahButton supports keyboard activation', () {
    for (final LogicalKeyboardKey key in <LogicalKeyboardKey>[
      LogicalKeyboardKey.enter,
      LogicalKeyboardKey.space,
    ]) {
      testWidgets('DovahButton activates on $key when focused', (
        WidgetTester tester,
      ) async {
        int activationCount = 0;
        await pumpDovahThemedWidget(
          tester,
          DovahButton(label: 'Confirm', onPressed: () => activationCount++),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        await tester.sendKeyEvent(LogicalKeyboardKey.tab);
        await tester.sendKeyEvent(key);
        await tester.pump();

        expect(activationCount, 1);
      });
    }

    testWidgets('DovahButton displays its focus outline after Tab', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        Column(
          children: [
            DovahButton(label: 'Confirm', onPressed: () {}),
            TextButton(onPressed: () {}, child: const Text('Next')),
          ],
        ),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );

      await tester.sendKeyEvent(LogicalKeyboardKey.tab);
      await tester.pump();
      expect(
        find.byKey(const Key('dovah-button-focus-outline')),
        findsOneWidget,
      );

      await tester.sendKeyEvent(LogicalKeyboardKey.tab);
      await tester.pump();
      expect(find.byKey(const Key('dovah-button-focus-outline')), findsNothing);
    });
  });

  group('DovahButton exposes button semantics', () {
    testWidgets('DovahButton exposes its label and enabled state', (
      WidgetTester tester,
    ) async {
      final SemanticsHandle semantics = tester.ensureSemantics();
      try {
        int tapCount = 0;
        await pumpDovahThemedWidget(
          tester,
          DovahButton(label: 'Confirm', onPressed: () => tapCount++),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        final SemanticsNode node = tester.getSemantics(
          find.byKey(const Key('dovah-button-semantics')),
        );
        final SemanticsData data = node.getSemanticsData();
        expect(data.label, 'Confirm');
        expect(find.bySemanticsLabel('Confirm'), findsOneWidget);
        expect(data.flagsCollection.isButton, isTrue);
        expect(data.flagsCollection.isEnabled, Tristate.isTrue);
        expect(data.hasAction(SemanticsAction.tap), isTrue);
        tester.semantics.performAction(
          find.semantics.byLabel('Confirm'),
          SemanticsAction.tap,
        );
        expect(tapCount, 1);
      } finally {
        semantics.dispose();
      }
    });

    testWidgets('DovahButton exposes disabled state without keyboard focus', (
      WidgetTester tester,
    ) async {
      final SemanticsHandle semantics = tester.ensureSemantics();
      try {
        await pumpDovahThemedWidget(
          tester,
          const DovahButton(label: 'Confirm', onPressed: null),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        final SemanticsNode node = tester.getSemantics(
          find.byKey(const Key('dovah-button-semantics')),
        );
        final SemanticsData data = node.getSemanticsData();
        expect(data.label, 'Confirm');
        expect(find.bySemanticsLabel('Confirm'), findsOneWidget);
        expect(data.flagsCollection.isButton, isTrue);
        expect(data.flagsCollection.isEnabled, Tristate.isFalse);
        expect(data.hasAction(SemanticsAction.tap), isFalse);

        await tester.sendKeyEvent(LogicalKeyboardKey.tab);
        await tester.sendKeyEvent(LogicalKeyboardKey.enter);
        await tester.sendKeyEvent(LogicalKeyboardKey.space);
        expect(
          find.byKey(const Key('dovah-button-focus-outline')),
          findsNothing,
        );
      } finally {
        semantics.dispose();
      }
    });
  });

  group('DovahButton contains a tap target of at least 48x48', () {
    for (final DovahButtonVariant variant in DovahButtonVariant.values) {
      testWidgets(
        'DovahButton contains a $variant tap target of at least 48x48',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            DovahButton(label: 'OK', onPressed: () {}, variant: variant),
            preset: DovahThemePreset.frostbound,
            size: dovahTestSizes.first,
          );

          final Size size = tester.getSize(find.byType(DovahButton));

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
    }
  });

  group('DovahButton preserves the prototype\'s visible geometry', () {
    testWidgets(
      'DovahButton does not stretch its visible surface to the 48dp tap target',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          DovahButton(label: 'X', onPressed: () {}),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        final Size interactiveSize = tester.getSize(find.byType(DovahButton));
        final Size visibleSurfaceSize = tester.getSize(
          find.descendant(
            of: find.byType(DovahButton),
            matching: find.byType(DovahSurface),
          ),
        );

        expect(
          interactiveSize.height,
          greaterThanOrEqualTo(DovahThemeTokens.minimumTapTargetSize),
        );
        expect(
          visibleSurfaceSize.height,
          lessThan(DovahThemeTokens.minimumTapTargetSize),
        );
      },
    );

    testWidgets(
      'DovahButton calls onPressed when tapped in the transparent tap-target margin',
      (WidgetTester tester) async {
        int tapCount = 0;

        await pumpDovahThemedWidget(
          tester,
          DovahButton(label: 'X', onPressed: () => tapCount++),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        final Rect bounds = tester.getRect(find.byType(DovahButton));
        // A point at the very top edge of the >=48dp interactive region: outside the small
        // painted surface (which is centered and shorter than 48dp) but inside DovahButton's
        // own bounds.
        await tester.tapAt(Offset(bounds.center.dx, bounds.top + 1));
        await tester.pump();

        expect(tapCount, 1);
      },
    );
  });

  group('DovahButton uses approved primary action colors', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'DovahButton preserves the approved gradient and label color under $preset',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            const DovahButton(label: 'Confirm', onPressed: null),
            preset: preset,
            size: dovahTestSizes.first,
          );

          final Text text = tester.widget<Text>(find.text('Confirm'));
          final DovahSurface surface = tester.widget(
            find
                .descendant(
                  of: find.byType(DovahButton),
                  matching: find.byType(DovahSurface),
                )
                .first,
          );
          final LinearGradient gradient = surface.gradient! as LinearGradient;
          final Color foreground = text.style!.color!;
          final List<Color> expectedStops = switch (preset) {
            DovahThemePreset.frostbound => const [
              Color(0xFF263239),
              Color(0xFF11191D),
            ],
            DovahThemePreset.dovah => const [
              Color(0xFFF0BD73),
              Color(0xFFC77D38),
            ],
            DovahThemePreset.hearth => const [
              Color(0xFFA96932),
              Color(0xFF82491E),
            ],
          };
          final Alignment expectedBegin = preset == DovahThemePreset.hearth
              ? Alignment.topCenter
              : Alignment.topLeft;
          final Alignment expectedEnd = preset == DovahThemePreset.hearth
              ? Alignment.bottomCenter
              : Alignment.bottomRight;
          final Color expectedForeground = switch (preset) {
            DovahThemePreset.frostbound => const Color(0xFFE9F0F2),
            DovahThemePreset.dovah => const Color(0xFF1A0E04),
            DovahThemePreset.hearth => const Color(0xFFFFF9EE),
          };

          expect(gradient.colors, expectedStops);
          expect(gradient.stops, isNull);
          expect(gradient.begin, expectedBegin);
          expect(gradient.end, expectedEnd);
          expect(foreground, expectedForeground);

          final double foregroundLuminance = foreground.computeLuminance();
          for (int index = 0; index < gradient.colors.length; index++) {
            final double backgroundLuminance = gradient.colors[index]
                .computeLuminance();
            final double lighterLuminance =
                foregroundLuminance > backgroundLuminance
                ? foregroundLuminance
                : backgroundLuminance;
            final double darkerLuminance =
                foregroundLuminance > backgroundLuminance
                ? backgroundLuminance
                : foregroundLuminance;
            final double contrastRatio =
                (lighterLuminance + 0.05) / (darkerLuminance + 0.05);

            if (preset == DovahThemePreset.hearth) {
              // The approved Hearth foreground and bright gradient endpoint intentionally
              // preserve the prototype's 4.22:1 contrast for visual fidelity.
              expect(contrastRatio, closeTo(index == 0 ? 4.22 : 6.86, 0.01));
            } else {
              expect(contrastRatio, greaterThanOrEqualTo(4.5));
            }
          }
        },
      );
    }
  });

  group('DovahButton uses raised material for secondary actions', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'DovahButton uses $preset raised material for the secondary variant',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            const DovahButton(
              label: 'Cancel',
              onPressed: null,
              variant: DovahButtonVariant.secondary,
            ),
            preset: preset,
            size: dovahTestSizes.first,
          );

          final DovahSurface surface = tester.widget(
            find
                .descendant(
                  of: find.byType(DovahButton),
                  matching: find.byType(DovahSurface),
                )
                .first,
          );
          final Text text = tester.widget<Text>(find.text('Cancel'));
          final DovahThemeTokens tokens = dovahThemeDataFor(
            preset,
          ).extension<DovahThemeTokens>()!;
          final Finder surfaceFinder = find
              .descendant(
                of: find.byType(DovahButton),
                matching: find.byType(DovahSurface),
              )
              .first;

          expect(surface.raised, isTrue);
          if (preset == DovahThemePreset.hearth) {
            final Container material = tester.widget(
              find
                  .descendant(
                    of: surfaceFinder,
                    matching: find.byType(Container),
                  )
                  .first,
            );

            expect(
              (material.decoration! as BoxDecoration).gradient,
              tokens.materialRaisedGradient,
            );
            expect(
              (material.decoration! as BoxDecoration).border?.top.color,
              tokens.lineStrong,
            );
          } else {
            final CustomPaint material = tester.widget(
              find
                  .descendant(
                    of: surfaceFinder,
                    matching: find.byType(CustomPaint),
                  )
                  .first,
            );
            final DovahMaterialPainter painter =
                material.painter! as DovahMaterialPainter;

            expect(painter.gradient, tokens.materialRaisedGradient);
            expect(painter.borderColor, tokens.lineStrong);
          }
          expect(text.style?.color, tokens.textPrimary);
        },
      );
    }
  });

  group('DovahButton applies the shared primary hover treatment', () {
    testWidgets(
      'DovahButton completes its hover treatment immediately with reduced motion enabled',
      (WidgetTester tester) async {
        await tester.binding.setSurfaceSize(dovahTestSizes.first);
        addTearDown(() => tester.binding.setSurfaceSize(null));
        await tester.pumpWidget(
          MaterialApp(
            theme: dovahThemeDataFor(DovahThemePreset.dovah),
            home: MediaQuery(
              data: const MediaQueryData(disableAnimations: true),
              child: Scaffold(
                body: DovahButton(label: 'Confirm', onPressed: () {}),
              ),
            ),
          ),
        );
        final TestGesture pointer = await tester.createGesture(
          kind: PointerDeviceKind.mouse,
        );
        await pointer.addPointer(location: const Offset(899, 559));
        await tester.pump();
        await pointer.moveTo(tester.getCenter(find.byType(DovahButton)));
        await tester.pump();

        final TweenAnimationBuilder<double> animation = tester.widget(
          find.byKey(const Key('dovah-button-hover-effect')),
        );

        expect(animation.duration, Duration.zero);
        await tester.pump();
        final Transform transform = tester.widget(
          find.descendant(
            of: find.byKey(const Key('dovah-button-hover-effect')),
            matching: find.byType(Transform),
          ),
        );
        expect(transform.transform.storage[13], closeTo(-1, 0.001));
        await pointer.removePointer();
      },
    );

    testWidgets(
      'DovahButton brightens by seven percent and lifts one pixel while hovered',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          Column(
            children: [
              DovahButton(label: 'Confirm', onPressed: () {}),
              TextButton(onPressed: () {}, child: const Text('Next')),
            ],
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );
        final TestGesture pointer = await tester.createGesture(
          kind: PointerDeviceKind.mouse,
        );
        await pointer.addPointer(location: const Offset(899, 559));
        await tester.pump();
        await pointer.moveTo(tester.getCenter(find.byType(DovahButton)));
        await tester.pump();
        await tester.pump(DovahThemeTokens.buttonHoverDuration);

        final TweenAnimationBuilder<double> animation = tester.widget(
          find.byKey(const Key('dovah-button-hover-effect')),
        );
        final Transform transform = tester.widget(
          find.descendant(
            of: find.byKey(const Key('dovah-button-hover-effect')),
            matching: find.byType(Transform),
          ),
        );

        expect(animation.duration, DovahThemeTokens.buttonHoverDuration);
        expect(
          animation.tween.end,
          1 + DovahThemeTokens.primaryButtonHoverBrightness,
        );
        expect(transform.transform.storage[13], closeTo(-1, 0.001));
        expect(
          find.descendant(
            of: find.byKey(const Key('dovah-button-hover-effect')),
            matching: find.byType(ColorFiltered),
          ),
          findsOneWidget,
        );

        await pointer.moveTo(tester.getCenter(find.text('Next')));
        await tester.pumpAndSettle();
        expect(
          find.descendant(
            of: find.byKey(const Key('dovah-button-hover-effect')),
            matching: find.byType(ColorFiltered),
          ),
          findsNothing,
        );
        await pointer.removePointer();
      },
    );

    testWidgets('DovahButton leaves secondary actions unbrightened on hover', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        DovahButton(
          label: 'Cancel',
          onPressed: () {},
          variant: DovahButtonVariant.secondary,
        ),
        preset: DovahThemePreset.hearth,
        size: dovahTestSizes.first,
      );
      final TestGesture pointer = await tester.createGesture(
        kind: PointerDeviceKind.mouse,
      );
      await pointer.addPointer(location: const Offset(899, 559));
      await tester.pump();
      await pointer.moveTo(tester.getCenter(find.byType(DovahButton)));
      await tester.pump(DovahThemeTokens.buttonHoverDuration);

      final TweenAnimationBuilder<double> animation = tester.widget(
        find.byKey(const Key('dovah-button-hover-effect')),
      );
      expect(animation.tween.end, 1);
      expect(
        find.descendant(
          of: find.byKey(const Key('dovah-button-hover-effect')),
          matching: find.byType(ColorFiltered),
        ),
        findsNothing,
      );
      await pointer.removePointer();
    });

    testWidgets('DovahButton keeps disabled primary actions unbrightened', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const DovahButton(label: 'Confirm', onPressed: null),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );
      final TestGesture pointer = await tester.createGesture(
        kind: PointerDeviceKind.mouse,
      );
      await pointer.addPointer(location: const Offset(899, 559));
      await tester.pump();
      await pointer.moveTo(tester.getCenter(find.byType(DovahButton)));
      await tester.pump(DovahThemeTokens.buttonHoverDuration);

      final TweenAnimationBuilder<double> animation = tester.widget(
        find.byKey(const Key('dovah-button-hover-effect')),
      );
      expect(animation.tween.end, 1);
      expect(
        find.descendant(
          of: find.byKey(const Key('dovah-button-hover-effect')),
          matching: find.byType(ColorFiltered),
        ),
        findsNothing,
      );
      await pointer.removePointer();
    });
  });
}
