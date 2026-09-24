import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_code_boxes.widget.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import '../../../../shared/theme/widgets/dovah_widget_test_helpers.dart';

/// Reads a digit box's container decoration.
BoxDecoration boxDecoration(WidgetTester tester, int index) =>
    tester
            .widget<Container>(find.byKey(Key('pairing-code-box-$index')))
            .decoration!
        as BoxDecoration;

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
            boxDecoration(tester, index).boxShadow,
            index == 2 ? isNotNull : isNull,
          );
        }
      },
    );

    testWidgets('PairingCodeBoxes accents the border of only the focused box', (
      WidgetTester tester,
    ) async {
      await pumpBoxes(tester, code: '12', isFocused: true);

      final Border active = boxDecoration(tester, 2).border! as Border;
      final Border inactive = boxDecoration(tester, 3).border! as Border;
      expect(active.top.color, isNot(inactive.top.color));
      expect(boxDecoration(tester, 0).border, inactive);
    });

    testWidgets(
      'PairingCodeBoxes halos the last box once the code is complete',
      (WidgetTester tester) async {
        await pumpBoxes(tester, code: '123456', isFocused: true);

        expect(
          boxDecoration(tester, pairingCodeLength - 1).boxShadow,
          isNotNull,
        );
        expect(boxDecoration(tester, 0).boxShadow, isNull);
      },
    );

    testWidgets('PairingCodeBoxes shows no halo when not focused', (
      WidgetTester tester,
    ) async {
      await pumpBoxes(tester, code: '12', isFocused: false);

      for (int index = 0; index < pairingCodeLength; index++) {
        expect(boxDecoration(tester, index).boxShadow, isNull);
      }
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
