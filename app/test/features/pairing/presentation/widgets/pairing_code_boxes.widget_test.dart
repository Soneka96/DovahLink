import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_code_boxes.widget.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_materials.dart';
import 'package:dovahlink_client/shared/theme/materials/dovah_theme_materials.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_focus_ring.widget.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_focus_ring_painter.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_material_painter.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_surface.widget.dart';
import '../../../../shared/theme/widgets/dovah_widget_test_helpers.dart';

/// Reads a digit box's halo decoration.
BoxDecoration haloDecoration(WidgetTester tester, int index) =>
    tester
            .widget<DecoratedBox>(
              find.descendant(
                of: find.byKey(Key('pairing-code-box-$index')),
                matching: find.byType(DecoratedBox),
              ),
            )
            .decoration
        as BoxDecoration;

/// Reads the painter of a digit box's material surface.
DovahMaterialPainter boxPainter(WidgetTester tester, int index) =>
    tester
            .widget<CustomPaint>(
              find
                  .descendant(
                    of: find.descendant(
                      of: find.byKey(Key('pairing-code-box-$index')),
                      matching: find.byType(DovahSurface),
                    ),
                    matching: find.byType(CustomPaint),
                  )
                  .first,
            )
            .painter!
        as DovahMaterialPainter;

/// Exercises [PairingCodeBoxes] rendering and focus treatment.
void main() {
  Future<void> pumpBoxes(
    WidgetTester tester, {
    required String code,
    required bool isFocused,
    DovahThemePreset preset = DovahThemePreset.dovah,
    Size size = const Size(900, 560),
  }) => pumpDovahThemedWidget(
    tester,
    Center(
      child: PairingCodeBoxes(code: code, isFocused: isFocused),
    ),
    preset: preset,
    size: size,
  );

  group('PairingCodeBoxes contains widgets', () {
    testWidgets(
      'PairingCodeBoxes contains one box per digit of pairingCodeLength',
      (WidgetTester tester) async {
        await pumpBoxes(tester, code: '', isFocused: false);

        for (int index = 0; index < pairingCodeLength; index++) {
          expect(find.byKey(Key('pairing-code-box-$index')), findsOneWidget);
        }
        expect(
          find.byKey(const Key('pairing-code-box-$pairingCodeLength')),
          findsNothing,
        );
      },
    );

    testWidgets(
      'PairingCodeBoxes displays each entered digit in its own box and leaves the rest empty',
      (WidgetTester tester) async {
        await pumpBoxes(tester, code: '123', isFocused: false);

        for (int index = 0; index < 3; index++) {
          expect(
            find.descendant(
              of: find.byKey(Key('pairing-code-box-$index')),
              matching: find.text('${index + 1}'),
            ),
            findsOneWidget,
          );
        }
        for (int index = 3; index < pairingCodeLength; index++) {
          expect(
            find.descendant(
              of: find.byKey(Key('pairing-code-box-$index')),
              matching: find.text(''),
            ),
            findsOneWidget,
          );
        }
      },
    );

    for (final Size size in dovahResponsiveTestSizes) {
      final bool isCompact = size.height <= 620;
      final double boxWidth = isCompact ? 45 : 49;
      final double boxHeight = isCompact ? 48 : 56;
      testWidgets(
        'PairingCodeBoxes draws ${boxWidth}x$boxHeight boxes 8 apart at $size',
        (WidgetTester tester) async {
          await pumpBoxes(tester, code: '', isFocused: false, size: size);

          expect(
            tester.getSize(find.byKey(const Key('pairing-code-box-0'))),
            Size(boxWidth, boxHeight),
          );
          expect(
            tester.getSize(find.byType(PairingCodeBoxes)).width,
            pairingCodeLength * boxWidth + (pairingCodeLength - 1) * 8,
          );
          expect(
            tester.getTopLeft(find.byKey(const Key('pairing-code-box-1'))).dx -
                tester
                    .getTopRight(find.byKey(const Key('pairing-code-box-0')))
                    .dx,
            8,
          );
        },
      );
    }
  });

  group('PairingCodeBoxes shows focus', () {
    testWidgets(
      'PairingCodeBoxes halos only the box the next digit lands in when focused',
      (WidgetTester tester) async {
        await pumpBoxes(tester, code: '12', isFocused: true);

        for (int index = 0; index < pairingCodeLength; index++) {
          expect(
            haloDecoration(tester, index).boxShadow,
            index == 2 ? isNotNull : isNull,
          );
        }
      },
    );

    testWidgets(
      'PairingCodeBoxes halos the focused box 3px wide in the soft tone',
      (WidgetTester tester) async {
        await pumpBoxes(tester, code: '12', isFocused: true);
        final DovahThemeTokens tokens = dovahThemeDataFor(
          DovahThemePreset.dovah,
        ).extension<DovahThemeTokens>()!;

        final BoxShadow halo = haloDecoration(tester, 2).boxShadow!.single;
        expect(halo.color, tokens.soft);
        expect(halo.spreadRadius, 3);
        expect(halo.blurRadius, 0);
      },
    );

    testWidgets(
      'PairingCodeBoxes outlines only the focused box with the focus ring',
      (WidgetTester tester) async {
        await pumpBoxes(tester, code: '12', isFocused: true);

        for (int index = 0; index < pairingCodeLength; index++) {
          expect(
            find.descendant(
              of: find.byKey(Key('pairing-code-box-$index')),
              matching: find.byKey(DovahFocusRing.ringKey),
            ),
            index == 2 ? findsOneWidget : findsNothing,
          );
        }
      },
    );

    testWidgets('PairingCodeBoxes keeps every box border when one is focused', (
      WidgetTester tester,
    ) async {
      await pumpBoxes(tester, code: '12', isFocused: true);

      expect(
        boxPainter(tester, 2).material.borderColor,
        boxPainter(tester, 3).material.borderColor,
      );
      expect(
        boxPainter(tester, 2).material.borderColor,
        dovahMaterials.control.borderColor,
      );
    });

    testWidgets(
      'PairingCodeBoxes halos the last box once the code is complete',
      (WidgetTester tester) async {
        await pumpBoxes(tester, code: '123456', isFocused: true);

        expect(
          haloDecoration(tester, pairingCodeLength - 1).boxShadow,
          isNotNull,
        );
        expect(haloDecoration(tester, 0).boxShadow, isNull);
      },
    );

    testWidgets(
      'PairingCodeBoxes halos and outlines the first box for an empty code',
      (WidgetTester tester) async {
        await pumpBoxes(tester, code: '', isFocused: true);

        expect(haloDecoration(tester, 0).boxShadow, isNotNull);
        expect(haloDecoration(tester, 1).boxShadow, isNull);
        expect(
          find.descendant(
            of: find.byKey(const Key('pairing-code-box-0')),
            matching: find.byKey(DovahFocusRing.ringKey),
          ),
          findsOneWidget,
        );
      },
    );

    testWidgets('PairingCodeBoxes shows no halo or ring when not focused', (
      WidgetTester tester,
    ) async {
      await pumpBoxes(tester, code: '12', isFocused: false);

      for (int index = 0; index < pairingCodeLength; index++) {
        expect(haloDecoration(tester, index).boxShadow, isNull);
      }
      expect(find.byKey(DovahFocusRing.ringKey), findsNothing);
    });
  });

  group('PairingCodeBoxes paints the control material', () {
    for (final (DovahThemePreset preset, double radius) in [
      (DovahThemePreset.frostbound, 0.0),
      (DovahThemePreset.dovah, 0.0),
      (DovahThemePreset.hearth, 9.0),
    ]) {
      for (final Size size in dovahResponsiveTestSizes) {
        testWidgets(
          'PairingCodeBoxes paints every box on the $preset control material with radius $radius at $size',
          (WidgetTester tester) async {
            await pumpBoxes(
              tester,
              code: '12',
              isFocused: true,
              preset: preset,
              size: size,
            );
            final DovahThemeMaterials materials = dovahThemeDataFor(
              preset,
            ).extension<DovahThemeMaterials>()!;

            for (int index = 0; index < pairingCodeLength; index++) {
              final DovahMaterialPainter painter = boxPainter(tester, index);
              expect(painter.material, materials.control);
              expect(painter.cornerStyle, DovahPanelCornerStyle.rounded);
              expect(painter.cornerRadius, radius);
            }
            expect(
              haloDecoration(tester, 2).borderRadius,
              BorderRadius.circular(radius),
            );
          },
        );
      }
    }

    testWidgets('PairingCodeBoxes rounds the focus ring of a Hearth box', (
      WidgetTester tester,
    ) async {
      await pumpBoxes(
        tester,
        code: '12',
        isFocused: true,
        preset: DovahThemePreset.hearth,
      );
      final DovahFocusRingPainter painter =
          tester
                  .widget<CustomPaint>(find.byKey(DovahFocusRing.ringKey))
                  .foregroundPainter!
              as DovahFocusRingPainter;

      expect(painter.cornerRadius, 9);
    });
  });

  group('PairingCodeBoxes meets accessibility recommended guidelines', () {
    testWidgets(
      'PairingCodeBoxes hides the digits from semantics because the text field is the control',
      (WidgetTester tester) async {
        final SemanticsHandle handle = tester.ensureSemantics();
        try {
          await pumpBoxes(tester, code: '123', isFocused: false);

          expect(find.bySemanticsLabel('1'), findsNothing);
          expect(find.bySemanticsLabel('2'), findsNothing);
        } finally {
          handle.dispose();
        }
      },
    );
  });

  group('PairingCodeBoxes lays out at supported sizes', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in dovahResponsiveTestSizes) {
        testWidgets(
          'PairingCodeBoxes renders under $preset at $size without overflow',
          (WidgetTester tester) async {
            await pumpBoxes(
              tester,
              code: '123456',
              isFocused: true,
              preset: preset,
              size: size,
            );

            expect(tester.takeException(), isNull);
          },
        );
      }
    }
  });
}
