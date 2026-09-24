import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_mark.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import '../../../../shared/theme/widgets/dovah_widget_test_helpers.dart';

/// Exercises [PairingMark] rendering and accessibility.
void main() {
  group('PairingMark contains widgets', () {
    testWidgets(
      'PairingMark contains the given icon at the prototype tile size',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          const Center(child: PairingMark(icon: Icons.refresh)),
          preset: DovahThemePreset.dovah,
          size: const Size(900, 560),
        );

        expect(find.byIcon(Icons.refresh), findsOneWidget);
        expect(
          tester.getSize(find.byType(PairingMark)),
          const Size.square(DovahThemeTokens.pairingMarkSize),
        );
      },
    );
  });

  group('PairingMark meets accessibility recommended guidelines', () {
    testWidgets('PairingMark hides its decorative icon from semantics', (
      WidgetTester tester,
    ) async {
      final SemanticsHandle handle = tester.ensureSemantics();
      try {
        await pumpDovahThemedWidget(
          tester,
          const Center(child: PairingMark(icon: Icons.refresh)),
          preset: DovahThemePreset.dovah,
          size: const Size(900, 560),
        );

        expect(find.byType(ExcludeSemantics), findsWidgets);
        expect(tester.getSemantics(find.byType(PairingMark)).label, isEmpty);
      } finally {
        handle.dispose();
      }
    });
  });

  group('PairingMark lays out at supported sizes', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in const [Size(720, 480), ...dovahTestSizes]) {
        testWidgets(
          'PairingMark renders under $preset at $size without overflow',
          (WidgetTester tester) async {
            await pumpDovahThemedWidget(
              tester,
              const Center(child: PairingMark(icon: Icons.refresh)),
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
