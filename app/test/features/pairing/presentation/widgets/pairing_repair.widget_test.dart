import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_repair.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/widgets/dovah_button.widget.dart';
import '../../../../shared/theme/widgets/dovah_widget_test_helpers.dart';

/// Pumps a [PairingRepair], counting cancels into [cancels] and code requests into [requests].
Future<void> pumpRepair(
  WidgetTester tester, {
  String? message = 'This device’s trust was revoked.',
  List<int>? cancels,
  List<int>? requests,
  DovahThemePreset preset = DovahThemePreset.dovah,
  Size size = const Size(900, 560),
}) => pumpDovahThemedWidget(
  tester,
  PairingRepair(
    hostName: 'Laptop',
    message: message,
    onCancel: () => cancels?.add(1),
    onRequestCode: () => requests?.add(1),
  ),
  preset: preset,
  size: size,
);

/// Exercises [PairingRepair] copy, actions, and layout.
void main() {
  group('PairingRepair displays', () {
    testWidgets('PairingRepair displays the heading naming the Host', (
      WidgetTester tester,
    ) async {
      await pumpRepair(tester);

      expect(find.text('Pair Laptop again'), findsOneWidget);
    });

    testWidgets(
      'PairingRepair displays the prototype explanation that the connection changed',
      (WidgetTester tester) async {
        await pumpRepair(tester);

        expect(
          (tester.widget<Text>(find.byKey(const Key('pairing-body'))).textSpan!
                  as TextSpan)
              .toPlainText(),
          'Your connection changed in Skyrim. Pair again to restore automatic connections.',
        );
      },
    );

    testWidgets('PairingRepair displays the real reason from the Host', (
      WidgetTester tester,
    ) async {
      await pumpRepair(tester);

      expect(find.text('This device’s trust was revoked.'), findsOneWidget);
    });

    testWidgets(
      'PairingRepair displays an empty reason slot without a reason',
      (WidgetTester tester) async {
        await pumpRepair(tester, message: null);

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

    testWidgets('PairingRepair displays Cancel before Pair again', (
      WidgetTester tester,
    ) async {
      await pumpRepair(tester);

      expect(find.text('Cancel'), findsOneWidget);
      expect(find.text('Pair again'), findsOneWidget);
      expect(
        tester
            .getTopLeft(find.byKey(const Key('pairing-repair-cancel-button')))
            .dx,
        lessThan(
          tester
              .getTopLeft(find.byKey(const Key('pairing-request-code-button')))
              .dx,
        ),
      );
    });

    testWidgets(
      'PairingRepair styles Cancel as secondary and Pair again as primary',
      (WidgetTester tester) async {
        await pumpRepair(tester);

        expect(
          tester
              .widget<DovahButton>(
                find.byKey(const Key('pairing-repair-cancel-button')),
              )
              .variant,
          DovahButtonVariant.secondary,
        );
        expect(
          tester
              .widget<DovahButton>(
                find.byKey(const Key('pairing-request-code-button')),
              )
              .variant,
          DovahButtonVariant.primary,
        );
      },
    );
  });

  group('PairingRepair calls callbacks', () {
    testWidgets('PairingRepair calls onRequestCode when Pair again is tapped', (
      WidgetTester tester,
    ) async {
      final List<int> cancels = [];
      final List<int> requests = [];
      await pumpRepair(tester, cancels: cancels, requests: requests);

      await tester.tap(find.byKey(const Key('pairing-request-code-button')));
      await tester.pump();

      expect(requests, hasLength(1));
      expect(cancels, isEmpty);
    });

    testWidgets('PairingRepair calls onCancel when Cancel is tapped', (
      WidgetTester tester,
    ) async {
      final List<int> cancels = [];
      final List<int> requests = [];
      await pumpRepair(tester, cancels: cancels, requests: requests);

      await tester.tap(find.byKey(const Key('pairing-repair-cancel-button')));
      await tester.pump();

      expect(cancels, hasLength(1));
      expect(requests, isEmpty);
    });

    testWidgets('PairingRepair calls nothing until a button is tapped', (
      WidgetTester tester,
    ) async {
      final List<int> cancels = [];
      final List<int> requests = [];
      await pumpRepair(tester, cancels: cancels, requests: requests);

      expect(cancels, isEmpty);
      expect(requests, isEmpty);
    });
  });

  group('PairingRepair meets accessibility recommended guidelines', () {
    testWidgets('PairingRepair exposes both actions as labeled buttons', (
      WidgetTester tester,
    ) async {
      final SemanticsHandle handle = tester.ensureSemantics();
      try {
        await pumpRepair(tester);

        expect(find.bySemanticsLabel('Cancel'), findsOneWidget);
        expect(find.bySemanticsLabel('Pair again'), findsOneWidget);
        await expectLater(tester, meetsGuideline(labeledTapTargetGuideline));
      } finally {
        handle.dispose();
      }
    });

    testWidgets('PairingRepair announces its heading as a live-region header', (
      WidgetTester tester,
    ) async {
      final SemanticsHandle handle = tester.ensureSemantics();
      try {
        await pumpRepair(tester);

        expect(
          tester.getSemantics(find.byKey(const Key('pairing-heading'))),
          isSemantics(
            label: 'Pair Laptop again',
            isHeader: true,
            isLiveRegion: true,
          ),
        );
      } finally {
        handle.dispose();
      }
    });
  });

  group('PairingRepair lays out at supported sizes', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in dovahResponsiveTestSizes) {
        testWidgets(
          'PairingRepair renders under $preset at $size without overflow',
          (WidgetTester tester) async {
            await pumpRepair(tester, preset: preset, size: size);

            expect(tester.takeException(), isNull);
          },
        );
      }
    }
  });
}
