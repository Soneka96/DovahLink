import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_request_code_button.widget.dart';

/// Exercises PairingRequestCodeButton rendering and interaction.
void main() {
  group('PairingRequestCodeButton', () {
    testWidgets(
      'renders the request-code label keyed pairing-request-code-button',
      (WidgetTester tester) async {
        await tester.pumpWidget(
          MaterialApp(home: PairingRequestCodeButton(onRequestCode: () {})),
        );

        expect(find.text('Request Pairing Code'), findsOneWidget);
        expect(
          find.byKey(const Key('pairing-request-code-button')),
          findsOneWidget,
        );
      },
    );

    testWidgets('calls onRequestCode when tapped', (
      WidgetTester tester,
    ) async {
      int callCount = 0;
      await tester.pumpWidget(
        MaterialApp(
          home: PairingRequestCodeButton(onRequestCode: () => callCount++),
        ),
      );

      await tester.tap(find.byKey(const Key('pairing-request-code-button')));
      await tester.pump();

      expect(callCount, 1);
    });
  });
}
