import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_retry_button.widget.dart';

/// Exercises PairingRetryButton rendering and interaction.
void main() {
  group('PairingRetryButton', () {
    testWidgets('renders the retry label keyed pairing-retry-button', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(
        MaterialApp(home: PairingRetryButton(onRetry: () {})),
      );

      expect(find.text('Try Again'), findsOneWidget);
      expect(find.byKey(const Key('pairing-retry-button')), findsOneWidget);
    });

    testWidgets('calls onRetry when tapped', (WidgetTester tester) async {
      int callCount = 0;
      await tester.pumpWidget(
        MaterialApp(home: PairingRetryButton(onRetry: () => callCount++)),
      );

      await tester.tap(find.byKey(const Key('pairing-retry-button')));
      await tester.pump();

      expect(callCount, 1);
    });
  });
}
