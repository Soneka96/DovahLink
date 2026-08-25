import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_trusted.widget.dart';

/// Exercises PairingTrustedIndicator rendering.
void main() {
  group('PairingTrustedIndicator', () {
    testWidgets('PairingTrustedIndicator displays Paired keyed pairing-trusted', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(
        const MaterialApp(home: PairingTrustedIndicator()),
      );

      expect(find.text('Paired'), findsOneWidget);
      expect(find.byKey(const Key('pairing-trusted')), findsOneWidget);
    });
  });
}
