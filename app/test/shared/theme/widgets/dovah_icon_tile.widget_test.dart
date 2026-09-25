import 'dart:math' as math;
import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_icon_tile.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_material_painter.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_surface.widget.dart';
import 'dovah_widget_test_helpers.dart';

/// The side of the transparent probe area the tile is centered in.
const double probeSize = 100;

/// The tile's layout size in every probe.
const double tileSize = 40;

/// A horizontal white bar, so an upright glyph is told apart from a turned one.
const Widget glyphBar = SizedBox(
  width: 20,
  height: 4,
  child: ColoredBox(color: Color(0xFFFFFFFF)),
);

/// Renders a [DovahIconTile] of [rotation] and [cornerRadius] under [preset] and returns its
/// probe-area pixels, the tile centered in a [probeSize] square.
Future<Color Function(double dx, double dy)> renderTile(
  WidgetTester tester, {
  required DovahThemePreset preset,
  required double rotation,
  double cornerRadius = 0,
}) async {
  const Key boundaryKey = Key('tile-boundary');
  await pumpDovahThemedWidget(
    tester,
    Center(
      child: RepaintBoundary(
        key: boundaryKey,
        child: SizedBox(
          width: probeSize,
          height: probeSize,
          child: Center(
            child: DovahIconTile(
              size: tileSize,
              cornerRadius: cornerRadius,
              rotation: rotation,
              child: glyphBar,
            ),
          ),
        ),
      ),
    ),
    preset: preset,
    size: dovahTestSizes.first,
  );
  final RenderRepaintBoundary boundary = tester.renderObject(
    find.byKey(boundaryKey),
  );
  final ByteData data = (await tester.runAsync(() async {
    final ui.Image image = await boundary.toImage();
    return (await image.toByteData())!;
  }))!;

  return (double dx, double dy) {
    final int x = (probeSize / 2 + dx).floor();
    final int y = (probeSize / 2 + dy).floor();
    final int offset = (y * probeSize.toInt() + x) * 4;
    return Color.fromARGB(
      data.getUint8(offset + 3),
      data.getUint8(offset),
      data.getUint8(offset + 1),
      data.getUint8(offset + 2),
    );
  };
}

/// Whether [color] is the white glyph bar.
bool isBar(Color color) =>
    (color.a * 255).round() == 255 && (color.r * 255).round() > 240;

/// Whether [color] is not painted at all.
bool isClear(Color color) => (color.a * 255).round() == 0;

/// Exercises [DovahIconTile]'s size, turn, and upright glyph.
void main() {
  group('DovahIconTile renders correctly', () {
    testWidgets('DovahIconTile keeps its layout size when it turns', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const Center(
          child: DovahIconTile(
            size: tileSize,
            cornerRadius: 0,
            rotation: math.pi / 4,
            child: glyphBar,
          ),
        ),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );

      expect(
        tester.getSize(find.byType(DovahIconTile)),
        const Size.square(tileSize),
      );
    });

    testWidgets(
      'DovahIconTile paints its layout square when it does not turn',
      (WidgetTester tester) async {
        final Color Function(double, double) at = await renderTile(
          tester,
          preset: DovahThemePreset.frostbound,
          rotation: 0,
        );

        expect(isClear(at(19, 19)), isFalse);
        expect(isClear(at(-19, -19)), isFalse);
        expect(isClear(at(26, 0)), isTrue);
      },
    );

    testWidgets('DovahIconTile paints a diamond when it turns a quarter', (
      WidgetTester tester,
    ) async {
      final Color Function(double, double) at = await renderTile(
        tester,
        preset: DovahThemePreset.dovah,
        rotation: math.pi / 4,
      );

      expect(isClear(at(26, 0)), isFalse);
      expect(isClear(at(0, 26)), isFalse);
      expect(isClear(at(-26, 0)), isFalse);
      expect(isClear(at(0, -26)), isFalse);
      expect(isClear(at(19, 19)), isTrue);
      expect(isClear(at(-19, -19)), isTrue);
    });

    testWidgets('DovahIconTile keeps its glyph upright while the tile turns', (
      WidgetTester tester,
    ) async {
      final Color Function(double, double) at = await renderTile(
        tester,
        preset: DovahThemePreset.dovah,
        rotation: math.pi / 4,
      );

      expect(isBar(at(8, 0)), isTrue);
      expect(isBar(at(-8, 0)), isTrue);
      expect(isBar(at(0, 8)), isFalse);
      expect(isBar(at(0, -8)), isFalse);
    });

    testWidgets('DovahIconTile rounds the corners of a diamond tile', (
      WidgetTester tester,
    ) async {
      final Color Function(double, double) at = await renderTile(
        tester,
        preset: DovahThemePreset.dovah,
        rotation: math.pi / 4,
        cornerRadius: 10,
      );

      expect(isClear(at(0, 0)), isFalse);
      expect(isClear(at(27, 0)), isTrue);
      expect(isClear(at(0, -27)), isTrue);
      expect(isBar(at(8, 0)), isTrue);
    });

    testWidgets('DovahIconTile rounds a circle tile by half its size', (
      WidgetTester tester,
    ) async {
      final Color Function(double, double) at = await renderTile(
        tester,
        preset: DovahThemePreset.hearth,
        rotation: 0,
        cornerRadius: tileSize / 2,
      );

      expect(isClear(at(18, 18)), isTrue);
      expect(isClear(at(0, 18)), isFalse);
    });
  });

  group('DovahIconTile paints the theme icon material', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'DovahIconTile paints the icon material as a plain rounded box under $preset',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            const Center(
              child: DovahIconTile(
                size: tileSize,
                cornerRadius: 5,
                rotation: 0,
                child: glyphBar,
              ),
            ),
            preset: preset,
            size: dovahTestSizes.first,
          );
          final DovahSurface surface = tester.widget(
            find.descendant(
              of: find.byType(DovahIconTile),
              matching: find.byType(DovahSurface),
            ),
          );
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

          expect(surface.role, DovahMaterialRole.icon);
          expect(painter.cornerStyle, DovahPanelCornerStyle.rounded);
          expect(painter.cornerRadius, 5);
        },
      );
    }
  });
}
