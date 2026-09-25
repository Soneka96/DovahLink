import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_materials.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_theme_materials.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_material_painter.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_panel_clipper.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_surface.widget.dart';
import 'dovah_widget_test_helpers.dart';

/// Returns the painter of the [DovahSurface] in the tree.
DovahMaterialPainter findSurfacePainter(WidgetTester tester) =>
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

/// Returns the clipper of the [DovahSurface] in the tree.
DovahPanelClipper findSurfaceClipper(WidgetTester tester) =>
    tester
            .widget<ClipPath>(
              find
                  .descendant(
                    of: find.byType(DovahSurface),
                    matching: find.byType(ClipPath),
                  )
                  .first,
            )
            .clipper!
        as DovahPanelClipper;

/// Exercises [DovahSurface] across every DovahLink theme, material role, and representative
/// landscape size.
void main() {
  group('DovahSurface renders correctly', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in dovahTestSizes) {
        testWidgets(
          'DovahSurface renders its child under $preset at $size without overflow',
          (WidgetTester tester) async {
            await pumpDovahThemedWidget(
              tester,
              const DovahSurface(
                padding: EdgeInsets.all(16),
                child: Text('Surface content'),
              ),
              preset: preset,
              size: size,
            );

            expect(tester.takeException(), isNull);
            expect(find.text('Surface content'), findsOneWidget);
          },
        );
      }
    }

    testWidgets('DovahSurface uses the surface role by default', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const DovahSurface(child: Text('Default')),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );

      expect(findSurfacePainter(tester).material, dovahMaterials.surface);
    });

    testWidgets(
      'DovahSurface does not inset its child by the border width without padding',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const Center(
            child: DovahSurface(child: SizedBox(width: 100, height: 40)),
          ),
          preset: DovahThemePreset.hearth,
          size: dovahTestSizes.first,
        );

        expect(tester.getSize(find.byType(DovahSurface)), const Size(100, 40));
      },
    );
  });

  group('DovahSurface paints the material of its role', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final DovahMaterialRole role in DovahMaterialRole.values) {
        testWidgets(
          'DovahSurface paints the $preset $role material for the $role role',
          (WidgetTester tester) async {
            await pumpDovahThemedWidget(
              tester,
              DovahSurface(role: role, child: const Text('Role content')),
              preset: preset,
              size: dovahTestSizes.first,
            );

            expect(
              findSurfacePainter(tester).material,
              dovahThemeDataFor(
                preset,
              ).extension<DovahThemeMaterials>()!.forRole(role),
            );
          },
        );
      }
    }
  });

  group('DovahSurface keeps the theme corner geometry', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'DovahSurface paints and clips $preset with the theme corner treatment',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            const DovahSurface(child: Text('Geometry')),
            preset: preset,
            size: dovahTestSizes.first,
          );
          final DovahThemeTokens tokens = dovahThemeDataFor(
            preset,
          ).extension<DovahThemeTokens>()!;
          final DovahMaterialPainter painter = findSurfacePainter(tester);
          final DovahPanelClipper clipper = findSurfaceClipper(tester);

          expect(painter.cornerStyle, tokens.cornerStyle);
          expect(painter.cornerRadius, tokens.cornerRadius);
          expect(painter.cutSize, tokens.cornerCutSize);
          expect(clipper.cornerStyle, tokens.cornerStyle);
          expect(clipper.cornerRadius, tokens.cornerRadius);
          expect(clipper.cutSize, tokens.cornerCutSize);
        },
      );
    }

    testWidgets(
      'DovahSurface uses the override radius instead of the theme radius under Hearth',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const DovahSurface(cornerRadius: 20, child: Text('Override')),
          preset: DovahThemePreset.hearth,
          size: dovahTestSizes.first,
        );

        expect(findSurfacePainter(tester).cornerRadius, 20);
        expect(findSurfaceClipper(tester).cornerRadius, 20);
      },
    );

    testWidgets(
      'DovahSurface uses the theme radius under Hearth when no override is given',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const DovahSurface(child: Text('Default')),
          preset: DovahThemePreset.hearth,
          size: dovahTestSizes.first,
        );

        expect(findSurfacePainter(tester).cornerRadius, 13);
      },
    );

    testWidgets(
      'DovahSurface uses the override bevel instead of the theme bevel under Frostbound',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const DovahSurface(cornerCutSize: 11, child: Text('Override')),
          preset: DovahThemePreset.frostbound,
          size: dovahTestSizes.first,
        );

        expect(findSurfacePainter(tester).cutSize, 11);
        expect(findSurfaceClipper(tester).cutSize, 11);
      },
    );

    testWidgets(
      'DovahSurface uses the theme bevel under Frostbound when no override is given',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const DovahSurface(child: Text('Default')),
          preset: DovahThemePreset.frostbound,
          size: dovahTestSizes.first,
        );

        expect(findSurfacePainter(tester).cutSize, 9);
      },
    );
  });
}
