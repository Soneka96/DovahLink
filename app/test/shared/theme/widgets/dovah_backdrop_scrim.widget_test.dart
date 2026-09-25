import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/theme/materials/dovah_backdrop.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_materials.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_backdrop_painter.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_backdrop_scrim.widget.dart';

/// Exercises [DovahBackdropScrim]'s blur, treatment, tint, and pointer behavior.
void main() {
  const DovahBackdrop backdrop = DovahBackdrop(
    tint: Color(0x00000000),
    blurSigma: 7,
    saturation: 0.72,
  );
  const Key captureKey = Key('scrim-capture');

  Widget buildScrim({
    required DovahBackdrop backdrop,
    Widget child = const Text('Dialog'),
  }) => MaterialApp(
    home: Theme(
      data: ThemeData(
        extensions: [dovahMaterials.copyWith(backdrop: backdrop)],
      ),
      child: RepaintBoundary(
        key: captureKey,
        child: Stack(
          fit: StackFit.expand,
          children: [
            const ColoredBox(color: Color(0xFFFF0000)),
            DovahBackdropScrim(child: child),
          ],
        ),
      ),
    ),
  );

  group('DovahBackdropScrim renders correctly', () {
    testWidgets('DovahBackdropScrim displays its child above the scrim', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildScrim(backdrop: backdrop));

      expect(find.text('Dialog'), findsOneWidget);
      expect(tester.takeException(), isNull);
    });

    testWidgets('DovahBackdropScrim blurs the page with the backdrop sigma', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(buildScrim(backdrop: backdrop));
      final BackdropFilter filter = tester.widget(find.byType(BackdropFilter));

      expect(filter.filter, ui.ImageFilter.blur(sigmaX: 7, sigmaY: 7));
    });

    testWidgets(
      'DovahBackdropScrim paints the backdrop treatment behind its child',
      (WidgetTester tester) async {
        await tester.pumpWidget(buildScrim(backdrop: backdrop));
        final CustomPaint paint = tester.widget(
          find.descendant(
            of: find.byType(DovahBackdropScrim),
            matching: find.byType(CustomPaint),
          ),
        );

        expect(paint.painter, isA<DovahBackdropPainter>());
        expect((paint.painter! as DovahBackdropPainter).backdrop, backdrop);
        expect(paint.foregroundPainter, isNull);
      },
    );

    testWidgets('DovahBackdropScrim composites the treatment over the page', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(
        buildScrim(
          backdrop: const DovahBackdrop(
            tint: Color(0x00000000),
            blurSigma: 0,
            saturation: 0.72,
          ),
          child: const SizedBox.expand(),
        ),
      );
      final Color pixel = (await tester.runAsync(() async {
        final RenderRepaintBoundary boundary =
            tester.renderObject(find.byKey(captureKey))
                as RenderRepaintBoundary;
        final ui.Image image = await boundary.toImage();
        final ByteData data = (await image.toByteData(
          format: ui.ImageByteFormat.rawStraightRgba,
        ))!;
        image.dispose();

        return Color.fromARGB(
          data.getUint8(3),
          data.getUint8(0),
          data.getUint8(1),
          data.getUint8(2),
        );
      }))!;

      expect((pixel.r * 255).round(), lessThan(240));
      expect((pixel.g * 255).round(), greaterThan(8));
    });
  });

  group('DovahBackdropScrim passes pointer events through', () {
    testWidgets(
      'DovahBackdropScrim lets a tap on empty scrim reach the widget beneath',
      (WidgetTester tester) async {
        int tapCount = 0;
        await tester.pumpWidget(
          MaterialApp(
            home: Stack(
              fit: StackFit.expand,
              children: [
                GestureDetector(
                  behavior: HitTestBehavior.opaque,
                  onTap: () => tapCount++,
                ),
                Theme(
                  data: ThemeData(
                    extensions: [dovahMaterials.copyWith(backdrop: backdrop)],
                  ),
                  child: const DovahBackdropScrim(child: SizedBox.expand()),
                ),
              ],
            ),
          ),
        );

        await tester.tapAt(const Offset(100, 100));

        expect(tapCount, 1);
      },
    );

    testWidgets('DovahBackdropScrim still delivers taps to its own child', (
      WidgetTester tester,
    ) async {
      int tapCount = 0;
      await tester.pumpWidget(
        MaterialApp(
          home: Theme(
            data: ThemeData(
              extensions: [dovahMaterials.copyWith(backdrop: backdrop)],
            ),
            child: DovahBackdropScrim(
              child: Center(
                child: TextButton(
                  onPressed: () => tapCount++,
                  child: const Text('Inside'),
                ),
              ),
            ),
          ),
        ),
      );

      await tester.tap(find.text('Inside'));

      expect(tapCount, 1);
    });
  });
}
