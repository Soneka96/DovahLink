import 'dart:ui' show Tristate;

import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';
import 'package:flutter/semantics.dart';
import 'package:flutter/services.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_control_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_color_filter.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_linear_layer.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_theme_materials.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_button.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_focus_ring.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_focus_ring_painter.dart';
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

  group('DovahButton renders its label typography', () {
    for (final DovahButtonVariant variant in DovahButtonVariant.values) {
      testWidgets(
        'DovahButton sizes its $variant label with the shared button font size and line height',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            DovahButton(label: 'Confirm', onPressed: () {}, variant: variant),
            preset: DovahThemePreset.dovah,
            size: dovahTestSizes.first,
          );

          final Text text = tester.widget<Text>(find.text('Confirm'));

          expect(text.style?.fontSize, isA<double>());
          expect(
            text.style?.fontSize,
            variant == DovahButtonVariant.quiet
                ? DovahControlMetrics.quietButtonFontSize
                : DovahControlMetrics.buttonFontSize,
          );
          expect(text.style?.height, isA<double>());
          expect(text.style?.height, DovahThemeTokens.bodyLineHeight);
        },
      );
    }
  });

  group('DovahButton renders its icon', () {
    for (final DovahButtonVariant variant in DovahButtonVariant.values) {
      testWidgets(
        'DovahButton displays a $variant icon before its label in the label color',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            DovahButton(
              label: 'Discover',
              onPressed: () {},
              variant: variant,
              icon: Icons.zoom_in,
            ),
            preset: DovahThemePreset.dovah,
            size: dovahTestSizes.first,
          );

          final Icon icon = tester.widget(find.byIcon(Icons.zoom_in));
          final Text text = tester.widget<Text>(find.text('Discover'));

          expect(icon.size, isA<double>());
          expect(icon.size, DovahControlMetrics.buttonIconSize);
          expect(icon.color, text.style!.color);
          expect(
            tester.getTopLeft(find.text('Discover')).dx -
                tester.getTopRight(find.byIcon(Icons.zoom_in)).dx,
            greaterThanOrEqualTo(DovahControlMetrics.buttonIconGap),
          );
          expect(tester.takeException(), isNull);
        },
      );
    }

    testWidgets('DovahButton contains no icon when none is given', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        DovahButton(label: 'Discover', onPressed: () {}),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );

      expect(find.byType(Icon), findsNothing);
    });

    testWidgets('DovahButton keeps its label as the only semantics label', (
      WidgetTester tester,
    ) async {
      final SemanticsHandle semantics = tester.ensureSemantics();
      try {
        await pumpDovahThemedWidget(
          tester,
          DovahButton(label: 'Discover', onPressed: () {}, icon: Icons.zoom_in),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        expect(find.bySemanticsLabel('Discover'), findsOneWidget);
      } finally {
        semantics.dispose();
      }
    });

    testWidgets('DovahButton renders a disabled icon button without error', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const DovahButton(
          label: 'Discover',
          onPressed: null,
          icon: Icons.zoom_in,
        ),
        preset: DovahThemePreset.frostbound,
        size: dovahTestSizes.first,
      );

      expect(find.byIcon(Icons.zoom_in), findsOneWidget);
      expect(tester.takeException(), isNull);
    });

    for (final DovahButtonVariant variant in [
      DovahButtonVariant.secondary,
      DovahButtonVariant.quiet,
    ]) {
      testWidgets('DovahButton does not dim a disabled $variant button', (
        WidgetTester tester,
      ) async {
        await pumpDovahThemedWidget(
          tester,
          DovahButton(label: 'Cancel', onPressed: null, variant: variant),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        final Opacity opacity = tester.widget(
          find.descendant(
            of: find.byType(DovahButton),
            matching: find.byType(Opacity),
          ),
        );
        expect(opacity.opacity, isA<double>());
        expect(opacity.opacity, 1);
      });
    }

    testWidgets('DovahButton dims its disabled appearance', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const DovahButton(label: 'Discover', onPressed: null),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );

      final Opacity opacity = tester.widget(
        find.descendant(
          of: find.byType(DovahButton),
          matching: find.byType(Opacity),
        ),
      );

      expect(opacity.opacity, isA<double>());
      expect(opacity.opacity, DovahControlMetrics.disabledControlOpacity);
    });
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
      expect(find.byKey(DovahFocusRing.ringKey), findsOneWidget);

      await tester.sendKeyEvent(LogicalKeyboardKey.tab);
      await tester.pump();
      expect(find.byKey(DovahFocusRing.ringKey), findsNothing);
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
        expect(find.byKey(DovahFocusRing.ringKey), findsNothing);
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
            greaterThanOrEqualTo(DovahControlMetrics.minimumTapTargetSize),
          );
          expect(
            size.height,
            greaterThanOrEqualTo(DovahControlMetrics.minimumTapTargetSize),
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
          greaterThanOrEqualTo(DovahControlMetrics.minimumTapTargetSize),
        );
        expect(
          visibleSurfaceSize.height,
          lessThan(DovahControlMetrics.minimumTapTargetSize),
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
        'DovahButton preserves the approved primary-action material and label color under $preset',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            DovahButton(label: 'Confirm', onPressed: () {}),
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
          final DovahThemeMaterials materials = dovahThemeDataFor(
            preset,
          ).extension<DovahThemeMaterials>()!;
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
          final DovahLinearLayer fill =
              materials.primaryAction.layers.first as DovahLinearLayer;
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
          final double expectedAngle = preset == DovahThemePreset.hearth
              ? 180
              : 135;
          final Color expectedForeground = switch (preset) {
            DovahThemePreset.frostbound => const Color(0xFFE9F0F2),
            DovahThemePreset.dovah => const Color(0xFF1A0E04),
            DovahThemePreset.hearth => const Color(0xFFFFF9EE),
          };

          expect(surface.role, DovahMaterialRole.primaryAction);
          expect(painter.material, materials.primaryAction);
          expect(fill.colors, expectedStops);
          expect(fill.angleDegrees, expectedAngle);
          expect(foreground, expectedForeground);

          final double foregroundLuminance = foreground.computeLuminance();
          for (int index = 0; index < fill.colors.length; index++) {
            final double backgroundLuminance = fill.colors[index]
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

  group('DovahButton uses control material for secondary actions', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'DovahButton uses $preset control material for the secondary variant',
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
          final DovahMaterialPainter painter =
              tester
                      .widget<CustomPaint>(
                        find
                            .descendant(
                              of: find
                                  .descendant(
                                    of: find.byType(DovahButton),
                                    matching: find.byType(DovahSurface),
                                  )
                                  .first,
                              matching: find.byType(CustomPaint),
                            )
                            .first,
                      )
                      .painter!
                  as DovahMaterialPainter;

          expect(surface.role, DovahMaterialRole.control);
          expect(
            painter.material,
            dovahThemeDataFor(preset).extension<DovahThemeMaterials>()!.control,
          );
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
        await tester.pump(DovahControlMetrics.buttonHoverDuration);

        final TweenAnimationBuilder<double> animation = tester.widget(
          find.byKey(const Key('dovah-button-hover-effect')),
        );
        final Transform transform = tester.widget(
          find.descendant(
            of: find.byKey(const Key('dovah-button-hover-effect')),
            matching: find.byType(Transform),
          ),
        );

        expect(animation.duration, DovahControlMetrics.buttonHoverDuration);
        expect(
          animation.tween.end,
          1 + DovahControlMetrics.primaryButtonHoverBrightness,
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
      await tester.pump(DovahControlMetrics.buttonHoverDuration);

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
      await tester.pump(DovahControlMetrics.buttonHoverDuration);

      final TweenAnimationBuilder<double> animation = tester.widget(
        find.byKey(const Key('dovah-button-hover-effect')),
      );
      expect(animation.tween.end, 1);
      final ColorFiltered filtered = tester.widget(
        find.descendant(
          of: find.byKey(const Key('dovah-button-hover-effect')),
          matching: find.byType(ColorFiltered),
        ),
      );
      expect(
        filtered.colorFilter,
        const DovahColorFilter(
          saturate: DovahControlMetrics.disabledPrimarySaturation,
        ).toColorFilter(),
      );
      await pointer.removePointer();
    });
  });

  group('DovahButton follows the prototype geometry and disabled treatment', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'DovahButton outlines a secondary button as a plain rounded box under $preset',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            DovahButton(
              label: 'Cancel',
              onPressed: () {},
              variant: DovahButtonVariant.secondary,
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

      testWidgets(
        'DovahButton keeps the theme outline on a primary button under $preset',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            DovahButton(label: 'Confirm', onPressed: () {}),
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

          expect(painter.cornerStyle, tokens.cornerStyle);
        },
      );
    }

    testWidgets('DovahButton desaturates a disabled primary button', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const DovahButton(label: 'Confirm', onPressed: null),
        preset: DovahThemePreset.hearth,
        size: dovahTestSizes.first,
      );

      expect(
        find.descendant(
          of: find.byType(DovahButton),
          matching: find.byType(ColorFiltered),
        ),
        findsOneWidget,
      );
    });

    testWidgets('DovahButton does not desaturate an enabled primary button', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        DovahButton(label: 'Confirm', onPressed: () {}),
        preset: DovahThemePreset.hearth,
        size: dovahTestSizes.first,
      );

      expect(
        find.descendant(
          of: find.byType(DovahButton),
          matching: find.byType(ColorFiltered),
        ),
        findsNothing,
      );
    });

    testWidgets('DovahButton does not desaturate a disabled secondary button', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const DovahButton(
          label: 'Cancel',
          onPressed: null,
          variant: DovahButtonVariant.secondary,
        ),
        preset: DovahThemePreset.hearth,
        size: dovahTestSizes.first,
      );

      expect(
        find.descendant(
          of: find.byType(DovahButton),
          matching: find.byType(ColorFiltered),
        ),
        findsNothing,
      );
    });

    testWidgets('DovahButton drops the Hearth shadow of a disabled primary', (
      WidgetTester tester,
    ) async {
      Future<DovahMaterialPainter> painterFor(VoidCallback? onPressed) async {
        await pumpDovahThemedWidget(
          tester,
          DovahButton(label: 'Confirm', onPressed: onPressed),
          preset: DovahThemePreset.hearth,
          size: dovahTestSizes.first,
        );
        return tester
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
      }

      expect((await painterFor(() {})).material.shadow, isNotEmpty);
      expect((await painterFor(null)).material.shadow, isEmpty);
    });
  });

  group('DovahButton follows the prototype padding per variant', () {
    for (final (DovahButtonVariant variant, double vertical, double horizontal)
        in [
          (DovahButtonVariant.primary, 12.0, 17.0),
          (DovahButtonVariant.secondary, 12.0, 16.0),
          (DovahButtonVariant.quiet, 8.0, 11.0),
        ]) {
      testWidgets(
        'DovahButton pads a $variant label by $vertical x $horizontal',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            Center(
              child: DovahButton(
                label: 'Confirm',
                onPressed: () {},
                variant: variant,
              ),
            ),
            preset: DovahThemePreset.dovah,
            size: dovahTestSizes.first,
          );
          final Size label = tester.getSize(find.text('Confirm'));
          final Finder body = variant == DovahButtonVariant.quiet
              ? find.descendant(
                  of: find.byType(DovahButton),
                  matching: find.byType(Padding),
                )
              : find.byType(DovahSurface);

          expect(
            tester.getSize(body.first),
            Size(label.width + 2 * horizontal, label.height + 2 * vertical),
          );
        },
      );
    }
  });

  group('DovahButton draws a quiet button without a surface', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'DovahButton draws a quiet button as a muted label with no surface under $preset',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            DovahButton(
              label: 'Show code again',
              onPressed: () {},
              variant: DovahButtonVariant.quiet,
            ),
            preset: preset,
            size: dovahTestSizes.first,
          );
          final DovahThemeTokens tokens = dovahThemeDataFor(
            preset,
          ).extension<DovahThemeTokens>()!;
          final Text text = tester.widget<Text>(find.text('Show code again'));

          expect(find.byType(DovahSurface), findsNothing);
          expect(text.style?.color, tokens.textMuted);
          expect(text.style?.fontWeight, FontWeight.w700);
        },
      );
    }

    testWidgets(
      'DovahButton outlines a focused quiet button by the theme radius',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          DovahButton(
            label: 'Show code again',
            onPressed: () {},
            variant: DovahButtonVariant.quiet,
          ),
          preset: DovahThemePreset.hearth,
          size: dovahTestSizes.first,
        );

        await tester.sendKeyEvent(LogicalKeyboardKey.tab);
        await tester.pump();

        final DovahFocusRingPainter painter =
            tester
                    .widget<CustomPaint>(find.byKey(DovahFocusRing.ringKey))
                    .foregroundPainter!
                as DovahFocusRingPainter;
        expect(painter.cornerRadius, 13);
      },
    );

    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'DovahButton keeps a quiet label readable on the $preset surface',
        (WidgetTester tester) async {
          final DovahThemeTokens tokens = dovahThemeDataFor(
            preset,
          ).extension<DovahThemeTokens>()!;
          final double lighter =
              tokens.textMuted.computeLuminance() >
                  tokens.surface.computeLuminance()
              ? tokens.textMuted.computeLuminance()
              : tokens.surface.computeLuminance();
          final double darker =
              tokens.textMuted.computeLuminance() >
                  tokens.surface.computeLuminance()
              ? tokens.surface.computeLuminance()
              : tokens.textMuted.computeLuminance();

          expect((lighter + 0.05) / (darker + 0.05), greaterThanOrEqualTo(4.5));
        },
      );
    }

    testWidgets(
      'DovahButton does not lift or brighten a hovered quiet button',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          DovahButton(
            label: 'Show code again',
            onPressed: () {},
            variant: DovahButtonVariant.quiet,
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
        await tester.pump(DovahControlMetrics.buttonHoverDuration);

        final TweenAnimationBuilder<double> animation = tester.widget(
          find.byKey(const Key('dovah-button-hover-effect')),
        );
        expect(animation.tween.end, 1);
        await pointer.removePointer();
      },
    );

    testWidgets('DovahButton calls onPressed for a quiet button', (
      WidgetTester tester,
    ) async {
      int presses = 0;
      await pumpDovahThemedWidget(
        tester,
        DovahButton(
          label: 'Show code again',
          onPressed: () => presses++,
          variant: DovahButtonVariant.quiet,
        ),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );

      await tester.tap(find.text('Show code again'));

      expect(presses, 1);
    });
  });

  group('DovahButton uses the prototype corner radii and padding', () {
    for (final (DovahButtonVariant variant, double radius) in [
      (DovahButtonVariant.primary, 9.0),
      (DovahButtonVariant.secondary, 13.0),
    ]) {
      testWidgets(
        'DovahButton outlines a focused Hearth $variant button with radius $radius',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            DovahButton(label: 'Confirm', onPressed: () {}, variant: variant),
            preset: DovahThemePreset.hearth,
            size: dovahTestSizes.first,
          );

          await tester.sendKeyEvent(LogicalKeyboardKey.tab);
          await tester.pump();

          final CustomPaint outline = tester.widget<CustomPaint>(
            find.byKey(DovahFocusRing.ringKey),
          );
          final DovahFocusRingPainter painter =
              outline.foregroundPainter! as DovahFocusRingPainter;
          expect(painter.cornerRadius, radius);
        },
      );
    }

    for (final (
          DovahThemePreset preset,
          DovahButtonVariant variant,
          double radius,
        )
        in [
          (DovahThemePreset.frostbound, DovahButtonVariant.primary, 0.0),
          (DovahThemePreset.frostbound, DovahButtonVariant.secondary, 0.0),
          (DovahThemePreset.dovah, DovahButtonVariant.primary, 0.0),
          (DovahThemePreset.dovah, DovahButtonVariant.secondary, 3.0),
        ]) {
      testWidgets(
        'DovahButton outlines a focused $preset $variant button with radius $radius',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            DovahButton(label: 'Confirm', onPressed: () {}, variant: variant),
            preset: preset,
            size: dovahTestSizes.first,
          );

          await tester.sendKeyEvent(LogicalKeyboardKey.tab);
          await tester.pump();

          final DovahFocusRingPainter painter =
              tester
                      .widget<CustomPaint>(find.byKey(DovahFocusRing.ringKey))
                      .foregroundPainter!
                  as DovahFocusRingPainter;
          expect(painter.cornerRadius, radius);
        },
      );
    }

    for (final (DovahButtonVariant variant, double radius) in [
      (DovahButtonVariant.primary, 9.0),
      (DovahButtonVariant.secondary, 13.0),
    ]) {
      testWidgets('DovahButton rounds a Hearth $variant button by $radius', (
        WidgetTester tester,
      ) async {
        await pumpDovahThemedWidget(
          tester,
          DovahButton(label: 'Confirm', onPressed: () {}, variant: variant),
          preset: DovahThemePreset.hearth,
          size: dovahTestSizes.first,
        );

        final CustomPaint paint = tester.widget<CustomPaint>(
          find
              .descendant(
                of: find.byType(DovahSurface),
                matching: find.byType(CustomPaint),
              )
              .first,
        );
        expect((paint.painter! as DovahMaterialPainter).cornerRadius, radius);
      });
    }

    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'DovahButton pads its label 12 by 17 under ${preset.name} without density scaling',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            DovahButton(label: 'Confirm', onPressed: () {}),
            preset: preset,
            size: dovahTestSizes.first,
          );

          final Size surface = tester.getSize(find.byType(DovahSurface));
          final Size label = tester.getSize(find.text('Confirm'));
          const double border = 2;
          expect(surface.width - label.width, anyOf(34.0, 34.0 + border));
          expect(surface.height - label.height, anyOf(24.0, 24.0 + border));
        },
      );
    }
  });
}
