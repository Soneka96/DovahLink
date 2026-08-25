import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_loading.widget.dart';

/// Exercises PairingLoadingIndicator rendering.
void main() {
  group('PairingLoadingIndicator', () {
    testWidgets(
      'PairingLoadingIndicator renders a progress indicator keyed pairing-loading',
      (WidgetTester tester) async {
        await tester.pumpWidget(
          const MaterialApp(home: PairingLoadingIndicator()),
        );

        expect(find.byKey(const Key('pairing-loading')), findsOneWidget);
        expect(find.byType(CircularProgressIndicator), findsOneWidget);
      },
    );
  });
}
