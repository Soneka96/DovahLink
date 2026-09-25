import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_material.dart';
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

      expect(
        findSurfacePainter(tester).material,
        DovahThemeMaterials.dovah.surface,
      );
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

  group('DovahSurface outlines each role as the prototype does', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final DovahMaterialRole role in [
        DovahMaterialRole.control,
        DovahMaterialRole.icon,
      ]) {
        testWidgets(
          'DovahSurface rounds a $role surface by the theme radius under $preset',
          (WidgetTester tester) async {
            await pumpDovahThemedWidget(
              tester,
              DovahSurface(role: role, child: const Text('Plain box')),
              preset: preset,
              size: dovahTestSizes.first,
            );
            final DovahThemeTokens tokens = dovahThemeDataFor(
              preset,
            ).extension<DovahThemeTokens>()!;

            expect(
              findSurfacePainter(tester).cornerStyle,
              DovahPanelCornerStyle.rounded,
            );
            expect(
              findSurfacePainter(tester).cornerRadius,
              tokens.cornerRadius,
            );
            expect(
              findSurfaceClipper(tester).cornerStyle,
              DovahPanelCornerStyle.rounded,
            );
          },
        );
      }

      for (final DovahMaterialRole role in [
        DovahMaterialRole.surface,
        DovahMaterialRole.raised,
        DovahMaterialRole.primaryAction,
      ]) {
        testWidgets(
          'DovahSurface keeps the theme corner style on a $role surface under $preset',
          (WidgetTester tester) async {
            await pumpDovahThemedWidget(
              tester,
              DovahSurface(role: role, child: const Text('Clipped box')),
              preset: preset,
              size: dovahTestSizes.first,
            );
            final DovahThemeTokens tokens = dovahThemeDataFor(
              preset,
            ).extension<DovahThemeTokens>()!;

            expect(findSurfacePainter(tester).cornerStyle, tokens.cornerStyle);
          },
        );
      }
    }

    testWidgets(
      'DovahSurface lets an explicit corner style override the role',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const DovahSurface(
            role: DovahMaterialRole.icon,
            cornerStyle: DovahPanelCornerStyle.singleBevel,
            child: Text('Explicit'),
          ),
          preset: DovahThemePreset.hearth,
          size: dovahTestSizes.first,
        );

        expect(
          findSurfacePainter(tester).cornerStyle,
          DovahPanelCornerStyle.singleBevel,
        );
      },
    );

    testWidgets('DovahSurface paints the role shadow by default', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const DovahSurface(child: Text('Shadow')),
        preset: DovahThemePreset.hearth,
        size: dovahTestSizes.first,
      );

      expect(findSurfacePainter(tester).material.shadow, isNotEmpty);
    });

    testWidgets('DovahSurface drops the material shadow when it casts none', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const DovahSurface(castsShadow: false, child: Text('No shadow')),
        preset: DovahThemePreset.hearth,
        size: dovahTestSizes.first,
      );

      expect(findSurfacePainter(tester).material.shadow, isEmpty);
    });
  });

  group('DovahSurface layers decoration and pins a border', () {
    testWidgets(
      'DovahSurface paints an underlay beneath and an overlay above its child',
      (WidgetTester tester) async {
        const _MarkerPainter underlay = _MarkerPainter();
        const _MarkerPainter overlay = _MarkerPainter();
        await pumpDovahThemedWidget(
          tester,
          const DovahSurface(
            underlay: underlay,
            overlay: overlay,
            child: Text('Decorated'),
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );
        final CustomPaint decorated = tester.widget(
          find
              .descendant(
                of: find.byType(ClipPath),
                matching: find.byType(CustomPaint),
              )
              .first,
        );

        expect(decorated.painter, underlay);
        expect(decorated.foregroundPainter, overlay);
      },
    );

    testWidgets(
      'DovahSurface adds no decoration painter without an underlay or overlay',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const DovahSurface(child: Text('Plain')),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        expect(
          find.descendant(
            of: find.byType(ClipPath),
            matching: find.byType(CustomPaint),
          ),
          findsNothing,
        );
      },
    );

    testWidgets(
      'DovahSurface paints a given material instead of the role material',
      (WidgetTester tester) async {
        final DovahMaterial custom = DovahThemeMaterials.frostbound.icon;
        await pumpDovahThemedWidget(
          tester,
          DovahSurface(material: custom, child: const Text('Custom')),
          preset: DovahThemePreset.hearth,
          size: dovahTestSizes.first,
        );

        expect(findSurfacePainter(tester).material, custom);
        expect(
          findSurfacePainter(tester).material,
          isNot(DovahThemeMaterials.hearth.surface),
        );
      },
    );

    testWidgets(
      'DovahSurface keeps the role outline when it paints a given material',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          DovahSurface(
            role: DovahMaterialRole.icon,
            material: DovahThemeMaterials.dovah.surface,
            child: const Text('Custom'),
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        expect(
          findSurfacePainter(tester).cornerStyle,
          DovahPanelCornerStyle.rounded,
        );
      },
    );

    testWidgets('DovahSurface pins the border color over the role material', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const DovahSurface(
          borderColor: Color(0xFF79542F),
          child: Text('Pinned'),
        ),
        preset: DovahThemePreset.hearth,
        size: dovahTestSizes.first,
      );

      expect(
        findSurfacePainter(tester).material,
        DovahThemeMaterials.hearth.surface.withBorderColor(
          const Color(0xFF79542F),
        ),
      );
    });

    testWidgets('DovahSurface keeps the role border without a pinned color', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const DovahSurface(child: Text('Role border')),
        preset: DovahThemePreset.hearth,
        size: dovahTestSizes.first,
      );

      expect(
        findSurfacePainter(tester).material.borderColor,
        DovahThemeMaterials.hearth.surface.borderColor,
      );
    });
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
      'DovahSurface uses the override corner style instead of the theme corner style',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const DovahSurface(
            cornerStyle: DovahPanelCornerStyle.rounded,
            cornerRadius: 6,
            child: Text('Override'),
          ),
          preset: DovahThemePreset.frostbound,
          size: dovahTestSizes.first,
        );

        expect(
          findSurfacePainter(tester).cornerStyle,
          DovahPanelCornerStyle.rounded,
        );
        expect(
          findSurfaceClipper(tester).cornerStyle,
          DovahPanelCornerStyle.rounded,
        );
        expect(findSurfacePainter(tester).cornerRadius, 6);
      },
    );

    testWidgets(
      'DovahSurface keeps the theme radius and bevel when only the corner style is overridden',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const DovahSurface(
            cornerStyle: DovahPanelCornerStyle.rounded,
            child: Text('Style only'),
          ),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        expect(
          findSurfacePainter(tester).cornerStyle,
          DovahPanelCornerStyle.rounded,
        );
        expect(findSurfacePainter(tester).cornerRadius, 3);
        expect(findSurfacePainter(tester).cutSize, 12);
      },
    );

    testWidgets(
      'DovahSurface keeps the theme corner style when no style override is given',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const DovahSurface(child: Text('No override')),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        expect(
          findSurfacePainter(tester).cornerStyle,
          DovahPanelCornerStyle.doubleBevel,
        );
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

/// A painter that paints nothing, so a test can tell where a surface places it.
class _MarkerPainter extends CustomPainter {
  /// Creates a marker painter.
  const _MarkerPainter();

  /// See [CustomPainter.paint].
  @override
  void paint(Canvas canvas, Size size) {}

  /// See [CustomPainter.shouldRepaint].
  @override
  bool shouldRepaint(covariant CustomPainter oldDelegate) => false;
}
