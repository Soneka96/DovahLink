import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_blocked.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import '../../../../shared/theme/widgets/dovah_widget_test_helpers.dart';

/// Pumps a themed blocked state and records Close actions in [calls].
Future<void> pumpBlocked(
  WidgetTester tester, {
  List<String>? calls,
  DovahThemePreset preset = DovahThemePreset.dovah,
  Size size = const Size(900, 560),
}) => pumpDovahThemedWidget(
  tester,
  PairingBlocked(onClose: () => calls?.add('close')),
  preset: preset,
  size: size,
);

/// Exercises the non-repairable blocked pairing presentation.
void main() {
  group('PairingBlocked displays', () {
    testWidgets('PairingBlocked displays blocked copy and only Close', (
      WidgetTester tester,
    ) async {
      await pumpBlocked(tester);

      expect(find.text('Pairing unavailable'), findsOneWidget);
      expect(
        find.text(
          'This device is blocked by the Host and cannot pair again until an administrator '
          'unblocks it.',
          findRichText: true,
        ),
        findsOneWidget,
      );
      expect(find.byKey(const Key('pairing-close-button')), findsOneWidget);
      expect(find.text('Close'), findsOneWidget);
      expect(find.text('Pair again'), findsNothing);
      expect(find.byKey(const Key('pairing-retry-button')), findsNothing);
    });
  });

  group('PairingBlocked calls callbacks', () {
    testWidgets('PairingBlocked calls onClose when Close is tapped', (
      WidgetTester tester,
    ) async {
      final List<String> calls = [];
      await pumpBlocked(tester, calls: calls);

      await tester.tap(find.byKey(const Key('pairing-close-button')));
      await tester.pump();

      expect(calls, ['close']);
    });
  });

  group('PairingBlocked meets accessibility recommendations', () {
    testWidgets(
      'PairingBlocked exposes its heading and Close action semantically',
      (WidgetTester tester) async {
        final SemanticsHandle handle = tester.ensureSemantics();
        try {
          await pumpBlocked(tester);

          expect(
            tester.getSemantics(find.byKey(const Key('pairing-heading'))),
            isSemantics(
              label: 'Pairing unavailable',
              isHeader: true,
              isLiveRegion: true,
            ),
          );
          expect(
            tester.getSemantics(
              find.byKey(const Key('dovah-button-semantics')),
            ),
            isSemantics(label: 'Close', isButton: true, hasTapAction: true),
          );
        } finally {
          handle.dispose();
        }
      },
    );
  });

  group('PairingBlocked lays out at supported sizes', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in const [Size(720, 480), ...dovahTestSizes]) {
        testWidgets(
          'PairingBlocked renders under $preset at $size without overflow',
          (WidgetTester tester) async {
            await pumpBlocked(tester, preset: preset, size: size);

            expect(tester.takeException(), isNull);
          },
        );
      }
    }
  });
}
