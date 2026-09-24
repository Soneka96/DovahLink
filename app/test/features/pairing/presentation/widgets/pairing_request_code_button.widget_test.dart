import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_request_code_button.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_button.widget.dart';

/// Builds [PairingRequestCodeButton] inside a themed app.
Widget buildWidget({VoidCallback? onRequestCode}) => MaterialApp(
  theme: dovahThemeDataFor(DovahThemePreset.dovah),
  home: Scaffold(
    body: Center(
      child: PairingRequestCodeButton(onRequestCode: onRequestCode ?? () {}),
    ),
  ),
);

/// Exercises PairingRequestCodeButton rendering and interaction.
void main() {
  group('PairingRequestCodeButton displays', () {
    testWidgets(
      'PairingRequestCodeButton displays the Request Pairing Code label keyed pairing-request-code-button',
      (WidgetTester tester) async {
        await tester.pumpWidget(buildWidget());

        expect(find.text('Request Pairing Code'), findsOneWidget);
        expect(
          find.byKey(const Key('pairing-request-code-button')),
          findsOneWidget,
        );
        expect(find.byType(DovahButton), findsOneWidget);
      },
    );
  });

  group('PairingRequestCodeButton calls callbacks', () {
    testWidgets('PairingRequestCodeButton calls onRequestCode when tapped', (
      WidgetTester tester,
    ) async {
      int callCount = 0;
      await tester.pumpWidget(buildWidget(onRequestCode: () => callCount++));

      await tester.tap(find.byKey(const Key('pairing-request-code-button')));
      await tester.pump();

      expect(callCount, 1);
    });
  });

  group('PairingRequestCodeButton meets accessibility recommended guidelines', () {
    testWidgets(
      'PairingRequestCodeButton labels the button and meets its minimum interactive size',
      (WidgetTester tester) async {
        // DovahLink is a Windows desktop app, so this checks the platform-neutral
        // labeledTapTargetGuideline and Material's kMinInteractiveDimension directly rather than
        // the mobile-specific tap-target guidelines.
        final SemanticsHandle handle = tester.ensureSemantics();
        try {
          await tester.pumpWidget(buildWidget());

          await expectLater(tester, meetsGuideline(labeledTapTargetGuideline));
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
