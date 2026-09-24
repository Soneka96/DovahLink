import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_ready.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import '../../../../shared/theme/widgets/dovah_widget_test_helpers.dart';

/// Pumps a [PairingReady], counting code requests into [requests].
Future<void> pumpReady(
  WidgetTester tester, {
  String? message,
  List<int>? requests,
  DovahThemePreset preset = DovahThemePreset.dovah,
  Size size = const Size(900, 560),
}) => pumpDovahThemedWidget(
  tester,
  PairingReady(
    hostName: 'Bedroom PC',
    message: message,
    onRequestCode: () => requests?.add(1),
  ),
  preset: preset,
  size: size,
);

/// Exercises [PairingReady] copy, message, action, and layout.
void main() {
  group('PairingReady displays', () {
    testWidgets(
      'PairingReady displays the heading and names the Host in the body',
      (WidgetTester tester) async {
        await pumpReady(tester);

        expect(find.text('Pair this device'), findsOneWidget);
        expect(
          (tester.widget<Text>(find.byKey(const Key('pairing-body'))).textSpan!
                  as TextSpan)
              .toPlainText(),
          contains('Bedroom PC.'),
        );
      },
    );

    testWidgets(
      'PairingReady displays the message explaining why pairing is needed again',
      (WidgetTester tester) async {
        await pumpReady(tester, message: 'This device was revoked.');

        expect(find.text('This device was revoked.'), findsOneWidget);
      },
    );

    testWidgets(
      'PairingReady displays an empty message slot without a message',
      (WidgetTester tester) async {
        await pumpReady(tester);

        expect(
          tester
              .widget<Text>(
                find.descendant(
                  of: find.byKey(const Key('pairing-message')),
                  matching: find.byType(Text),
                ),
              )
              .data,
          '',
        );
      },
    );
  });

  group('PairingReady calls callbacks', () {
    testWidgets(
      'PairingReady calls onRequestCode when the request button is tapped',
      (WidgetTester tester) async {
        final List<int> requests = [];
        await pumpReady(tester, requests: requests);

        await tester.tap(find.byKey(const Key('pairing-request-code-button')));
        await tester.pump();

        expect(requests, hasLength(1));
      },
    );

    testWidgets(
      'PairingReady does not call onRequestCode until the button is tapped',
      (WidgetTester tester) async {
        final List<int> requests = [];
        await pumpReady(tester, requests: requests);

        expect(requests, isEmpty);
      },
    );
  });

  group('PairingReady lays out at supported sizes', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in const [Size(720, 480), ...dovahTestSizes]) {
        testWidgets(
          'PairingReady renders under $preset at $size without overflow',
          (WidgetTester tester) async {
            await pumpReady(
              tester,
              message: 'This device was revoked.',
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
