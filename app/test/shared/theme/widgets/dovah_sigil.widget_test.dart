import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_sigil.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_sigil_painter.dart';

import 'dovah_widget_test_helpers.dart';

/// Exercises [DovahSigil] across every DovahLink theme and test size.
void main() {
  group('DovahSigil renders correctly', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in dovahTestSizes) {
        testWidgets(
          'DovahSigil renders under $preset at $size at its requested size',
          (WidgetTester tester) async {
            await pumpDovahThemedWidget(
              tester,
              const Center(child: DovahSigil(size: 44)),
              preset: preset,
              size: size,
            );

            expect(tester.takeException(), isNull);
            expect(tester.getSize(find.byType(DovahSigil)), const Size(44, 44));
          },
        );
      }
    }

    testWidgets('DovahSigil contains the sigil painter', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const Center(child: DovahSigil(size: 30)),
        preset: DovahThemePreset.dovah,
        size: dovahTestSizes.first,
      );

      final CustomPaint paint = tester.widget(
        find.descendant(
          of: find.byType(DovahSigil),
          matching: find.byType(CustomPaint),
        ),
      );

      expect(paint.painter, isA<DovahSigilPainter>());
    });
  });

  group('DovahSigil exposes no semantics', () {
    testWidgets('DovahSigil is excluded from the semantics tree', (
      WidgetTester tester,
    ) async {
      final SemanticsHandle semantics = tester.ensureSemantics();
      try {
        await pumpDovahThemedWidget(
          tester,
          const Center(child: DovahSigil(size: 44)),
          preset: DovahThemePreset.dovah,
          size: dovahTestSizes.first,
        );

        expect(
          find.descendant(
            of: find.byType(DovahSigil),
            matching: find.byType(ExcludeSemantics),
          ),
          findsOneWidget,
        );
      } finally {
        semantics.dispose();
      }
    });
  });
}
