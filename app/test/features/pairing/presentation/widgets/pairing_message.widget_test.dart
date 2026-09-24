import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_message.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import '../../../../shared/theme/widgets/dovah_widget_test_helpers.dart';

/// Pumps a [PairingMessage] showing [message] under the Dovah preset.
Future<void> pumpMessage(WidgetTester tester, {String? message}) =>
    pumpDovahThemedWidget(
      tester,
      Center(child: PairingMessage(message: message)),
      preset: DovahThemePreset.dovah,
      size: const Size(900, 560),
    );

/// Exercises [PairingMessage] display, reserved height, and announcement.
void main() {
  group('PairingMessage displays', () {
    testWidgets('PairingMessage displays its message', (
      WidgetTester tester,
    ) async {
      await pumpMessage(tester, message: 'That code is not correct.');

      expect(find.text('That code is not correct.'), findsOneWidget);
    });

    testWidgets('PairingMessage displays nothing visible without a message', (
      WidgetTester tester,
    ) async {
      await pumpMessage(tester);

      expect(tester.widget<Text>(find.byType(Text)).data, '');
    });

    testWidgets(
      'PairingMessage reserves its height whether or not it has a message',
      (WidgetTester tester) async {
        await pumpMessage(tester);
        final double empty = tester.getSize(find.byType(PairingMessage)).height;

        await pumpMessage(tester, message: 'Wrong code.');
        final double filled = tester
            .getSize(find.byType(PairingMessage))
            .height;

        expect(
          empty,
          greaterThanOrEqualTo(DovahThemeTokens.formErrorMinHeight),
        );
        expect(filled, empty);
      },
    );
  });

  group('PairingMessage meets accessibility recommended guidelines', () {
    testWidgets('PairingMessage announces its message as a live region', (
      WidgetTester tester,
    ) async {
      final SemanticsHandle handle = tester.ensureSemantics();
      try {
        await pumpMessage(tester, message: 'Wrong code.');

        expect(
          tester.getSemantics(find.text('Wrong code.')),
          isSemantics(label: 'Wrong code.', isLiveRegion: true),
        );
      } finally {
        handle.dispose();
      }
    });
  });
}
