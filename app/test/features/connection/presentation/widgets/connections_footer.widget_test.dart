import 'package:flutter/material.dart';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/presentation/widgets/connections_footer.widget.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/theme/dovah_root_metrics.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_presets.dart';
import 'package:dovahlink_client/shared/theme/dovah_theme_tokens.dart';
import '../../../../shared/theme/widgets/dovah_widget_test_helpers.dart';

/// Exercises [ConnectionsFooter] across every DovahLink theme and both test sizes.
void main() {
  group('ConnectionsFooter renders correctly', () {
    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in const [Size(1280, 720), Size(1600, 900)]) {
        testWidgets(
          'ConnectionsFooter displays the reconnection note under $preset at $size',
          (WidgetTester tester) async {
            await pumpDovahThemedWidget(
              tester,
              const ConnectionsFooter(),
              preset: preset,
              size: size,
            );
            final DovahThemeTokens tokens = dovahThemeDataFor(
              preset,
            ).extension<DovahThemeTokens>()!;
            final Text note = tester.widget(
              find.text(
                'Trusted PCs reconnect automatically when Skyrim becomes available.',
              ),
            );

            expect(tester.takeException(), isNull);
            expect(note.style?.color, tokens.textFaint);
            expect(note.style?.fontSize, isA<double>());
            expect(note.style?.fontSize, DovahRootMetrics.footerFontSize);
          },
        );
      }
    }

    for (final DovahThemePreset preset in DovahThemePreset.values) {
      for (final Size size in const [Size(720, 480), Size(900, 560)]) {
        testWidgets(
          'ConnectionsFooter is hidden under $preset at the compact height $size',
          (WidgetTester tester) async {
            await pumpDovahThemedWidget(
              tester,
              const ConnectionsFooter(),
              preset: preset,
              size: size,
            );

            expect(tester.takeException(), isNull);
            expect(
              find.text(
                'Trusted PCs reconnect automatically when Skyrim becomes available.',
              ),
              findsNothing,
            );
          },
        );
      }
    }

    testWidgets(
      'ConnectionsFooter wraps at double text scale without overflow',
      (WidgetTester tester) async {
        await pumpDovahThemedWidget(
          tester,
          Builder(
            builder: (BuildContext context) => MediaQuery(
              data: MediaQuery.of(
                context,
              ).copyWith(textScaler: const TextScaler.linear(2)),
              child: const ConnectionsFooter(),
            ),
          ),
          preset: DovahThemePreset.hearth,
          size: dovahTestSizes.first,
        );

        expect(tester.takeException(), isNull);
      },
    );
  });
}
