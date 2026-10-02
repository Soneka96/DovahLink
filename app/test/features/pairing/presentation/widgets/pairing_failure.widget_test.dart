import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_failure.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import '../../../../shared/theme/widgets/dovah_widget_test_helpers.dart';

/// Pumps a [PairingFailure], recording actions into [log].
Future<void> pumpFailure(
  WidgetTester tester, {
  String message = 'That code has expired.',
  PairingFailureOutcome? outcome,
  int? attemptsRemaining,
  PairingRenotifyOutcome? renotifyOutcome,
  List<String>? log,
  DovahThemePreset preset = DovahThemePreset.dovah,
  Size size = const Size(900, 560),
}) => pumpDovahThemedWidget(
  tester,
  PairingFailure(
    message: message,
    outcome: outcome,
    attemptsRemaining: attemptsRemaining,
    renotifyOutcome: renotifyOutcome,
    onClose: () => log?.add('close'),
    onRetry: () => log?.add('retry'),
  ),
  preset: preset,
  size: size,
);

/// Exercises [PairingFailure] copy, actions, and layout.
void main() {
  group('PairingFailure displays', () {
    testWidgets(
      'PairingFailure displays the heading and the real failure reason',
      (WidgetTester tester) async {
        await pumpFailure(tester, message: 'Pairing cancelled.');

        expect(find.text('Pairing didn’t finish'), findsOneWidget);
        expect(
          (tester.widget<Text>(find.byKey(const Key('pairing-body'))).textSpan!
                  as TextSpan)
              .toPlainText(),
          'Pairing cancelled.',
        );
        expect(find.text('Close'), findsOneWidget);
        expect(find.text('Try Again'), findsOneWidget);
        expect(find.byIcon(Icons.refresh), findsOneWidget);
      },
    );

    testWidgets('PairingFailure contains Close and Try Again actions', (
      WidgetTester tester,
    ) async {
      await pumpFailure(tester);

      expect(find.byKey(const Key('pairing-close-button')), findsOneWidget);
      expect(find.byKey(const Key('pairing-retry-button')), findsOneWidget);
    });

    testWidgets('PairingFailure renders copy from the typed terminal outcome', (
      WidgetTester tester,
    ) async {
      await pumpFailure(
        tester,
        message: 'unused generic message',
        outcome: PairingFailureOutcome.hardLimitReached,
      );

      expect(
        (tester.widget<Text>(find.byKey(const Key('pairing-body'))).textSpan!
                as TextSpan)
            .toPlainText(),
        'This pairing code can no longer be used. Request a new code from Skyrim to try again.',
      );
      expect(find.text('Too many incorrect attempts'), findsOneWidget);
      expect(find.text('Cancel'), findsOneWidget);
      expect(find.text('Get a new code'), findsOneWidget);
      expect(find.byIcon(Icons.desktop_windows_outlined), findsOneWidget);
    });

    testWidgets('PairingFailure displays the expired-code prototype state', (
      WidgetTester tester,
    ) async {
      await pumpFailure(tester, outcome: PairingFailureOutcome.expired);

      expect(find.text('Code expired'), findsOneWidget);
      expect(
        find.text(
          'The code is no longer valid. Ask Skyrim for a new one to continue.',
        ),
        findsOneWidget,
      );
      expect(find.text('Cancel'), findsOneWidget);
      expect(find.text('Get a new code'), findsOneWidget);
      expect(find.byIcon(Icons.desktop_windows_outlined), findsOneWidget);
    });

    testWidgets(
      'PairingFailure renders copy from the typed idle redisplay result',
      (WidgetTester tester) async {
        await pumpFailure(
          tester,
          message: 'unused generic message',
          renotifyOutcome: PairingRenotifyOutcome.alreadyIdle,
        );

        expect(
          (tester.widget<Text>(find.byKey(const Key('pairing-body'))).textSpan!
                  as TextSpan)
              .toPlainText(),
          'No pairing is currently active.',
        );
      },
    );
  });

  group('PairingFailure calls callbacks', () {
    testWidgets('PairingFailure calls only onClose when Close is tapped', (
      WidgetTester tester,
    ) async {
      final List<String> log = [];
      await pumpFailure(tester, log: log);

      await tester.tap(find.byKey(const Key('pairing-close-button')));
      await tester.pump();

      expect(log, ['close']);
    });

    testWidgets('PairingFailure calls only onRetry when Try Again is tapped', (
      WidgetTester tester,
    ) async {
      final List<String> log = [];
      await pumpFailure(tester, log: log);

      await tester.tap(find.byKey(const Key('pairing-retry-button')));
      await tester.pump();

      expect(log, ['retry']);
    });
  });

  group('PairingFailure lays out at supported sizes', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in const [Size(720, 480), ...dovahTestSizes]) {
        testWidgets(
          'PairingFailure renders under $preset at $size without overflow',
          (WidgetTester tester) async {
            await pumpFailure(
              tester,
              message: 'A long failure reason. ' * 8,
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
