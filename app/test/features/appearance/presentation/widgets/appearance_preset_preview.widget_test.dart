import 'dart:math' as math;

import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/appearance/presentation/widgets/appearance_preset_preview.widget.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_preview_scene.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_theme_materials.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_layers_painter.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_scene.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_sigil.widget.dart';
import '../../../../shared/theme/widgets/dovah_widget_test_helpers.dart';

/// Returns the preview scene the preset's own theme defines.
DovahPreviewScene sceneOf(DovahThemePreset preset) =>
    dovahThemeDataFor(preset).extension<DovahThemeMaterials>()!.previewScene;

/// Pumps [AppearancePresetPreview] under [preset]'s theme, so it draws that preset.
Future<void> pumpPreview(
  WidgetTester tester,
  DovahThemePreset preset, {
  Size size = const Size(1280, 720),
}) => pumpDovahThemedWidget(
  tester,
  const SizedBox(width: 240, child: AppearancePresetPreview()),
  preset: preset,
  size: size,
);

/// Exercises [AppearancePresetPreview]'s scene, sigil, accent bars, sizing, and semantics.
void main() {
  group('AppearancePresetPreview draws the theme scene', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'AppearancePresetPreview draws the $preset scene image, treatment, and layers',
        (WidgetTester tester) async {
          await pumpPreview(tester, preset);
          final DovahScene scene = tester.widget(find.byType(DovahScene));
          final DovahPreviewScene expected = sceneOf(preset);

          expect(scene.imageAssetPath, expected.imageAssetPath);
          expect(scene.imageFilter, expected.imageFilter);
          expect(scene.layers, expected.layers);
        },
      );
    }

    for (final (DovahThemePreset preset, String asset) in [
      (DovahThemePreset.frostbound, frostboundEnvironmentAsset),
      (DovahThemePreset.dovah, dovahConnectionHeroAsset),
      (DovahThemePreset.hearth, hearthEnvironmentAsset),
    ]) {
      testWidgets('AppearancePresetPreview renders the $preset asset', (
        WidgetTester tester,
      ) async {
        await pumpPreview(tester, preset);
        final Image image = tester.widget(find.byType(Image));

        expect((image.image as AssetImage).assetName, asset);
      });
    }

    for (final DovahThemePreset preset in [
      DovahThemePreset.frostbound,
      DovahThemePreset.hearth,
    ]) {
      testWidgets(
        'AppearancePresetPreview applies the $preset color treatment to the scene',
        (WidgetTester tester) async {
          await pumpPreview(tester, preset);

          expect(
            find.descendant(
              of: find.byType(DovahScene),
              matching: find.byType(ColorFiltered),
            ),
            findsOneWidget,
          );
        },
      );
    }

    testWidgets(
      'AppearancePresetPreview applies no color treatment to the Dovah scene',
      (WidgetTester tester) async {
        await pumpPreview(tester, DovahThemePreset.dovah);

        expect(
          find.descendant(
            of: find.byType(DovahScene),
            matching: find.byType(ColorFiltered),
          ),
          findsNothing,
        );
      },
    );
  });

  group('AppearancePresetPreview draws the theme sigil', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'AppearancePresetPreview sizes and colors the $preset sigil tile',
        (WidgetTester tester) async {
          await pumpPreview(tester, preset);
          final Finder tileFinder = find.ancestor(
            of: find.byType(DovahSigil),
            matching: find.byType(Container),
          );
          final Container tile = tester.widget(tileFinder.first);
          final BoxDecoration decoration = tile.decoration! as BoxDecoration;

          expect(tester.getSize(tileFinder.first), const Size(45, 45));
          expect(decoration.color, sceneOf(preset).sigil.fill);
          expect(decoration.border?.top.color, sceneOf(preset).sigil.border);
          expect(tester.getSize(find.byType(DovahSigil)), const Size(29, 29));
        },
      );
    }

    testWidgets(
      'AppearancePresetPreview keeps the Frostbound sigil square and upright',
      (WidgetTester tester) async {
        await pumpPreview(tester, DovahThemePreset.frostbound);
        final Container tile = tester.widget(
          find
              .ancestor(
                of: find.byType(DovahSigil),
                matching: find.byType(Container),
              )
              .first,
        );

        expect((tile.decoration! as BoxDecoration).shape, BoxShape.rectangle);
        expect(
          find.ancestor(
            of: find.byType(Container),
            matching: find.byWidgetPredicate(
              (Widget widget) =>
                  widget is Transform &&
                  widget.transform.getRotation().entry(0, 1).abs() > 0.5,
            ),
          ),
          findsNothing,
        );
      },
    );

    testWidgets(
      'AppearancePresetPreview turns the Dovah sigil tile 45 degrees and its mark back upright',
      (WidgetTester tester) async {
        await pumpPreview(tester, DovahThemePreset.dovah);
        final List<Transform> rotations = tester
            .widgetList<Transform>(
              find.ancestor(
                of: find.byType(DovahSigil),
                matching: find.byType(Transform),
              ),
            )
            .toList();
        final List<double> angles = [
          for (final Transform rotation in rotations)
            math.atan2(
              rotation.transform.entry(1, 0),
              rotation.transform.entry(0, 0),
            ),
        ];

        expect(
          angles.any((double a) => (a - math.pi / 4).abs() < 1e-6),
          isTrue,
        );
        expect(
          angles.any((double a) => (a + math.pi / 4).abs() < 1e-6),
          isTrue,
        );
      },
    );

    testWidgets(
      'AppearancePresetPreview draws the Hearth sigil tile as a circle',
      (WidgetTester tester) async {
        await pumpPreview(tester, DovahThemePreset.hearth);
        final Container tile = tester.widget(
          find
              .ancestor(
                of: find.byType(DovahSigil),
                matching: find.byType(Container),
              )
              .first,
        );

        expect((tile.decoration! as BoxDecoration).shape, BoxShape.circle);
      },
    );

    testWidgets(
      'AppearancePresetPreview tints the Frostbound and Hearth marks but not the Dovah mark',
      (WidgetTester tester) async {
        for (final (DovahThemePreset preset, bool tinted) in [
          (DovahThemePreset.frostbound, true),
          (DovahThemePreset.dovah, false),
          (DovahThemePreset.hearth, true),
        ]) {
          await pumpPreview(tester, preset);

          expect(
            find
                .ancestor(
                  of: find.byType(DovahSigil),
                  matching: find.byType(ColorFiltered),
                )
                .evaluate()
                .isNotEmpty,
            tinted,
            reason: '$preset mark treatment',
          );
        }
      },
    );
  });

  group('AppearancePresetPreview draws the accent bars', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'AppearancePresetPreview draws three $preset bars with the prototype proportions',
        (WidgetTester tester) async {
          await pumpPreview(tester, preset);
          final Finder painters = find.byWidgetPredicate(
            (Widget widget) =>
                widget is CustomPaint &&
                widget.painter is DovahLayersPainter &&
                (widget.painter! as DovahLayersPainter).layers.length == 1 &&
                (widget.painter! as DovahLayersPainter).layers.single ==
                    sceneOf(preset).barFill,
          );
          final List<Size> sizes = [
            for (final Element element in painters.evaluate()) element.size!,
          ];

          expect(sizes, hasLength(3));
          expect(sizes.every((Size size) => size.height == 6), isTrue);
          const double usable = 240 - 2 * 12 - 2 * 4;
          expect(sizes[0].width, closeTo(usable * 140 / 265, 0.5));
          expect(sizes[1].width, closeTo(usable * 80 / 265, 0.5));
          expect(sizes[2].width, closeTo(usable * 45 / 265, 0.5));
        },
      );
    }

    testWidgets(
      'AppearancePresetPreview draws a red edge on each Frostbound bar',
      (WidgetTester tester) async {
        await pumpPreview(tester, DovahThemePreset.frostbound);
        final Finder edges = find.byWidgetPredicate(
          (Widget widget) =>
              widget is ColoredBox && widget.color == const Color(0xFFA43B40),
        );

        expect(edges, findsNWidgets(3));
        expect(tester.getSize(edges.first).width, 2);
      },
    );

    for (final DovahThemePreset preset in [
      DovahThemePreset.dovah,
      DovahThemePreset.hearth,
    ]) {
      testWidgets('AppearancePresetPreview draws no edge on $preset bars', (
        WidgetTester tester,
      ) async {
        await pumpPreview(tester, preset);

        expect(
          find.byWidgetPredicate(
            (Widget widget) => widget is Positioned && widget.width == 2,
          ),
          findsNothing,
        );
      });
    }

    testWidgets('AppearancePresetPreview rounds the Hearth bars by 4', (
      WidgetTester tester,
    ) async {
      await pumpPreview(tester, DovahThemePreset.hearth);
      final List<ClipRRect> clips = tester
          .widgetList<ClipRRect>(find.byType(ClipRRect))
          .toList();

      expect(clips, hasLength(3));
      expect(clips.first.borderRadius, BorderRadius.circular(4));
    });

    for (final DovahThemePreset preset in [
      DovahThemePreset.frostbound,
      DovahThemePreset.dovah,
    ]) {
      testWidgets('AppearancePresetPreview leaves the $preset bars square', (
        WidgetTester tester,
      ) async {
        await pumpPreview(tester, preset);

        expect(find.byType(ClipRRect), findsNothing);
      });
    }

    testWidgets(
      'AppearancePresetPreview insets the bars from the left, right, and bottom edges',
      (WidgetTester tester) async {
        await pumpPreview(tester, DovahThemePreset.dovah);
        final Rect preview = tester.getRect(
          find.byType(AppearancePresetPreview),
        );
        final Rect bars = tester.getRect(find.byType(Row).first);

        expect(bars.left - preview.left, 12);
        expect(preview.right - bars.right, 12);
        expect(preview.bottom - bars.bottom, 10);
      },
    );
  });

  group('AppearancePresetPreview sizes itself for the window', () {
    for (final (Size size, double height) in [
      (const Size(720, 480), 78.0),
      (const Size(900, 560), 78.0),
      (const Size(1280, 720), 112.0),
      (const Size(1600, 900), 112.0),
    ]) {
      for (final DovahThemePreset preset in DovahThemePreset.values) {
        testWidgets(
          'AppearancePresetPreview is $height tall and fills its width for $preset at $size',
          (WidgetTester tester) async {
            await pumpPreview(tester, preset, size: size);

            expect(
              tester.getSize(find.byType(AppearancePresetPreview)),
              Size(240, height),
            );
            expect(tester.takeException(), isNull);
          },
        );
      }
    }
  });

  group('AppearancePresetPreview stays out of accessibility', () {
    testWidgets('AppearancePresetPreview excludes itself from semantics', (
      WidgetTester tester,
    ) async {
      await pumpPreview(tester, DovahThemePreset.dovah);

      expect(
        find.descendant(
          of: find.byType(AppearancePresetPreview),
          matching: find.byType(ExcludeSemantics),
        ),
        findsWidgets,
      );
    });
  });
}
