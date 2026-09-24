import 'dart:ui' show Tristate;

import 'package:flutter/material.dart';
import 'package:flutter/semantics.dart';
import 'package:flutter/services.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/appearance/presentation/widgets/appearance_preset_card.widget.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_material_painter.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_panel_clipper.dart';
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
      'AppearancePresetCard uses shared preview and selection-icon metrics',
      (WidgetTester tester) async {
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
        final Size previewSize = tester.getSize(
          find.byKey(const Key('appearance-preset-card-preview')),
        );
        final Icon selectionIcon = tester.widget(
          find.byIcon(Icons.check_circle),
        );

        expect(previewSize.height, appearancePreviewHeight);
        expect(selectionIcon.size, appearanceSelectionIconSize);
      },
    );

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

  group('AppearancePresetCard previews its own theme geometry', () {
    for (final DovahThemePreset activeTheme in DovahThemePreset.values) {
      for (final DovahThemePreset previewedPreset in DovahThemePreset.values) {
        testWidgets('AppearancePresetCard uses $previewedPreset geometry while '
            '$activeTheme is active', (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            AppearancePresetCard(
              preset: previewedPreset,
              selected: previewedPreset == activeTheme,
              onTap: () {},
            ),
            preset: activeTheme,
            size: dovahTestSizes.first,
          );

          final BuildContext surfaceContext = tester.element(
            find.byKey(const Key('appearance-preset-card-surface')),
          );
          final DovahThemeTokens actualTokens = Theme.of(
            surfaceContext,
          ).extension<DovahThemeTokens>()!;
          final DovahThemeTokens expectedTokens = dovahThemeDataFor(
            previewedPreset,
          ).extension<DovahThemeTokens>()!;
          final Finder surfaceFinder = find.byKey(
            const Key('appearance-preset-card-surface'),
          );
          final Gradient expectedSurfaceGradient =
              previewedPreset == activeTheme
              ? expectedTokens.materialRaisedGradient
              : expectedTokens.materialGradient;

          expect(actualTokens.cornerStyle, expectedTokens.cornerStyle);
          expect(actualTokens.cornerRadius, expectedTokens.cornerRadius);
          expect(actualTokens.cornerCutSize, expectedTokens.cornerCutSize);

          if (expectedTokens.cornerStyle == DovahPanelCornerStyle.rounded) {
            final Container paintedSurface = tester.widget(
              find
                  .descendant(
                    of: surfaceFinder,
                    matching: find.byType(Container),
                  )
                  .first,
            );
            final BoxDecoration decoration =
                paintedSurface.decoration! as BoxDecoration;

            expect(
              decoration.borderRadius,
              BorderRadius.circular(expectedTokens.cornerRadius),
            );
            expect(
              (decoration.gradient! as LinearGradient).colors,
              (expectedSurfaceGradient as LinearGradient).colors,
            );
            expect(decoration.border?.top.color, expectedTokens.lineStrong);
          } else {
            final CustomPaint paintedSurface = tester.widget(
              find
                  .descendant(
                    of: surfaceFinder,
                    matching: find.byType(CustomPaint),
                  )
                  .first,
            );
            final DovahMaterialPainter painter =
                paintedSurface.painter! as DovahMaterialPainter;
            final ClipPath clippedSurface = tester.widget(
              find
                  .descendant(
                    of: surfaceFinder,
                    matching: find.byType(ClipPath),
                  )
                  .first,
            );
            final DovahPanelClipper clipper =
                clippedSurface.clipper! as DovahPanelClipper;

            expect(painter.cornerStyle, expectedTokens.cornerStyle);
            expect(painter.cornerRadius, expectedTokens.cornerRadius);
            expect(painter.cutSize, expectedTokens.cornerCutSize);
            expect(
              (painter.gradient as LinearGradient).colors,
              (expectedSurfaceGradient as LinearGradient).colors,
            );
            expect(painter.borderColor, expectedTokens.lineStrong);
            expect(clipper.cornerStyle, expectedTokens.cornerStyle);
            expect(clipper.cornerRadius, expectedTokens.cornerRadius);
            expect(clipper.cutSize, expectedTokens.cornerCutSize);
          }

          final Container preview = tester.widget(
            find.byKey(const Key('appearance-preset-card-preview')),
          );
          final BoxDecoration previewDecoration =
              preview.decoration! as BoxDecoration;
          expect(
            (previewDecoration.gradient! as LinearGradient).colors,
            (expectedTokens.materialGradient as LinearGradient).colors,
          );
          expect(
            previewDecoration.border?.top.color,
            expectedTokens.lineStrong,
          );
        });
      }
    }
  });

  group('AppearancePresetCard uses the Dovah preview asset', () {
    testWidgets(
      'AppearancePresetCard shows the approved hero image in the Dovah preview',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          AppearancePresetCard(
            preset: DovahThemePreset.dovah,
            selected: false,
            onTap: () {},
          ),
          preset: DovahThemePreset.hearth,
          size: dovahTestSizes.first,
        );

        final Container preview = tester.widget(
          find.byKey(const Key('appearance-preset-card-preview')),
        );
        final BoxDecoration decoration = preview.decoration! as BoxDecoration;
        final AssetImage image = decoration.image!.image as AssetImage;

        expect(
          image.assetName,
          'assets/themes/dovah/dovahlink-connection-hero.png',
        );
        expect(decoration.image!.fit, BoxFit.cover);
      },
    );

    for (final DovahThemePreset preset in <DovahThemePreset>[
      DovahThemePreset.frostbound,
      DovahThemePreset.hearth,
    ]) {
      testWidgets(
        'AppearancePresetCard does not use the Dovah image for $preset',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            AppearancePresetCard(preset: preset, selected: false, onTap: () {}),
            preset: DovahThemePreset.dovah,
            size: dovahTestSizes.first,
          );

          final Container preview = tester.widget(
            find.byKey(const Key('appearance-preset-card-preview')),
          );
          final BoxDecoration decoration = preview.decoration! as BoxDecoration;

          expect(decoration.image, isNull);
        },
      );
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
