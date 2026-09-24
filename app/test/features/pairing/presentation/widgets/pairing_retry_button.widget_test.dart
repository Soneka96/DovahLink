import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_retry_button.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_button.widget.dart';

/// Builds [PairingRetryButton] inside a themed app.
Widget buildWidget({VoidCallback? onRetry}) => MaterialApp(
  theme: dovahThemeDataFor(DovahThemePreset.dovah),
  home: Scaffold(
    body: Center(child: PairingRetryButton(onRetry: onRetry ?? () {})),
  ),
);

/// Exercises PairingRetryButton rendering and interaction.
void main() {
  group('PairingRetryButton displays', () {
    testWidgets(
      'PairingRetryButton displays the Try Again label keyed pairing-retry-button',
      (WidgetTester tester) async {
        await tester.pumpWidget(buildWidget());

        expect(find.text('Try Again'), findsOneWidget);
        expect(find.byKey(const Key('pairing-retry-button')), findsOneWidget);
        expect(find.byType(DovahButton), findsOneWidget);
      },
    );
  });

  group('PairingRetryButton calls callbacks', () {
    testWidgets('PairingRetryButton calls onRetry when tapped', (
      WidgetTester tester,
    ) async {
      int callCount = 0;
      await tester.pumpWidget(buildWidget(onRetry: () => callCount++));

      await tester.tap(find.byKey(const Key('pairing-retry-button')));
      await tester.pump();

      expect(callCount, 1);
    });
  });

  group('PairingRetryButton meets accessibility recommended guidelines', () {
    testWidgets(
      'PairingRetryButton labels the button and meets its minimum interactive size',
      (WidgetTester tester) async {
        // DovahLink is a Windows desktop app, so this checks the platform-neutral
        // labeledTapTargetGuideline and Material's kMinInteractiveDimension directly rather than
        // the mobile-specific tap-target guidelines.
        final SemanticsHandle handle = tester.ensureSemantics();
        try {
          await tester.pumpWidget(buildWidget());

          await expectLater(tester, meetsGuideline(labeledTapTargetGuideline));
          final Size buttonSize = tester.getSize(
            find.byKey(const Key('pairing-retry-button')),
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
