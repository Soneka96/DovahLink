import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_theme_materials.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_brand_mark.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_sigil.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_sigil_painter.dart';
import 'dovah_widget_test_helpers.dart';

/// The side of the transparent probe area the mark is centered in.
const double probeSize = 120;

/// The mark's size in every probe.
const double markSize = 44;

/// Renders a [DovahBrandMark] under [preset] and returns a pixel reader, in mark coordinates: the
/// mark's top-left is (0, 0), so a glow outside it reads at a negative coordinate.
Future<Color Function(int x, int y)> renderMark(
  WidgetTester tester,
  DovahThemePreset preset,
) async {
  const Key boundaryKey = Key('mark-boundary');
  await pumpDovahThemedWidget(
    tester,
    const Center(
      child: RepaintBoundary(
        key: boundaryKey,
        child: SizedBox(
          width: probeSize,
          height: probeSize,
          child: Center(child: DovahBrandMark(size: markSize)),
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
  const int origin = (probeSize - markSize) ~/ 2;

  return (int x, int y) {
    final int offset = ((y + origin) * probeSize.toInt() + x + origin) * 4;
    return Color.fromARGB(
      data.getUint8(offset + 3),
      data.getUint8(offset),
      data.getUint8(offset + 1),
      data.getUint8(offset + 2),
    );
  };
}

/// Exercises [DovahBrandMark] across every DovahLink theme.
void main() {
  group('DovahBrandMark renders correctly', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'DovahBrandMark keeps its layout size and one sigil under $preset',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            const Center(child: DovahBrandMark(size: markSize)),
            preset: preset,
            size: dovahTestSizes.first,
          );

          expect(
            tester.getSize(find.byType(DovahBrandMark)),
            const Size.square(markSize),
          );
          expect(find.byType(DovahSigil), findsOneWidget);
          expect(tester.takeException(), isNull);
        },
      );
    }
  });

  group('DovahBrandMark dresses the sigil in the theme treatment', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'DovahBrandMark applies the $preset color treatment and glow',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            const Center(child: DovahBrandMark(size: markSize)),
            preset: preset,
            size: dovahTestSizes.first,
          );
          final treatment = dovahThemeDataFor(
            preset,
          ).extension<DovahThemeMaterials>()!.brandMark;

          if (treatment.filter.isNeutral) {
            expect(find.byType(ColorFiltered), findsNothing);
          } else {
            final ColorFiltered filtered = tester.widget(
              find.byType(ColorFiltered),
            );
            expect(filtered.colorFilter, treatment.filter.toColorFilter());
            expect(
              find.descendant(
                of: find.byType(ColorFiltered),
                matching: find.byType(DovahSigil),
              ),
              findsOneWidget,
            );
          }

          final Iterable<CustomPaint> glows = tester
              .widgetList<CustomPaint>(find.byType(CustomPaint))
              .where(
                (CustomPaint paint) =>
                    paint.painter is DovahSigilPainter &&
                    (paint.painter! as DovahSigilPainter).glowColor != null,
              );
          if (treatment.glowColor == null) {
            expect(glows, isEmpty);
          } else {
            final DovahSigilPainter painter =
                glows.single.painter! as DovahSigilPainter;
            expect(painter.glowColor, treatment.glowColor);
            expect(painter.glowBlurRadius, treatment.glowBlurRadius);
            expect(
              find.descendant(
                of: find.byType(ColorFiltered),
                matching: find.byWidgetPredicate(
                  (Widget widget) =>
                      widget is CustomPaint &&
                      widget.painter is DovahSigilPainter &&
                      (widget.painter! as DovahSigilPainter).glowColor != null,
                ),
              ),
              findsNothing,
            );
          }
        },
      );
    }

    testWidgets(
      'DovahBrandMark puts a translucent disc behind the Hearth sigil',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const Center(child: DovahBrandMark(size: markSize)),
          preset: DovahThemePreset.hearth,
          size: dovahTestSizes.first,
        );
        final DecoratedBox disc = tester.widget(
          find.descendant(
            of: find.byType(ColorFiltered),
            matching: find.byType(DecoratedBox),
          ),
        );
        final BoxDecoration decoration = disc.decoration as BoxDecoration;

        expect(decoration.shape, BoxShape.circle);
        expect(decoration.color, const Color.fromRGBO(255, 248, 230, 0.3));
      },
    );

    for (final DovahThemePreset preset in [
      DovahThemePreset.frostbound,
      DovahThemePreset.dovah,
    ]) {
      testWidgets('DovahBrandMark draws no disc under $preset', (
        WidgetTester tester,
      ) async {
        await pumpDovahThemedWidget(
          tester,
          const Center(child: DovahBrandMark(size: markSize)),
          preset: preset,
          size: dovahTestSizes.first,
        );

        expect(find.byType(DecoratedBox), findsNothing);
      });
    }
  });

  group('DovahBrandMark paints the treatment', () {
    for (final DovahThemePreset preset in [
      DovahThemePreset.frostbound,
      DovahThemePreset.dovah,
    ]) {
      testWidgets('DovahBrandMark glows beyond its bounds under $preset', (
        WidgetTester tester,
      ) async {
        final Color Function(int, int) at = await renderMark(tester, preset);

        expect(at(-3, 22).a, greaterThan(0));
        expect(at(-30, 22).a, 0);
      });
    }

    testWidgets('DovahBrandMark does not glow under Hearth', (
      WidgetTester tester,
    ) async {
      final Color Function(int, int) at = await renderMark(
        tester,
        DovahThemePreset.hearth,
      );

      expect(at(-3, 22).a, 0);
    });

    testWidgets('DovahBrandMark paints the Hearth disc between the arrows', (
      WidgetTester tester,
    ) async {
      final Color Function(int, int) at = await renderMark(
        tester,
        DovahThemePreset.hearth,
      );

      expect((at(22, 22).a * 255).round(), inInclusiveRange(70, 82));
      expect(at(1, 1).a, 0);
    });

    testWidgets('DovahBrandMark mutes Frostbound colors toward gray', (
      WidgetTester tester,
    ) async {
      final Color Function(int, int) at = await renderMark(
        tester,
        DovahThemePreset.frostbound,
      );
      final Color arrow = at(8, 22);

      // The upper arrow's own blue is far more saturated than the treated one.
      const Color raw = DovahSigilPainter.upperColor;
      expect((arrow.b - arrow.r).abs(), lessThan((raw.b - raw.r).abs()));
    });

    testWidgets('DovahBrandMark leaves Dovah colors untouched', (
      WidgetTester tester,
    ) async {
      final Color Function(int, int) at = await renderMark(
        tester,
        DovahThemePreset.dovah,
      );

      expect(at(8, 22), DovahSigilPainter.upperColor);
    });
  });
}
