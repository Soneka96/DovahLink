import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/pairing/presentation/widgets/pairing_mark.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import '../../../../shared/theme/widgets/dovah_widget_test_helpers.dart';

/// Exercises [PairingMark] rendering and accessibility.
void main() {
  group('PairingMark contains widgets', () {
    testWidgets('PairingMark contains the given icon', (
      WidgetTester tester,
    ) async {
      await pumpDovahThemedWidget(
        tester,
        const Center(child: PairingMark(icon: Icons.refresh)),
        preset: DovahThemePreset.dovah,
        size: const Size(900, 560),
      );

      expect(find.byIcon(Icons.refresh), findsOneWidget);
    });

    for (final Size size in dovahResponsiveTestSizes) {
      final bool isCompact = size.height <= 620;
      testWidgets(
        'PairingMark draws a ${isCompact ? 42 : 54} tile with a ${isCompact ? 20 : 24} icon at $size',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            const Center(child: PairingMark(icon: Icons.refresh)),
            preset: DovahThemePreset.dovah,
            size: size,
          );

          expect(
            tester.getSize(find.byType(PairingMark)),
            Size.square(isCompact ? 42 : 54),
          );
          expect(
            tester.widget<Icon>(find.byIcon(Icons.refresh)).size,
            isA<double>(),
          );
          expect(
            tester.widget<Icon>(find.byIcon(Icons.refresh)).size,
            isCompact ? 20 : 24,
          );
        },
      );
    }
  });

  group('PairingMark colors its icon with the theme mark tone', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      testWidgets(
        'PairingMark colors its icon with the ${preset.name} mark tone',
        (WidgetTester tester) async {
          await pumpDovahThemedWidget(
            tester,
            const Center(child: PairingMark(icon: Icons.refresh)),
            preset: preset,
            size: const Size(900, 560),
          );
          final DovahThemeTokens tokens = dovahThemeDataFor(
            preset,
          ).extension<DovahThemeTokens>()!;

          expect(
            tester.widget<Icon>(find.byIcon(Icons.refresh)).color,
            tokens.markIcon,
          );
        },
      );
    }
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
      for (final Size size in dovahResponsiveTestSizes) {
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
