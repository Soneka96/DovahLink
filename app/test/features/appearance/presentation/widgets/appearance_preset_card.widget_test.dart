import 'dart:ui' show Tristate;

import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';
import 'package:flutter/semantics.dart';
import 'package:flutter/services.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/appearance/presentation/widgets/appearance_preset_card.widget.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_appearance_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_control_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_preset_card_style.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_preview_scene.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_theme_materials.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_focus_ring.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_focus_ring_painter.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_material_painter.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_panel_clipper.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_scene.widget.dart';
import '../../../../shared/theme/widgets/dovah_widget_test_helpers.dart';

/// The recipe the preset draws its card with.
DovahPresetCardStyle styleOf(DovahThemePreset preset) =>
    dovahThemeDataFor(preset).extension<DovahThemeMaterials>()!.presetCard;

/// The card's material painter.
DovahMaterialPainter surfacePainter(WidgetTester tester) =>
    tester
            .widget<CustomPaint>(
              find
                  .descendant(
                    of: find.byKey(const Key('appearance-preset-card-surface')),
                    matching: find.byType(CustomPaint),
                  )
                  .first,
            )
            .painter!
        as DovahMaterialPainter;

/// Pumps one card of [preset] at 240px wide under the [active] theme.
Future<void> pumpCard(
  WidgetTester tester, {
  required DovahThemePreset preset,
  bool selected = false,
  DovahThemePreset active = DovahThemePreset.dovah,
  Size size = const Size(1280, 720),
}) => pumpDovahThemedWidget(
  tester,
  Align(
    alignment: Alignment.topLeft,
    child: Padding(
      padding: const EdgeInsets.all(20),
      child: SizedBox(
        width: 240,
        child: AppearancePresetCard(
          preset: preset,
          selected: selected,
          onTap: () {},
        ),
      ),
    ),
  ),
  preset: active,
  size: size,
);

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
  });

  group('AppearancePresetCard has no Material press overlay', () {
    testWidgets(
      'AppearancePresetCard keeps its surface free of splash effects',
      (WidgetTester tester) async {
        await pumpCard(tester, preset: DovahThemePreset.hearth);

        expectNoMaterialOverlay(tester, mouseCursor: SystemMouseCursors.click);
      },
    );
  });

  group('AppearancePresetCard draws each preset as the prototype does', () {
    for (final DovahThemePreset active in DovahThemePreset.values) {
      for (final (
            DovahThemePreset preset,
            DovahPanelCornerStyle style,
            double cut,
            double radius,
          )
          in [
            (
              DovahThemePreset.frostbound,
              DovahPanelCornerStyle.singleBevel,
              9.0,
              0.0,
            ),
            (
              DovahThemePreset.dovah,
              DovahPanelCornerStyle.doubleBevel,
              10.0,
              0.0,
            ),
            (DovahThemePreset.hearth, DovahPanelCornerStyle.rounded, 0.0, 13.0),
          ]) {
        testWidgets(
          'AppearancePresetCard paints the $preset card material and outline while $active is active',
          (WidgetTester tester) async {
            await pumpCard(tester, preset: preset, active: active);
            final DovahMaterialPainter painter = surfacePainter(tester);
            final DovahPanelClipper clipper =
                tester
                        .widget<ClipPath>(
                          find
                              .descendant(
                                of: find.byKey(
                                  const Key('appearance-preset-card-surface'),
                                ),
                                matching: find.byType(ClipPath),
                              )
                              .first,
                        )
                        .clipper!
                    as DovahPanelClipper;

            expect(painter.material, styleOf(preset).material);
            expect(painter.cornerStyle, style);
            expect(painter.cutSize, cut);
            expect(painter.cornerRadius, radius);
            expect(clipper.cornerStyle, style);
            expect(clipper.cutSize, cut);
            expect(clipper.cornerRadius, radius);
          },
        );
      }
    }

    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'AppearancePresetCard keeps the $preset card material when selected',
        (WidgetTester tester) async {
          await pumpCard(
            tester,
            preset: preset,
            selected: true,
            active: preset,
          );

          expect(surfacePainter(tester).material, styleOf(preset).material);
          expect(
            surfacePainter(tester).material,
            isNot(
              dovahThemeDataFor(
                preset,
              ).extension<DovahThemeMaterials>()!.raised,
            ),
          );
        },
      );
    }

    testWidgets(
      'AppearancePresetCard runs its preview inside the card border',
      (WidgetTester tester) async {
        await pumpCard(tester, preset: DovahThemePreset.hearth);
        final Rect card = tester.getRect(
          find.byKey(const Key('appearance-preset-card-surface')),
        );
        final Rect preview = tester.getRect(
          find.byKey(const Key('appearance-preset-card-preview')),
        );

        expect(preview.left, card.left + 1);
        expect(preview.top, card.top + 1);
        expect(preview.right, card.right - 1);
      },
    );
  });

  group('AppearancePresetCard shows its copy', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'AppearancePresetCard shows the $preset name, summary, and materials in its own tones',
        (WidgetTester tester) async {
          await pumpCard(tester, preset: preset);
          final DovahPresetCardStyle style = styleOf(preset);
          final Text title = tester.widget(find.text(preset.label));
          final Text summary = tester.widget(find.text(preset.summary));
          final Text detail = tester.widget(find.text(preset.materials));

          expect(title.style?.color, style.titleColor);
          expect(title.style?.fontSize, DovahAppearanceMetrics.titleFontSize);
          expect(title.style?.fontWeight, FontWeight.w700);
          expect(summary.style?.color, style.summaryColor);
          expect(
            summary.style?.fontSize,
            DovahAppearanceMetrics.summaryFontSize,
          );
          expect(summary.style?.height, DovahAppearanceMetrics.copyLineHeight);
          expect(detail.style?.color, style.detailColor);
          expect(detail.style?.fontSize, DovahAppearanceMetrics.detailFontSize);
          expect(detail.style?.height, DovahAppearanceMetrics.copyLineHeight);
        },
      );
    }

    for (final (Size size, double padding, bool detail) in [
      (const Size(720, 480), 9.0, false),
      (const Size(900, 560), 9.0, false),
      (const Size(1280, 720), 13.0, true),
      (const Size(1600, 900), 13.0, true),
    ]) {
      testWidgets(
        'AppearancePresetCard pads its copy by $padding and ${detail ? 'shows' : 'hides'} the materials line at $size',
        (WidgetTester tester) async {
          await pumpCard(tester, preset: DovahThemePreset.hearth, size: size);
          final Rect card = tester.getRect(
            find.byKey(const Key('appearance-preset-card-surface')),
          );
          final Rect preview = tester.getRect(
            find.byKey(const Key('appearance-preset-card-preview')),
          );
          final Rect title = tester.getRect(
            find.text(DovahThemePreset.hearth.label),
          );

          expect(title.left - card.left, 1 + padding);
          expect(title.top - preview.bottom, padding);
          expect(
            find.text(DovahThemePreset.hearth.materials),
            detail ? findsOneWidget : findsNothing,
          );
          expect(find.text(DovahThemePreset.hearth.summary), findsOneWidget);
        },
      );
    }

    testWidgets('AppearancePresetCard stacks its copy 3px apart', (
      WidgetTester tester,
    ) async {
      await pumpCard(tester, preset: DovahThemePreset.dovah);
      final Rect title = tester.getRect(
        find.text(DovahThemePreset.dovah.label),
      );
      final Rect summary = tester.getRect(
        find.text(DovahThemePreset.dovah.summary),
      );
      final Rect detail = tester.getRect(
        find.text(DovahThemePreset.dovah.materials),
      );

      expect(summary.top - title.bottom, closeTo(3, 0.6));
      expect(detail.top - summary.bottom, closeTo(3, 0.6));
    });
  });

  group('AppearancePresetCard shows its selected badge', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'AppearancePresetCard floats the $preset badge 9px from the top and right when selected',
        (WidgetTester tester) async {
          await pumpCard(
            tester,
            preset: preset,
            selected: true,
            active: preset,
          );
          final Rect card = tester.getRect(
            find.byKey(const Key('appearance-preset-card-surface')),
          );
          final Rect badge = tester.getRect(
            find.byKey(const Key('appearance-preset-card-badge')),
          );
          final DovahPresetCardStyle style = styleOf(preset);
          final Icon check = tester.widget(
            find.descendant(
              of: find.byKey(const Key('appearance-preset-card-badge')),
              matching: find.byIcon(Icons.check),
            ),
          );
          final DecoratedBox disc = tester.widget(
            find.descendant(
              of: find.byKey(const Key('appearance-preset-card-badge')),
              matching: find.byType(DecoratedBox),
            ),
          );

          expect(badge.size, const Size.square(23));
          expect(badge.top, card.top + 1 + 9);
          expect(badge.right, card.right - 1 - 9);
          expect(check.size, 13);
          expect(check.color, style.badgeForeground);
          expect((disc.decoration as BoxDecoration).color, style.badgeFill);
          expect((disc.decoration as BoxDecoration).shape, BoxShape.circle);
        },
      );

      testWidgets(
        'AppearancePresetCard shows no $preset badge when not selected',
        (WidgetTester tester) async {
          await pumpCard(tester, preset: preset);

          expect(
            find.byKey(const Key('appearance-preset-card-badge')),
            findsNothing,
          );
          expect(find.byIcon(Icons.check), findsNothing);
        },
      );
    }
  });

  group('AppearancePresetCard rings the selected card', () {
    DecoratedBox ringOf(WidgetTester tester) => tester.widget(
      find.byKey(const Key('appearance-preset-card-selection-ring')),
    );

    testWidgets(
      'AppearancePresetCard rings a selected Hearth card 2px in the active accent',
      (WidgetTester tester) async {
        await pumpCard(
          tester,
          preset: DovahThemePreset.hearth,
          selected: true,
          active: DovahThemePreset.hearth,
        );
        final BoxDecoration ring = ringOf(tester).decoration as BoxDecoration;

        expect(ring.boxShadow, hasLength(1));
        expect(ring.boxShadow!.single.spreadRadius, 2);
        expect(ring.boxShadow!.single.blurRadius, 0);
        expect(
          ring.boxShadow!.single.color,
          dovahThemeDataFor(
            DovahThemePreset.hearth,
          ).extension<DovahThemeTokens>()!.accentPrimary,
        );
        expect(ring.borderRadius, BorderRadius.circular(13));
      },
    );

    for (final DovahThemePreset preset in [
      DovahThemePreset.frostbound,
      DovahThemePreset.dovah,
    ]) {
      testWidgets(
        'AppearancePresetCard draws no ring around a selected $preset card, whose clip-path clips it',
        (WidgetTester tester) async {
          await pumpCard(
            tester,
            preset: preset,
            selected: true,
            active: preset,
          );

          expect(
            (ringOf(tester).decoration as BoxDecoration).boxShadow,
            isNull,
          );
        },
      );
    }

    testWidgets(
      'AppearancePresetCard rings a selected Hearth card in the active theme accent, not its own',
      (WidgetTester tester) async {
        await pumpCard(
          tester,
          preset: DovahThemePreset.hearth,
          selected: true,
          active: DovahThemePreset.dovah,
        );
        final BoxDecoration ring =
            tester
                    .widget<DecoratedBox>(
                      find.byKey(
                        const Key('appearance-preset-card-selection-ring'),
                      ),
                    )
                    .decoration
                as BoxDecoration;

        expect(
          ring.boxShadow!.single.color,
          dovahThemeDataFor(
            DovahThemePreset.dovah,
          ).extension<DovahThemeTokens>()!.accentPrimary,
        );
      },
    );

    for (final DovahThemePreset preset in [
      DovahThemePreset.frostbound,
      DovahThemePreset.dovah,
    ]) {
      testWidgets(
        'AppearancePresetCard keeps a bevelled $preset card square around its ring slot',
        (WidgetTester tester) async {
          await pumpCard(
            tester,
            preset: preset,
            selected: true,
            active: preset,
          );

          expect(
            (tester
                        .widget<DecoratedBox>(
                          find.byKey(
                            const Key('appearance-preset-card-selection-ring'),
                          ),
                        )
                        .decoration
                    as BoxDecoration)
                .borderRadius,
            BorderRadius.circular(0),
          );
        },
      );
    }

    testWidgets(
      'AppearancePresetCard draws no ring around an unselected card',
      (WidgetTester tester) async {
        await pumpCard(tester, preset: DovahThemePreset.hearth);

        expect((ringOf(tester).decoration as BoxDecoration).boxShadow, isNull);
      },
    );
  });

  group('AppearancePresetCard rises when hovered', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'AppearancePresetCard rises 2px when the $preset card is hovered',
        (WidgetTester tester) async {
          await pumpCard(tester, preset: preset);
          final Finder surface = find.byKey(
            const Key('appearance-preset-card-surface'),
          );
          final Offset rest = tester.getTopLeft(surface);
          final TestGesture pointer = await tester.createGesture(
            kind: PointerDeviceKind.mouse,
          );
          await pointer.addPointer(location: const Offset(1200, 700));
          await tester.pump();

          await pointer.moveTo(tester.getCenter(surface));
          await tester.pump();
          await tester.pump(DovahControlMetrics.liftDuration);

          expect(tester.getTopLeft(surface), rest + const Offset(0, -2));
          await pointer.moveTo(const Offset(1200, 700));
          await tester.pump();
          await tester.pump(DovahControlMetrics.liftDuration);
          expect(tester.getTopLeft(surface), rest);
          await pointer.removePointer();
        },
      );
    }
  });

  group('AppearancePresetCard previews its own theme scene', () {
    for (final DovahThemePreset activeTheme in DovahThemePreset.values) {
      for (final (DovahThemePreset preset, String asset) in [
        (DovahThemePreset.frostbound, frostboundEnvironmentAsset),
        (DovahThemePreset.dovah, dovahConnectionHeroAsset),
        (DovahThemePreset.hearth, hearthEnvironmentAsset),
      ]) {
        testWidgets(
          'AppearancePresetCard shows the $preset scene image while $activeTheme is active',
          (WidgetTester tester) async {
            await pumpDovahThemedWidget(
              tester,
              AppearancePresetCard(
                preset: preset,
                selected: preset == activeTheme,
                onTap: () {},
              ),
              preset: activeTheme,
              size: dovahTestSizes.first,
            );
            final Image image = tester.widget(
              find.descendant(
                of: find.byKey(const Key('appearance-preset-card-preview')),
                matching: find.byType(Image),
              ),
            );

            expect((image.image as AssetImage).assetName, asset);
            expect(image.fit, BoxFit.cover);
          },
        );
      }
    }

    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'AppearancePresetCard draws the $preset scene from its own theme',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            AppearancePresetCard(preset: preset, selected: false, onTap: () {}),
            preset: DovahThemePreset.hearth,
            size: dovahTestSizes.first,
          );
          final DovahScene scene = tester.widget(
            find.descendant(
              of: find.byKey(const Key('appearance-preset-card-preview')),
              matching: find.byType(DovahScene),
            ),
          );
          final DovahPreviewScene expected = dovahThemeDataFor(
            preset,
          ).extension<DovahThemeMaterials>()!.previewScene;

          expect(scene.imageAssetPath, expected.imageAssetPath);
          expect(scene.imageFilter, expected.imageFilter);
          expect(scene.layers, expected.layers);
        },
      );
    }
  });

  group('AppearancePresetCard stretches to its grid row', () {
    testWidgets(
      'AppearancePresetCard fills a taller row with its card surface',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          Align(
            alignment: Alignment.topLeft,
            child: SizedBox(
              width: 240,
              height: 400,
              child: AppearancePresetCard(
                preset: DovahThemePreset.hearth,
                selected: false,
                onTap: () {},
              ),
            ),
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.last,
        );

        expect(
          tester
              .getSize(find.byKey(const Key('appearance-preset-card-surface')))
              .height,
          400,
        );
      },
    );
  });

  group('AppearancePresetCard fits at every responsive size', () {
    for (final DovahThemePreset activeTheme in DovahThemePreset.values) {
      for (final DovahThemePreset previewedPreset in DovahThemePreset.values) {
        for (final Size size in dovahResponsiveTestSizes) {
          testWidgets(
            'AppearancePresetCard renders $previewedPreset under $activeTheme at $size without overflow',
            (WidgetTester tester) async {
              await pumpDovahThemedWidget(
                tester,
                SizedBox(
                  width: 240,
                  child: AppearancePresetCard(
                    preset: previewedPreset,
                    selected: previewedPreset == activeTheme,
                    onTap: () {},
                  ),
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
      expect(find.byKey(DovahFocusRing.ringKey), findsOneWidget);

      await tester.sendKeyEvent(LogicalKeyboardKey.tab);
      await tester.pump();
      expect(find.byKey(DovahFocusRing.ringKey), findsNothing);
    });

    for (final (
          DovahThemePreset activeTheme,
          DovahThemePreset previewedPreset,
          double radius,
        )
        in [
          (DovahThemePreset.dovah, DovahThemePreset.hearth, 13.0),
          (DovahThemePreset.hearth, DovahThemePreset.frostbound, 0.0),
          (DovahThemePreset.frostbound, DovahThemePreset.dovah, 0.0),
        ]) {
      testWidgets(
        'AppearancePresetCard outlines its focus ring in the $activeTheme accent around a $previewedPreset card',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            AppearancePresetCard(
              preset: previewedPreset,
              selected: false,
              onTap: () {},
            ),
            preset: activeTheme,
            size: dovahTestSizes.first,
          );

          await tester.sendKeyEvent(LogicalKeyboardKey.tab);
          await tester.pump();

          final DovahFocusRingPainter painter =
              tester
                      .widget<CustomPaint>(find.byKey(DovahFocusRing.ringKey))
                      .foregroundPainter!
                  as DovahFocusRingPainter;
          expect(
            painter.color,
            dovahThemeDataFor(
              activeTheme,
            ).extension<DovahThemeTokens>()!.accentPrimary,
          );
          expect(painter.cornerRadius, radius);
        },
      );
    }
  });

  group('AppearancePresetCard exposes button semantics', () {
    testWidgets(
      'AppearancePresetCard exposes its label, enabled, and selected state',
      (WidgetTester tester) async {
        final SemanticsHandle semantics = tester.ensureSemantics();
        try {
          int tapCount = 0;
          await pumpDovahThemedWidget(
            tester,
            AppearancePresetCard(
              preset: DovahThemePreset.hearth,
              selected: true,
              onTap: () => tapCount++,
            ),
            preset: DovahThemePreset.dovah,
            size: dovahTestSizes.first,
          );

          final SemanticsNode node = tester.getSemantics(
            find.bySemanticsLabel(DovahThemePreset.hearth.label),
          );
          final SemanticsData data = node.getSemanticsData();
          expect(data.label, DovahThemePreset.hearth.label);
          expect(data.flagsCollection.isButton, isTrue);
          expect(data.flagsCollection.isEnabled, Tristate.isTrue);
          expect(data.flagsCollection.isSelected, Tristate.isTrue);
          expect(data.hasAction(SemanticsAction.tap), isTrue);
          tester.semantics.performAction(
            find.semantics.byLabel(DovahThemePreset.hearth.label),
            SemanticsAction.tap,
          );
          expect(tapCount, 1);
        } finally {
          semantics.dispose();
        }
      },
    );

    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'AppearancePresetCard exposes the $preset preset once and not through its preview',
        (WidgetTester tester) async {
          final SemanticsHandle semantics = tester.ensureSemantics();
          try {
            await pumpDovahThemedWidget(
              tester,
              AppearancePresetCard(
                preset: preset,
                selected: true,
                onTap: () {},
              ),
              preset: DovahThemePreset.dovah,
              size: dovahTestSizes.first,
            );

            expect(find.bySemanticsLabel(preset.label), findsOneWidget);
            expect(
              find.descendant(
                of: find.byKey(const Key('appearance-preset-card-preview')),
                matching: find.byType(Semantics),
              ),
              findsNothing,
            );
          } finally {
            semantics.dispose();
          }
        },
      );
    }

    testWidgets('AppearancePresetCard exposes its unselected state', (
      WidgetTester tester,
    ) async {
      final SemanticsHandle semantics = tester.ensureSemantics();
      try {
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

        final SemanticsNode node = tester.getSemantics(
          find.bySemanticsLabel(DovahThemePreset.hearth.label),
        );
        expect(
          node.getSemanticsData().flagsCollection.isSelected,
          Tristate.isFalse,
        );
      } finally {
        semantics.dispose();
      }
    });
  });
}
