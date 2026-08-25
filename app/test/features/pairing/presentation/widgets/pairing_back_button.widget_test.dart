import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_back_button.widget.dart';

/// Exercises PairingBackButton rendering and interaction.
void main() {
  group('PairingBackButton', () {
    testWidgets('PairingBackButton renders keyed pairing-back-button', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(
        MaterialApp(home: PairingBackButton(onBack: () {})),
      );

      expect(find.byKey(const Key('pairing-back-button')), findsOneWidget);
    });

    testWidgets('PairingBackButton calls onBack when tapped', (
      WidgetTester tester,
    ) async {
      int callCount = 0;
      await tester.pumpWidget(
        MaterialApp(home: PairingBackButton(onBack: () => callCount++)),
      );

      await tester.tap(find.byKey(const Key('pairing-back-button')));
      await tester.pump();

      expect(callCount, 1);
    });

    testWidgets(
      'PairingBackButton labels the button and meets its minimum interactive size',
      (WidgetTester tester) async {
        // See the equivalent check on PairingRequestCodeButton for why this
        // checks labeledTapTargetGuideline + kMinInteractiveDimension
        // directly rather than the mobile-specific tap-target guidelines.
        final SemanticsHandle handle = tester.ensureSemantics();
        try {
          await tester.pumpWidget(
            MaterialApp(home: PairingBackButton(onBack: () {})),
          );

          await expectLater(tester, meetsGuideline(labeledTapTargetGuideline));
          final Size buttonSize = tester.getSize(
            find.byKey(const Key('pairing-back-button')),
          );
          expect(
            buttonSize.height,
            greaterThanOrEqualTo(kMinInteractiveDimension),
          );
        } finally {
          handle.dispose();
        }
      },
    );
  });
}
