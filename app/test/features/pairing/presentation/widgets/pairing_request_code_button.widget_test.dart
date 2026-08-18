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

    testWidgets(
      'labels the button and meets its minimum interactive size',
      (WidgetTester tester) async {
        // DovahLink is a Windows desktop app, not a touch device, so this
        // checks labeledTapTargetGuideline (semantic labeling, platform-
        // neutral) and Material's own kMinInteractiveDimension directly,
        // rather than the mobile-specific androidTapTargetGuideline/
        // iOSTapTargetGuideline -- the app theme's default
        // VisualDensity.adaptivePlatformDensity shrinks targets below the
        // 48dp mobile touch guideline on desktop by design, which is not an
        // accessibility defect here.
        final SemanticsHandle handle = tester.ensureSemantics();
        try {
          await tester.pumpWidget(
            MaterialApp(home: PairingRequestCodeButton(onRequestCode: () {})),
          );

          await expectLater(
            tester,
            meetsGuideline(labeledTapTargetGuideline),
          );
          final Size buttonSize = tester.getSize(
            find.byKey(const Key('pairing-request-code-button')),
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
